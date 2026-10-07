using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Blazemoji.Test.State;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using NSubstitute;

namespace Blazemoji.Test.Service
{
    /// <summary>
    /// The route that passes HTTP requests on to a server program, with the real service
    /// hosted in memory and a scripted run standing in for the program.
    /// </summary>
    public sealed class ProxyEndpointTests : IDisposable
    {
        private readonly ToolchainServiceFactory _factory = new();
        private readonly ScriptedRun _run = new();

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        public void Dispose() => _factory.Dispose();

        private static ProgramResponse Answer(int status, string body = "", params KeyValuePair<string, string>[] headers) =>
            new(ProgramResponseOutcome.Answered, status, null, headers, Encoding.UTF8.GetBytes(body), TimeSpan.FromMilliseconds(5));

        private async Task<(HttpClient Client, string RunId)> StartServerAsync(Func<ProgramRequest, ProgramResponse>? respond = null)
        {
            _run.Respond = respond ?? (_ => Answer(200, "ok"));
            _factory.Toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);

            var client = _factory.CreateClient();
            var response = await client.PostAsJsonAsync(ToolchainRoutes.Runs, new StartRunBody(ToolchainServiceFactory.KnownBuild, Http: true), ToolchainJson.Options, Cancellation);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            var runId = (await response.Content.ReadFromJsonAsync<RunStartedBody>(ToolchainJson.Options, Cancellation))!.RunId;
            return (client, runId);
        }

        private static string? ProxyReason(HttpResponseMessage response) =>
            response.Headers.TryGetValues(ProxyReasons.Header, out var values) ? values.Single() : null;

        [Fact]
        public async Task Asking_for_a_server_run_starts_one()
        {
            await StartServerAsync();

            await _factory.Toolchain.Received(1).StartRunAsync(Arg.Is<RunRequest>(request => request.Server), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_run_is_not_a_server_unless_asked()
        {
            _factory.Toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);

            await _factory.CreateClient().PostAsJsonAsync(ToolchainRoutes.Runs, new { buildId = ToolchainServiceFactory.KnownBuild }, Cancellation);

            await _factory.Toolchain.Received(1).StartRunAsync(Arg.Is<RunRequest>(request => !request.Server), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_request_is_handed_to_the_run_with_its_method_path_query_headers_and_body()
        {
            var (client, runId) = await StartServerAsync();
            using var request = new HttpRequestMessage(HttpMethod.Put, $"/runs/{runId}/http/todos/7?done=true&q=a%20b")
            {
                Content = new StringContent("{\"title\":\"🍇\"}", Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Test", "grapes");

            using var response = await client.SendAsync(request, Cancellation);

            var seen = _run.Requests.ShouldHaveSingleItem();
            seen.Method.ShouldBe("PUT");
            seen.Path.ShouldBe("/todos/7?done=true&q=a%20b");
            seen.Headers.ShouldContain(new KeyValuePair<string, string>("X-Test", "grapes"));
            seen.Headers.ShouldContain(header => header.Key == "Content-Type" && header.Value.StartsWith("application/json"));
            Encoding.UTF8.GetString(seen.Body).ShouldBe("{\"title\":\"🍇\"}");
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        [InlineData("HEAD")]
        [InlineData("OPTIONS")]
        public async Task Every_method_is_passed_on(string method)
        {
            var (client, runId) = await StartServerAsync();

            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/runs/{runId}/http/x"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            _run.Requests.ShouldHaveSingleItem().Method.ShouldBe(method);
        }

        [Theory]
        [InlineData("/http", "/")]
        [InlineData("/http/", "/")]
        [InlineData("/http/a%20b/c", "/a%20b/c")]
        [InlineData("/http/?x=1", "/?x=1")]
        public async Task The_path_after_the_route_is_the_path_the_program_sees(string suffix, string expected)
        {
            var (client, runId) = await StartServerAsync();

            using var response = await client.GetAsync($"/runs/{runId}{suffix}", Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            _run.Requests.ShouldHaveSingleItem().Path.ShouldBe(expected);
        }

        [Fact]
        public async Task The_programs_response_comes_back_as_it_is()
        {
            var (client, runId) = await StartServerAsync(_ => Answer(
                418,
                "short and stout",
                new("Content-Type", "text/plain; charset=utf-8"),
                new("X-From-Program", "yes"),
                new("Set-Cookie", "a=1"),
                new("Set-Cookie", "b=2")));

            using var response = await client.GetAsync($"/runs/{runId}/http/teapot", Cancellation);

            ((int)response.StatusCode).ShouldBe(418);
            (await response.Content.ReadAsStringAsync(Cancellation)).ShouldBe("short and stout");
            response.Content.Headers.ContentType!.ToString().ShouldBe("text/plain; charset=utf-8");
            response.Headers.GetValues("X-From-Program").ShouldBe(["yes"]);
            response.Headers.GetValues("Set-Cookie").ShouldBe(["a=1", "b=2"]);
            ProxyReason(response).ShouldBeNull();
        }

        [Fact]
        public async Task A_programs_own_not_found_can_be_told_from_the_services()
        {
            var (client, runId) = await StartServerAsync(_ => Answer(404, "no such todo"));

            using var fromProgram = await client.GetAsync($"/runs/{runId}/http/todos/99", Cancellation);
            using var fromService = await client.GetAsync("/runs/no-such-run/http/todos/99", Cancellation);

            fromProgram.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            ProxyReason(fromProgram).ShouldBeNull();
            fromService.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            ProxyReason(fromService).ShouldBe("unknown-run");
            fromService.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        }

        [Theory]
        [InlineData(ProgramResponseOutcome.NotAServer, 409, "not-a-server", "The run is not a server.")]
        [InlineData(ProgramResponseOutcome.Ended, 409, "ended", "The run has ended.")]
        [InlineData(ProgramResponseOutcome.NotListening, 502, "not-listening", "The program is not accepting connections.")]
        [InlineData(ProgramResponseOutcome.BadResponse, 502, "bad-response", "The program did not send a usable response.")]
        [InlineData(ProgramResponseOutcome.TimedOut, 504, "timed-out", "The program did not answer in time.")]
        [InlineData(ProgramResponseOutcome.TooLarge, 413, "too-large", "The request body is too large.")]
        [InlineData(ProgramResponseOutcome.InvalidRequest, 400, "invalid-request", "The request could not be sent to the program.")]
        public async Task A_request_the_program_did_not_answer_is_a_marked_problem(ProgramResponseOutcome outcome, int status, string reason, string title)
        {
            var (client, runId) = await StartServerAsync(_ => ProgramResponse.Without(outcome));

            using var response = await client.GetAsync($"/runs/{runId}/http/x", Cancellation);

            ((int)response.StatusCode).ShouldBe(status);
            ProxyReason(response).ShouldBe(reason);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
            problem.RootElement.GetProperty("title").GetString().ShouldBe(title);
        }

        [Fact]
        public async Task A_request_body_over_the_limit_is_refused_before_it_reaches_the_program()
        {
            _factory.MaxProxiedRequestBytes = 16;
            var (client, runId) = await StartServerAsync();

            using var response = await client.PostAsync($"/runs/{runId}/http/x", new StringContent(new string('x', 17)), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            ProxyReason(response).ShouldBe("too-large");
            _run.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_request_body_at_the_limit_is_passed_on()
        {
            _factory.MaxProxiedRequestBytes = 16;
            var (client, runId) = await StartServerAsync();

            using var response = await client.PostAsync($"/runs/{runId}/http/x", new StringContent(new string('x', 16)), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            _run.Requests.ShouldHaveSingleItem().Body.Length.ShouldBe(16);
        }

        [Fact]
        public async Task Headers_that_belong_to_the_connection_or_to_the_service_are_not_taken_from_the_program()
        {
            var (client, runId) = await StartServerAsync(_ => Answer(
                200,
                "five!",
                new("Content-Length", "999"),
                new("Transfer-Encoding", "chunked"),
                new("Connection", "close"),
                new(ProxyReasons.Header, "ended")));

            using var response = await client.GetAsync($"/runs/{runId}/http/x", Cancellation);

            (await response.Content.ReadAsStringAsync(Cancellation)).ShouldBe("five!");
            response.Content.Headers.ContentLength.ShouldBe(5);
            response.Headers.TransferEncodingChunked.ShouldNotBe(true);
            ProxyReason(response).ShouldBeNull();
        }

        [Fact]
        public async Task A_response_without_content_is_passed_on_without_a_body()
        {
            var (client, runId) = await StartServerAsync(_ => Answer(204));

            using var response = await client.DeleteAsync($"/runs/{runId}/http/todos/1", Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await response.Content.ReadAsByteArrayAsync(Cancellation)).ShouldBeEmpty();
        }
    }
}
