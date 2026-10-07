using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Blazemoji.Toolchain.Http
{
    /// <summary>
    /// The toolchain as a client of the toolchain service. Failures to reach the service are
    /// reported the way a failed build or a run that could not start is, with fixed messages,
    /// so callers handle one shape of result.
    /// </summary>
    public sealed class HttpToolchain(HttpClient http, ILogger<HttpToolchain> logger) : IToolchain
    {
        public async Task<CompileResult> CompileAsync(CompileRequest request, CancellationToken cancellationToken = default)
        {
            var body = new CompileRequestBody(new Dictionary<string, string>(request.Files), request.Entry);

            try
            {
                using var response = await http.PostAsJsonAsync(ToolchainRoutes.Compile, body, ToolchainJson.Options, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<CompileResponseBody>(ToolchainJson.Options, cancellationToken);
                    if (result is not null)
                        return new CompileResult(result.Ok, result.Diagnostics.Select(diagnostic => diagnostic.ToDiagnostic()).ToList(), result.BuildId);
                }

                if (response.StatusCode == HttpStatusCode.BadRequest && await ProblemTitleAsync(response, cancellationToken) is { } reason)
                    return Failed(reason);

                logger.LogError("The toolchain service answered a compile request with {StatusCode}", (int)response.StatusCode);
                return Failed("The toolchain service could not compile the program.");
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(exception, "The toolchain service could not be reached");
                return Failed("The toolchain service could not be reached.");
            }
        }

        public async Task<IToolchainRun> StartRunAsync(RunRequest request, CancellationToken cancellationToken = default)
        {
            var environment = request.Environment is null ? null : new Dictionary<string, string>(request.Environment);

            try
            {
                using var response = await http.PostAsJsonAsync(ToolchainRoutes.Runs, new StartRunBody(request.BuildId, environment), ToolchainJson.Options, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Created
                    && await response.Content.ReadFromJsonAsync<RunStartedBody>(ToolchainJson.Options, cancellationToken) is { } started)
                {
                    return new HttpRun(http, started.RunId);
                }

                logger.LogError("The toolchain service answered a run request with {StatusCode}", (int)response.StatusCode);
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(exception, "The toolchain service could not be reached");
            }

            return new NeverStartedRun();
        }

        /// <summary>
        /// The service removes builds itself once they are old enough, so there is nothing to ask of it.
        /// </summary>
        public Task ReleaseBuildAsync(string buildId) => Task.CompletedTask;

        private static async Task<string?> ProblemTitleAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                return document.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static CompileResult Failed(string message) =>
            new(false, [new Diagnostic(DiagnosticSeverity.Error, string.Empty, 0, 0, message)], null);

        private sealed class HttpRun(HttpClient http, string runId) : IToolchainRun
        {
            private int _readerTaken;
            private bool _ended;

            public string RunId => runId;

            public async IAsyncEnumerable<RunEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                if (Interlocked.Exchange(ref _readerTaken, 1) == 1)
                    throw new InvalidOperationException("A run supports a single reader.");

                using var request = new HttpRequestMessage(HttpMethod.Get, ToolchainRoutes.RunEvents(runId));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _ended = true;
                    yield return new ExitEvent(null, RunEndReason.FailedToStart, TimeSpan.Zero);
                    yield break;
                }

                await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
                await foreach (var serverEvent in ServerSentEventReader.ReadAsync(body, cancellationToken))
                {
                    switch (serverEvent.Name)
                    {
                        case EventNames.Stdout:
                            yield return new StdoutEvent(Read<OutputEventData>(serverEvent).Text);
                            break;

                        case EventNames.Stderr:
                            yield return new StderrEvent(Read<OutputEventData>(serverEvent).Text);
                            break;

                        case EventNames.Exit:
                            var exit = Read<ExitEventData>(serverEvent);
                            _ended = true;
                            yield return new ExitEvent(exit.ExitCode, RunEndReasons.Parse(exit.Reason), TimeSpan.FromMilliseconds(exit.DurationMs));
                            yield break;
                    }
                }

                throw new IOException("The event stream ended before the run did.");
            }

            public async Task StopAsync()
            {
                using var response = await http.DeleteAsync(ToolchainRoutes.Run(runId));
            }

            public async Task WriteInputAsync(string text, bool endOfInput = false, CancellationToken cancellationToken = default)
            {
                using var content = new StringContent(text, Encoding.UTF8, "text/plain");
                using var response = await http.PostAsync(ToolchainRoutes.RunInput(runId, endOfInput), content, cancellationToken);

                if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
                    throw new InvalidOperationException("The run is not accepting input.");

                response.EnsureSuccessStatusCode();
            }

            public async ValueTask DisposeAsync()
            {
                if (_ended)
                    return;

                _ended = true;
                try
                {
                    await StopAsync();
                }
                catch (HttpRequestException)
                {
                    // The service is gone, and the run with it.
                }
            }

            private static T Read<T>(ServerSentEvent serverEvent) =>
                JsonSerializer.Deserialize<T>(serverEvent.Data, ToolchainJson.EventOptions)
                ?? throw new IOException($"The {serverEvent.Name} event carried no data.");
        }

        private sealed class NeverStartedRun : IToolchainRun
        {
            public string RunId => string.Empty;

            public async IAsyncEnumerable<RunEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.CompletedTask;
                yield return new ExitEvent(null, RunEndReason.FailedToStart, TimeSpan.Zero);
            }

            public Task StopAsync() => Task.CompletedTask;

            public Task WriteInputAsync(string text, bool endOfInput = false, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("The run is not accepting input.");

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
