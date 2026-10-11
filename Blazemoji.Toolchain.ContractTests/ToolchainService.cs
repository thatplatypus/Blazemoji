using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Blazemoji.Toolchain.ContractTests
{
    public sealed record ReceivedEvent(string? Id, string Name, JsonElement Data, TimeSpan At);

    /// <summary>
    /// Plain HTTP against whatever is at TOOLCHAIN_BASE_URL. Nothing here knows how the
    /// service is built.
    /// </summary>
    public static class ToolchainService
    {
        public const string SkipReason = "Set TOOLCHAIN_BASE_URL to a toolchain service to run the contract tests.";

        private static readonly Lazy<HttpClient> Client = new(() => new HttpClient
        {
            BaseAddress = new Uri(BaseUrl!.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(2),
        });

        public static string? BaseUrl => Environment.GetEnvironmentVariable("TOOLCHAIN_BASE_URL");

        public static HttpClient Http => Client.Value;

        public static StringContent Json(object body) =>
            new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        public static Task<HttpResponseMessage> PostCompileAsync(object body, CancellationToken cancellationToken) =>
            Http.PostAsync("compile", Json(body), cancellationToken);

        public static async Task<JsonElement> CompileAsync(string code, CancellationToken cancellationToken)
        {
            using var response = await PostCompileAsync(new { files = new Dictionary<string, string> { ["main.🍇"] = code }, entry = "main.🍇" }, cancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            return await ReadJsonAsync(response, cancellationToken);
        }

        public static async Task<string> BuildAsync(string code, CancellationToken cancellationToken)
        {
            var result = await CompileAsync(code, cancellationToken);
            result.GetProperty("ok").GetBoolean().ShouldBeTrue(result.GetRawText());
            return result.GetProperty("buildId").GetString()!;
        }

        public static async Task<string> StartRunAsync(string buildId, CancellationToken cancellationToken, Dictionary<string, string>? env = null)
        {
            using var response = await PostRunAsync(buildId, cancellationToken, env);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await ReadJsonAsync(response, cancellationToken)).GetProperty("runId").GetString()!;
        }

        /// <summary>
        /// "env" is optional in the contract, so it is left out when there is none to send.
        /// </summary>
        public static Task<HttpResponseMessage> PostRunAsync(string buildId, CancellationToken cancellationToken, Dictionary<string, string>? env = null)
        {
            var body = new Dictionary<string, object> { ["buildId"] = buildId };
            if (env is not null)
                body["env"] = env;

            return Http.PostAsync("runs", Json(body), cancellationToken);
        }

        public static Task<HttpResponseMessage> PostDependenciesAsync(string manifest, string lockfile, CancellationToken cancellationToken) =>
            Http.PostAsync("dependencies", Json(new { manifest, lockfile }), cancellationToken);

        public static Task<HttpResponseMessage> PutArchiveAsync(string name, string version, string sha256, byte[] archive, CancellationToken cancellationToken) =>
            Http.PutAsync(
                $"dependencies/archives/{sha256}?name={Uri.EscapeDataString(name)}&version={Uri.EscapeDataString(version)}",
                new ByteArrayContent(archive),
                cancellationToken);

        /// <summary>
        /// Asks for a set until it is ready, handing over whatever is asked for on the way and
        /// asking again while the service says to. Gives the last answer, whatever it was.
        /// </summary>
        public static async Task<JsonElement> PrepareAsync(string manifest, string lockfile, IReadOnlyDictionary<string, byte[]> archivesByDigest, CancellationToken cancellationToken)
        {
            for (var asked = 0; ; asked++)
            {
                using var response = await PostDependenciesAsync(manifest, lockfile, cancellationToken);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && asked < 60)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                response.StatusCode.ShouldBe(HttpStatusCode.OK);
                var answer = await ReadJsonAsync(response, cancellationToken);
                if (answer.GetProperty("state").GetString() != "needed" || asked >= 60)
                    return answer;

                foreach (var needed in answer.GetProperty("needed").EnumerateArray())
                {
                    var sha256 = needed.GetProperty("sha256").GetString()!;
                    archivesByDigest.ContainsKey(sha256).ShouldBeTrue("the service asked for an archive the lockfile does not name");

                    using var put = await PutArchiveAsync(needed.GetProperty("name").GetString()!, needed.GetProperty("version").GetString()!, sha256, archivesByDigest[sha256], cancellationToken);
                    put.StatusCode.ShouldBe(HttpStatusCode.NoContent);
                }
            }
        }

        public static string MediaType(HttpResponseMessage response) =>
            response.Content.Headers.ContentType.ShouldNotBeNull("the response has no Content-Type").MediaType.ShouldNotBeNull();

        public static async Task<string> BuildAndStartAsync(string code, CancellationToken cancellationToken) =>
            await StartRunAsync(await BuildAsync(code, cancellationToken), cancellationToken);

        public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.Clone();
        }

        public static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, CancellationToken cancellationToken)
        {
            response.StatusCode.ShouldBe(status);
            MediaType(response).ShouldBe("application/problem+json");
            (await ReadJsonAsync(response, cancellationToken)).GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        }

        /// <summary>
        /// Reads a run's event stream to its end and says when each event arrived.
        /// </summary>
        public static async Task<List<ReceivedEvent>> ReadEventsAsync(string runId, CancellationToken cancellationToken, string? lastEventId = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"runs/{runId}/events");
            if (lastEventId is not null)
                request.Headers.Add("Last-Event-ID", lastEventId);

            var clock = Stopwatch.StartNew();
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            MediaType(response).ShouldBe("text/event-stream");

            var events = new List<ReceivedEvent>();
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            await foreach (var (id, name, data) in ParseAsync(body, cancellationToken))
            {
                using var document = JsonDocument.Parse(data);
                events.Add(new ReceivedEvent(id, name, document.RootElement.Clone(), clock.Elapsed));
            }

            return events;
        }

        public static string Text(IEnumerable<ReceivedEvent> events, string name) =>
            string.Concat(events.Where(e => e.Name == name).Select(e => e.Data.GetProperty("text").GetString()));

        /// <summary>
        /// The server-sent events format, as much of it as the contract uses: "field: value"
        /// lines, a blank line between events, lines starting with a colon ignored.
        /// </summary>
        private static async IAsyncEnumerable<(string? Id, string Name, string Data)> ParseAsync(Stream stream, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? id = null;
            string? name = null;
            var data = new StringBuilder();
            var hasData = false;

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.Length == 0)
                {
                    if (name is not null || hasData)
                        yield return (id, name ?? "message", data.ToString());

                    id = null;
                    name = null;
                    data.Clear();
                    hasData = false;
                    continue;
                }

                if (line[0] == ':')
                    continue;

                var colon = line.IndexOf(':');
                var field = colon < 0 ? line : line[..colon];
                var value = colon < 0 ? string.Empty : line[(colon + 1)..];
                if (value.StartsWith(' '))
                    value = value[1..];

                if (field == "id")
                {
                    id = value;
                }
                else if (field == "event")
                {
                    name = value;
                }
                else if (field == "data")
                {
                    if (hasData)
                        data.Append('\n');

                    data.Append(value);
                    hasData = true;
                }
            }
        }
    }
}
