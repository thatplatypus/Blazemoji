using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Blazemoji.Test.State;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.Service
{
    public sealed class ToolchainServiceTests : IDisposable
    {
        private readonly ToolchainServiceFactory _factory = new();
        private readonly ScriptedRun _run = new();

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private HttpClient Client => _factory.CreateClient();

        public void Dispose() => _factory.Dispose();

        private void RunsStart() =>
            _factory.Toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);

        private async Task<string> StartRunAsync(HttpClient client)
        {
            RunsStart();
            var response = await client.PostAsJsonAsync(ToolchainRoutes.Runs, new StartRunBody(ToolchainServiceFactory.KnownBuild), Cancellation);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<RunStartedBody>(ToolchainJson.Options, Cancellation))!.RunId;
        }

        private static async Task<List<ServerSentEvent>> ReadEventsAsync(HttpClient client, string runId, string? lastEventId = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ToolchainRoutes.RunEvents(runId));
            if (lastEventId is not null)
                request.Headers.Add("Last-Event-ID", lastEventId);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Cancellation);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");

            var events = new List<ServerSentEvent>();
            await using var body = await response.Content.ReadAsStreamAsync(Cancellation);
            await foreach (var serverEvent in ServerSentEventReader.ReadAsync(body, Cancellation))
                events.Add(serverEvent);

            return events;
        }

        private static async Task<string> ProblemTitleAsync(HttpResponseMessage response)
        {
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
            return document.RootElement.GetProperty("title").GetString()!;
        }

        [Fact]
        public async Task Health_answers_ok()
        {
            var response = await Client.GetAsync(ToolchainRoutes.Health, Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task A_successful_compile_returns_ok_and_a_build_id()
        {
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [], "build-1"));

            var response = await Client.PostAsync(ToolchainRoutes.Compile,
                new StringContent("""{"files":{"main.🍇":"🏁 🍇 🍉"},"entry":"main.🍇"}""", Encoding.UTF8, "application/json"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync(Cancellation)).ShouldBe("""{"ok":true,"diagnostics":[],"buildId":"build-1"}""");
            await _factory.Toolchain.Received(1).CompileAsync(
                Arg.Is<CompileRequest>(request => request.Entry == "main.🍇" && request.Files["main.🍇"] == "🏁 🍇 🍉"),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_compile_beyond_the_concurrency_limit_is_a_429_until_one_finishes()
        {
            _factory.MaxConcurrentCompiles = 1;
            var compiling = new TaskCompletionSource<CompileResult>();
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>()).Returns(compiling.Task);
            var client = Client;
            var body = new CompileRequestBody(new() { ["main.🍇"] = "x" }, "main.🍇");

            var first = client.PostAsJsonAsync(ToolchainRoutes.Compile, body, ToolchainJson.Options, Cancellation);
            while (_factory.Toolchain.ReceivedCalls().Count() == 0)
                await Task.Delay(5, Cancellation);
            var refused = await client.PostAsJsonAsync(ToolchainRoutes.Compile, body, ToolchainJson.Options, Cancellation);

            refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
            (await ProblemTitleAsync(refused)).ShouldBe("Too many programs are being compiled. Try again shortly.");

            compiling.SetResult(new CompileResult(true, [], "build-1"));
            (await first).StatusCode.ShouldBe(HttpStatusCode.OK);
            var afterwards = await client.PostAsJsonAsync(ToolchainRoutes.Compile, body, ToolchainJson.Options, Cancellation);
            afterwards.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task A_compile_that_throws_gives_its_place_back()
        {
            _factory.MaxConcurrentCompiles = 1;
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns<CompileResult>(_ => throw new InvalidOperationException("boom"), _ => new CompileResult(true, [], "build-2"));
            var client = Client;
            var body = new CompileRequestBody(new() { ["main.🍇"] = "x" }, "main.🍇");

            var failed = await client.PostAsJsonAsync(ToolchainRoutes.Compile, body, ToolchainJson.Options, Cancellation);
            var next = await client.PostAsJsonAsync(ToolchainRoutes.Compile, body, ToolchainJson.Options, Cancellation);

            failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            next.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task A_check_only_compile_is_passed_on_as_one_and_answers_without_a_build_id()
        {
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [], null));

            var response = await Client.PostAsync(ToolchainRoutes.Compile,
                new StringContent("""{"files":{"main.🍇":"🏁 🍇 🍉"},"entry":"main.🍇","check":true}""", Encoding.UTF8, "application/json"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync(Cancellation)).ShouldBe("""{"ok":true,"diagnostics":[]}""");
            await _factory.Toolchain.Received(1).CompileAsync(Arg.Is<CompileRequest>(request => request.CheckOnly), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_compile_is_a_full_build_unless_it_asks_to_be_checked_only()
        {
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [], "build-1"));

            await Client.PostAsJsonAsync(ToolchainRoutes.Compile, new { files = new Dictionary<string, string> { ["main.🍇"] = "x" }, entry = "main.🍇" }, Cancellation);

            await _factory.Toolchain.Received(1).CompileAsync(Arg.Is<CompileRequest>(request => !request.CheckOnly), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_failed_compile_is_still_a_200_with_diagnostics_and_no_build_id()
        {
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false,
                [
                    new Diagnostic(DiagnosticSeverity.Warning, string.Empty, 0, 0, "A warning."),
                    new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Variable \"nope\" not defined."),
                ], null));

            var response = await Client.PostAsJsonAsync(ToolchainRoutes.Compile,
                new CompileRequestBody(new() { ["main.🍇"] = "x" }, "main.🍇"), ToolchainJson.Options, Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
            var root = document.RootElement;
            root.GetProperty("ok").GetBoolean().ShouldBeFalse();
            root.TryGetProperty("buildId", out JsonElement absent).ShouldBeFalse();
            var diagnostics = root.GetProperty("diagnostics");
            diagnostics.GetArrayLength().ShouldBe(2);
            diagnostics[0].GetProperty("severity").GetString().ShouldBe("warning");
            diagnostics[1].GetProperty("severity").GetString().ShouldBe("error");
            diagnostics[1].GetProperty("file").GetString().ShouldBe("main.🍇");
            diagnostics[1].GetProperty("line").GetInt32().ShouldBe(2);
            diagnostics[1].GetProperty("character").GetInt32().ShouldBe(5);
            diagnostics[1].GetProperty("message").GetString().ShouldBe("Variable \"nope\" not defined.");
        }

        [Theory]
        [InlineData("""{"entry":"main.🍇"}""", "At least one file is required.")]
        [InlineData("""{"files":{},"entry":"main.🍇"}""", "At least one file is required.")]
        [InlineData("""{"files":{"main.🍇":"x"}}""", "The entry must name one of the files.")]
        [InlineData("""{"files":{"main.🍇":"x"},"entry":"other.🍇"}""", "The entry must name one of the files.")]
        [InlineData("""{"files":{"../main.🍇":"x"},"entry":"../main.🍇"}""", "File names must be relative and must not contain '..' segments.")]
        [InlineData("""{"files":{"main.🍇":"x"},"entry":"main.🍇","packages":["nope"]}""", "An unknown package was requested.")]
        public async Task A_compile_request_that_cannot_be_right_is_a_400_and_never_reaches_the_compiler(string body, string title)
        {
            var response = await Client.PostAsync(ToolchainRoutes.Compile, new StringContent(body, Encoding.UTF8, "application/json"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ProblemTitleAsync(response)).ShouldBe(title);
            await _factory.Toolchain.DidNotReceive().CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_body_that_is_not_json_is_a_400()
        {
            var response = await Client.PostAsync(ToolchainRoutes.Compile, new StringContent("not json", Encoding.UTF8, "application/json"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Known_packages_may_be_named()
        {
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [], "build-1"));

            var response = await Client.PostAsync(ToolchainRoutes.Compile,
                new StringContent("""{"files":{"main.🍇":"x"},"entry":"main.🍇","packages":["s","json"]}""", Encoding.UTF8, "application/json"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task A_compiler_failure_is_a_500_that_gives_nothing_away()
        {
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("secret path /srv/keys"));

            var response = await Client.PostAsJsonAsync(ToolchainRoutes.Compile,
                new CompileRequestBody(new() { ["main.🍇"] = "x" }, "main.🍇"), ToolchainJson.Options, Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            (await response.Content.ReadAsStringAsync(Cancellation)).ShouldNotContain("secret");
        }

        [Fact]
        public async Task Starting_a_run_of_an_unknown_build_is_a_404()
        {
            var response = await Client.PostAsJsonAsync(ToolchainRoutes.Runs, new StartRunBody("ffffffffffffffffffffffffffffffff"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ProblemTitleAsync(response)).ShouldBe("No such build.");
            await _factory.Toolchain.DidNotReceive().StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Starting_a_run_without_a_build_id_is_a_400()
        {
            var response = await Client.PostAsync(ToolchainRoutes.Runs, new StringContent("{}", Encoding.UTF8, "application/json"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Starting_a_run_passes_the_environment_on()
        {
            RunsStart();

            var response = await Client.PostAsync(ToolchainRoutes.Runs,
                new StringContent("{\"buildId\":\"" + ToolchainServiceFactory.KnownBuild + "\",\"env\":{\"PORT\":\"8080\"}}", Encoding.UTF8, "application/json"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            await _factory.Toolchain.Received(1).StartRunAsync(
                Arg.Is<RunRequest>(request => request.BuildId == ToolchainServiceFactory.KnownBuild && request.Environment!["PORT"] == "8080"),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task The_event_stream_carries_numbered_stdout_stderr_and_exit_events_then_ends()
        {
            // System.Text.Json always writes characters outside the Basic Multilingual Plane as
            // escapes, so an emoji arrives as a surrogate pair escape. Any JSON parser reads it back.
            var client = Client;
            var runId = await StartRunAsync(client);
            _run.Emit(new StdoutEvent("one\n"));
            _run.Emit(new StderrEvent("oops 😀\n"));
            _run.Exit(3);

            var events = await ReadEventsAsync(client, runId);

            events.ShouldBe(
            [
                new ServerSentEvent("1", "stdout", """{"text":"one\n"}"""),
                new ServerSentEvent("2", "stderr", "{\"text\":\"oops \\uD83D\\uDE00\\n\"}"),
                new ServerSentEvent("3", "exit", """{"exitCode":3,"reason":"exited","durationMs":120}"""),
            ]);
        }

        [Fact]
        public async Task A_client_that_connects_while_the_program_runs_sees_events_as_they_happen()
        {
            var client = Client;
            var runId = await StartRunAsync(client);
            _run.Emit(new StdoutEvent("early\n"));

            var reading = ReadEventsAsync(client, runId);
            _run.Emit(new StdoutEvent("late\n"));
            _run.Exit();
            var events = await reading;

            events.Select(e => e.Name).ShouldBe(["stdout", "stdout", "exit"]);
        }

        [Fact]
        public async Task A_client_can_resume_after_the_last_event_it_saw()
        {
            var client = Client;
            var runId = await StartRunAsync(client);
            _run.Emit(new StdoutEvent("one\n"));
            _run.Emit(new StdoutEvent("two\n"));
            _run.Exit();
            await ReadEventsAsync(client, runId);

            var events = await ReadEventsAsync(client, runId, lastEventId: "1");

            events.Select(e => e.Id).ShouldBe(["2", "3"]);
        }

        [Fact]
        public async Task A_run_that_could_not_start_reports_it_with_a_null_exit_code()
        {
            var client = Client;
            var runId = await StartRunAsync(client);
            _run.Exit(null, RunEndReason.FailedToStart);

            var events = await ReadEventsAsync(client, runId);

            events.ShouldHaveSingleItem().Data.ShouldBe("""{"exitCode":null,"reason":"failedToStart","durationMs":120}""");
        }

        [Fact]
        public async Task Events_of_an_unknown_run_are_a_404()
        {
            var response = await Client.GetAsync(ToolchainRoutes.RunEvents("nope"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ProblemTitleAsync(response)).ShouldBe("No such run.");
        }

        [Fact]
        public async Task Delete_stops_the_run()
        {
            var client = Client;
            var runId = await StartRunAsync(client);

            var response = await client.DeleteAsync(ToolchainRoutes.Run(runId), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            _run.StopCalls.ShouldBe(1);
            (await ReadEventsAsync(client, runId)).ShouldHaveSingleItem().Data.ShouldContain("\"reason\":\"stopped\"");
        }

        [Fact]
        public async Task Deleting_a_run_that_has_already_ended_is_still_a_204()
        {
            var client = Client;
            var runId = await StartRunAsync(client);
            _run.Exit();
            await ReadEventsAsync(client, runId);

            var response = await client.DeleteAsync(ToolchainRoutes.Run(runId), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task Deleting_an_unknown_run_is_a_404()
        {
            var response = await Client.DeleteAsync(ToolchainRoutes.Run("nope"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task Text_posted_to_stdin_reaches_the_run()
        {
            var client = Client;
            var runId = await StartRunAsync(client);

            var first = await client.PostAsync(ToolchainRoutes.RunInput(runId, endOfInput: false), new StringContent("héllo 😀\n", Encoding.UTF8, "text/plain"), Cancellation);
            var second = await client.PostAsync(ToolchainRoutes.RunInput(runId, endOfInput: true), new StringContent(string.Empty), Cancellation);

            first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            _run.Input.ShouldBe([("héllo 😀\n", false), (string.Empty, true)]);
        }

        [Fact]
        public async Task Stdin_for_a_run_that_has_ended_is_a_409()
        {
            var client = Client;
            var runId = await StartRunAsync(client);
            _run.Exit();
            await ReadEventsAsync(client, runId);

            var response = await client.PostAsync(ToolchainRoutes.RunInput(runId, endOfInput: false), new StringContent("late"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ProblemTitleAsync(response)).ShouldBe("The run is not accepting input.");
        }

        [Fact]
        public async Task Stdin_for_an_unknown_run_is_a_404()
        {
            var response = await Client.PostAsync(ToolchainRoutes.RunInput("nope", endOfInput: false), new StringContent("x"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task A_run_beyond_the_concurrency_limit_is_a_429_until_one_ends()
        {
            _factory.MaxConcurrentRuns = 1;
            var client = Client;
            var runId = await StartRunAsync(client);

            var refused = await client.PostAsJsonAsync(ToolchainRoutes.Runs, new StartRunBody(ToolchainServiceFactory.KnownBuild), Cancellation);
            refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

            _run.Exit();
            await ReadEventsAsync(client, runId);
            var second = new ScriptedRun();
            _factory.Toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(second);
            var accepted = await PostUntilAcceptedAsync(client);

            accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        [Fact]
        public async Task A_run_that_fails_to_start_does_not_use_up_a_slot()
        {
            _factory.MaxConcurrentRuns = 1;
            var client = Client;
            _factory.Toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("disk"));
            var failed = await client.PostAsJsonAsync(ToolchainRoutes.Runs, new StartRunBody(ToolchainServiceFactory.KnownBuild), Cancellation);
            failed.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

            await StartRunAsync(client);
        }

        [Fact]
        public async Task A_finished_run_is_forgotten_and_disposed_after_its_retention_time()
        {
            var client = Client;
            var runId = await StartRunAsync(client);
            _run.Exit();
            await ReadEventsAsync(client, runId);

            var waited = System.Diagnostics.Stopwatch.StartNew();
            HttpStatusCode status;
            do
            {
                waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "the run was never removed");
                _factory.Time.Advance(TimeSpan.FromMinutes(1));
                await Task.Delay(20, Cancellation);
                status = (await client.GetAsync(ToolchainRoutes.RunEvents(runId), Cancellation)).StatusCode;
            }
            while (status != HttpStatusCode.NotFound);

            _run.Disposed.ShouldBeTrue();
        }

        [Fact]
        public async Task A_run_still_going_is_not_removed_however_old_it_is()
        {
            var client = Client;
            var runId = await StartRunAsync(client);

            _factory.Time.Advance(TimeSpan.FromHours(1));
            await Task.Delay(100, Cancellation);

            _run.Exit();
            (await ReadEventsAsync(client, runId)).ShouldHaveSingleItem().Name.ShouldBe("exit");
        }

        [Fact]
        public async Task The_package_list_names_only_folders_that_hold_documentation()
        {
            var names = await Client.GetFromJsonAsync<string[]>(ToolchainRoutes.Packages, Cancellation);

            names.ShouldBe(["json", "s"]);
        }

        [Fact]
        public async Task Package_documentation_is_served_as_json()
        {
            var response = await Client.GetAsync(ToolchainRoutes.PackageDocumentation("s"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
            (await response.Content.ReadAsStringAsync(Cancellation)).ShouldContain("\"types\"");
        }

        [Theory]
        [InlineData("nope")]
        [InlineData("not-a-package")]
        [InlineData("..")]
        [InlineData("s%2F..%2Fjson")]
        public async Task Documentation_for_anything_that_is_not_a_known_package_is_a_404(string name)
        {
            var response = await Client.GetAsync($"/packages/{name}/documentation.json", Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        private static async Task<HttpResponseMessage> PostUntilAcceptedAsync(HttpClient client)
        {
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                var response = await client.PostAsJsonAsync(ToolchainRoutes.Runs, new StartRunBody(ToolchainServiceFactory.KnownBuild), Cancellation);
                if (response.StatusCode != HttpStatusCode.TooManyRequests || waited.Elapsed > TimeSpan.FromSeconds(5))
                    return response;

                await Task.Delay(20, Cancellation);
            }
        }
    }
}
