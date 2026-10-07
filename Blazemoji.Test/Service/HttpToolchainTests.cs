using System.Net;
using System.Text;
using Blazemoji.Test.State;
using Blazemoji.Test.Toolchain;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.Service
{
    /// <summary>
    /// The client talking to the real service hosted in memory, with a scripted toolchain behind it.
    /// </summary>
    public sealed class HttpToolchainTests : IDisposable
    {
        private readonly ToolchainServiceFactory _factory = new();
        private readonly ScriptedRun _run = new();

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        public void Dispose() => _factory.Dispose();

        private HttpToolchain CreateClient() => new(_factory.CreateClient(), NullLogger<HttpToolchain>.Instance);

        private static HttpToolchain ClientOver(HttpMessageHandler handler, TimeSpan? timeout = null) =>
            new(new HttpClient(handler) { BaseAddress = new Uri("http://toolchain.test"), Timeout = timeout ?? TimeSpan.FromSeconds(100) }, NullLogger<HttpToolchain>.Instance);

        private async Task<IToolchainRun> StartRunAsync(HttpToolchain client)
        {
            _factory.Toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);
            return await client.StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild), Cancellation);
        }

        [Fact]
        public async Task A_compile_result_comes_back_with_its_build_id_and_diagnostics()
        {
            var warning = new Diagnostic(DiagnosticSeverity.Warning, string.Empty, 0, 0, "A warning.");
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [warning], "build-1"));

            var result = await CreateClient().CompileAsync(ToolchainFixture.SingleFile("🏁 🍇 🍉"), Cancellation);

            result.Ok.ShouldBeTrue();
            result.BuildId.ShouldBe("build-1");
            result.Diagnostics.ShouldBe([warning]);
            await _factory.Toolchain.Received(1).CompileAsync(
                Arg.Is<CompileRequest>(request => request.Entry == "main.🍇" && request.Files["main.🍇"] == "🏁 🍇 🍉"),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_failed_build_comes_back_with_its_error()
        {
            var error = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Variable \"nope\" not defined.");
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false, [error], null));

            var result = await CreateClient().CompileAsync(ToolchainFixture.SingleFile("x"), Cancellation);

            result.Ok.ShouldBeFalse();
            result.BuildId.ShouldBeNull();
            result.Diagnostics.ShouldBe([error]);
        }

        [Fact]
        public async Task A_request_the_service_refuses_becomes_a_failed_build_with_the_services_reason()
        {
            var request = new CompileRequest(new Dictionary<string, string> { ["../x.🍇"] = "x" }, "../x.🍇");

            var result = await CreateClient().CompileAsync(request, Cancellation);

            result.Ok.ShouldBeFalse();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("File names must be relative and must not contain '..' segments.");
        }

        [Fact]
        public async Task A_service_error_becomes_a_failed_build_with_a_fixed_message()
        {
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("secret"));

            var result = await CreateClient().CompileAsync(ToolchainFixture.SingleFile("x"), Cancellation);

            result.Ok.ShouldBeFalse();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("The toolchain service could not compile the program.");
        }

        [Fact]
        public async Task An_unreachable_service_becomes_a_failed_build_not_an_exception()
        {
            var client = ClientOver(new FailingHandler());

            var result = await client.CompileAsync(ToolchainFixture.SingleFile("x"), Cancellation);

            result.Ok.ShouldBeFalse();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("The toolchain service could not be reached.");
        }

        [Fact]
        public async Task A_run_delivers_its_events_in_order_and_then_ends()
        {
            await using var run = await StartRunAsync(CreateClient());
            _run.Emit(new StdoutEvent("one 😀\n"));
            _run.Emit(new StderrEvent("oops\n"));
            _run.Exit(3);

            var finished = await run.RunToEndAsync(Cancellation);

            finished.Stdout.ShouldBe("one 😀\n");
            finished.Stderr.ShouldBe("oops\n");
            finished.Exit.ShouldBe(new ExitEvent(3, RunEndReason.Exited, TimeSpan.FromMilliseconds(120)));
            finished.EventsAfterExit.ShouldBe(0);
        }

        [Theory]
        [InlineData(RunEndReason.Stopped)]
        [InlineData(RunEndReason.TimedOut)]
        [InlineData(RunEndReason.OutputLimit)]
        [InlineData(RunEndReason.FailedToStart)]
        [InlineData(RunEndReason.Idle)]
        public async Task Every_end_reason_survives_the_trip(RunEndReason reason)
        {
            await using var run = await StartRunAsync(CreateClient());
            _run.Exit(null, reason);

            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(reason);
            finished.Exit.ExitCode.ShouldBeNull();
        }

        [Fact]
        public async Task A_run_of_an_unknown_build_reports_that_it_failed_to_start()
        {
            await using var run = await CreateClient().StartRunAsync(new RunRequest("ffffffffffffffffffffffffffffffff"), Cancellation);

            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.FailedToStart);
        }

        [Fact]
        public async Task A_run_against_an_unreachable_service_reports_that_it_failed_to_start()
        {
            await using var run = await ClientOver(new FailingHandler()).StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild), Cancellation);

            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.FailedToStart);
        }

        [Fact]
        public async Task The_environment_is_sent_with_the_run()
        {
            _factory.Toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);
            var environment = new Dictionary<string, string> { ["PORT"] = "8080" };

            await using var run = await CreateClient().StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild, environment), Cancellation);

            await _factory.Toolchain.Received(1).StartRunAsync(
                Arg.Is<RunRequest>(request => request.Environment!["PORT"] == "8080"), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Stop_reaches_the_program()
        {
            await using var run = await StartRunAsync(CreateClient());

            await run.StopAsync();
            var finished = await run.RunToEndAsync(Cancellation);

            _run.StopCalls.ShouldBe(1);
            finished.Exit.Reason.ShouldBe(RunEndReason.Stopped);
        }

        [Fact]
        public async Task Input_reaches_the_program()
        {
            await using var run = await StartRunAsync(CreateClient());

            await run.WriteInputAsync("héllo 😀\n", cancellationToken: Cancellation);
            await run.WriteInputAsync(string.Empty, endOfInput: true, Cancellation);

            _run.Input.ShouldBe([("héllo 😀\n", false), (string.Empty, true)]);
        }

        [Fact]
        public async Task Input_for_a_run_that_has_ended_is_refused()
        {
            await using var run = await StartRunAsync(CreateClient());
            _run.Exit();
            await run.RunToEndAsync(Cancellation);

            await Should.ThrowAsync<InvalidOperationException>(() => run.WriteInputAsync("late", cancellationToken: Cancellation));
        }

        [Fact]
        public async Task Disposing_a_run_that_is_still_going_stops_it()
        {
            var run = await StartRunAsync(CreateClient());

            await run.DisposeAsync();

            _run.StopCalls.ShouldBe(1);
        }

        [Fact]
        public async Task Disposing_a_run_that_has_ended_does_not_stop_it_again()
        {
            var run = await StartRunAsync(CreateClient());
            _run.Exit();
            await run.RunToEndAsync(Cancellation);

            await run.DisposeAsync();

            _run.StopCalls.ShouldBe(0);
        }

        [Fact]
        public async Task A_stream_that_breaks_off_before_the_exit_event_is_an_error()
        {
            var handler = new ScriptedHandler(request => request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.Created, """{"runId":"r1"}""")
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("id: 1\nevent: stdout\ndata: {\"text\":\"partial\"}\n\n", Encoding.UTF8, "text/event-stream"),
                });
            await using var run = await ClientOver(handler).StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild), Cancellation);

            await Should.ThrowAsync<IOException>(() => run.RunToEndAsync(Cancellation));
        }

        [Fact]
        public async Task Releasing_a_build_asks_nothing_of_the_service()
        {
            var handler = new ScriptedHandler(_ => throw new InvalidOperationException("no request expected"));

            await Should.NotThrowAsync(() => ClientOver(handler).ReleaseBuildAsync("build-1"));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        [Fact]
        public async Task A_server_run_is_asked_for_as_one()
        {
            _factory.Toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);

            await using var run = await CreateClient().StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild, Server: true), Cancellation);

            await _factory.Toolchain.Received(1).StartRunAsync(Arg.Is<RunRequest>(request => request.Server), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_request_reaches_the_program_and_its_response_comes_back()
        {
            await using var run = await StartRunAsync(CreateClient());
            _run.Respond = _ => new ProgramResponse(
                ProgramResponseOutcome.Answered,
                201,
                null,
                [new("Location", "/todos/1"), new("Content-Type", "application/json")],
                Encoding.UTF8.GetBytes("{\"id\":1}"),
                TimeSpan.Zero);
            var request = new ProgramRequest(
                "POST",
                "/todos?notify=true",
                [new("X-Api-Key", "vineyard"), new("Content-Type", "application/json")],
                Encoding.UTF8.GetBytes("{\"title\":\"🍇\"}"));

            var response = await run.SendHttpAsync(request, Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            response.StatusCode.ShouldBe(201);
            Encoding.UTF8.GetString(response.Body).ShouldBe("{\"id\":1}");
            response.Headers.ShouldContain(new KeyValuePair<string, string>("Location", "/todos/1"));
            response.Headers.ShouldContain(new KeyValuePair<string, string>("Content-Type", "application/json"));
            response.Duration.ShouldBeGreaterThan(TimeSpan.Zero);

            var seen = _run.Requests.ShouldHaveSingleItem();
            seen.Method.ShouldBe("POST");
            seen.Path.ShouldBe("/todos?notify=true");
            seen.Headers.ShouldContain(new KeyValuePair<string, string>("X-Api-Key", "vineyard"));
            Encoding.UTF8.GetString(seen.Body).ShouldBe("{\"title\":\"🍇\"}");
        }

        [Theory]
        [InlineData(ProgramResponseOutcome.NotAServer)]
        [InlineData(ProgramResponseOutcome.Ended)]
        [InlineData(ProgramResponseOutcome.NotListening)]
        [InlineData(ProgramResponseOutcome.BadResponse)]
        [InlineData(ProgramResponseOutcome.TimedOut)]
        [InlineData(ProgramResponseOutcome.TooLarge)]
        [InlineData(ProgramResponseOutcome.InvalidRequest)]
        public async Task Every_way_a_request_can_go_unanswered_survives_the_trip(ProgramResponseOutcome outcome)
        {
            await using var run = await StartRunAsync(CreateClient());
            _run.Respond = _ => ProgramResponse.Without(outcome);

            var response = await run.SendHttpAsync(new ProgramRequest("GET", "/", [], []), Cancellation);

            response.Outcome.ShouldBe(outcome);
            response.Body.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_programs_own_error_status_is_an_answer()
        {
            await using var run = await StartRunAsync(CreateClient());
            _run.Respond = _ => new ProgramResponse(ProgramResponseOutcome.Answered, 502, null, [], [], TimeSpan.Zero);

            var response = await run.SendHttpAsync(new ProgramRequest("GET", "/", [], []), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            response.StatusCode.ShouldBe(502);
        }

        [Fact]
        public async Task A_request_for_a_run_the_service_no_longer_knows_says_it_has_ended()
        {
            var handler = new ScriptedHandler(request =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.NotFound);
                response.Headers.Add(ProxyReasons.Header, "unknown-run");
                return response;
            });
            await using var run = await RunOverAsync(handler);

            var response = await run.SendHttpAsync(new ProgramRequest("GET", "/", [], []), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Ended);
        }

        [Fact]
        public async Task A_request_when_the_service_cannot_be_reached_says_so()
        {
            var started = false;
            var handler = new ScriptedHandler(request =>
            {
                if (started)
                    throw new HttpRequestException("Connection refused");

                started = true;
                return RunStarted();
            });
            await using var run = await ClientOver(handler).StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild, Server: true), Cancellation);

            var response = await run.SendHttpAsync(new ProgramRequest("GET", "/", [], []), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Unavailable);
        }

        [Fact]
        public async Task A_request_the_service_never_answers_says_the_service_is_unavailable_once_the_client_gives_up()
        {
            var started = false;
            var handler = new RoutingHandler(_ =>
            {
                if (started)
                    return new SilentHandler();

                started = true;
                return new ScriptedHandler(_ => RunStarted());
            });
            await using var run = await ClientOver(handler, TimeSpan.FromMilliseconds(200))
                .StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild, Server: true), Cancellation);

            var response = await run.SendHttpAsync(new ProgramRequest("GET", "/", [], []), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Unavailable);
        }

        [Theory]
        [InlineData("/../../compile")]
        [InlineData("/a/../../../compile")]
        [InlineData("/..%2f..%2fcompile")]
        [InlineData("/%2e%2e/%2E%2E/compile")]
        [InlineData("\\..\\..\\compile")]
        [InlineData("/./x")]
        [InlineData("/x/..")]
        [InlineData("/a%00b")]
        [InlineData("/a%0d%0ab")]
        [InlineData("/a%7Fb")]
        public async Task A_path_that_would_climb_out_of_the_programs_address_space_is_refused_unsent(string path)
        {
            var sent = new List<string>();
            var handler = new ScriptedHandler(request =>
            {
                sent.Add(request.RequestUri!.AbsolutePath);
                return sent.Count == 1 ? RunStarted() : new HttpResponseMessage(HttpStatusCode.OK);
            });
            await using var run = await ClientOver(handler).StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild, Server: true), Cancellation);

            var response = await run.SendHttpAsync(new ProgramRequest("DELETE", path, [], []), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.InvalidRequest);
            sent.ShouldBe(["/runs"]);
        }

        private static HttpResponseMessage RunStarted() => new(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"runId\":\"abc\"}", Encoding.UTF8, "application/json"),
        };

        private static async Task<IToolchainRun> RunOverAsync(HttpMessageHandler afterStart)
        {
            var started = false;
            var handler = new ScriptedHandler(request =>
            {
                if (started)
                    return ((ScriptedHandler)afterStart).Respond(request);

                started = true;
                return RunStarted();
            });
            return await ClientOver(handler).StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild, Server: true), Cancellation);
        }

        [Fact]
        public async Task A_compile_refused_because_the_service_is_busy_carries_the_services_reason()
        {
            var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"title\":\"Too many programs are being compiled. Try again shortly.\"}", Encoding.UTF8, "application/problem+json"),
            });

            var result = await ClientOver(handler).CompileAsync(ToolchainFixture.SingleFile("x"), Cancellation);

            result.Ok.ShouldBeFalse();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("Too many programs are being compiled. Try again shortly.");
        }

        [Fact]
        public async Task A_compile_the_service_never_answers_is_a_failed_build_once_the_client_gives_up()
        {
            var result = await ClientOver(new SilentHandler(), TimeSpan.FromMilliseconds(200)).CompileAsync(ToolchainFixture.SingleFile("x"), Cancellation);

            result.Ok.ShouldBeFalse();
            result.Diagnostics.ShouldHaveSingleItem().Message.ShouldBe("The toolchain service did not answer in time.");
        }

        [Fact]
        public async Task A_compile_the_caller_cancels_is_still_a_cancellation()
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

            await Should.ThrowAsync<OperationCanceledException>(
                () => ClientOver(new SilentHandler(), TimeSpan.FromSeconds(30)).CompileAsync(ToolchainFixture.SingleFile("x"), cancellation.Token));
        }

        [Fact]
        public async Task A_run_the_service_never_answers_fails_to_start_once_the_client_gives_up()
        {
            await using var run = await ClientOver(new SilentHandler(), TimeSpan.FromMilliseconds(200))
                .StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild), Cancellation);

            var finished = await run.RunToEndAsync(Cancellation);

            finished.Exit.Reason.ShouldBe(RunEndReason.FailedToStart);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Stopping_a_run_when_the_service_is_gone_or_silent_does_not_throw(bool silent)
        {
            var started = false;
            HttpMessageHandler afterStart = silent ? new SilentHandler() : new FailingHandler();
            var handler = new RoutingHandler(request =>
            {
                if (started)
                    return afterStart;

                started = true;
                return new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"runId\":\"abc\"}", Encoding.UTF8, "application/json"),
                });
            });
            var run = await ClientOver(handler, TimeSpan.FromMilliseconds(200)).StartRunAsync(new RunRequest(ToolchainServiceFactory.KnownBuild), Cancellation);

            // Awaited directly: a cancelled task would slip past Should.NotThrowAsync.
            await run.StopAsync();
            await run.DisposeAsync();
        }

        /// <summary>Accepts every request and never answers, as a frozen service does.</summary>
        private sealed class SilentHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("unreachable");
            }
        }

        private sealed class RoutingHandler(Func<HttpRequestMessage, HttpMessageHandler> route) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                new HttpMessageInvoker(route(request), disposeHandler: false).SendAsync(request, cancellationToken);
        }

        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("Connection refused");
        }

        private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> Respond => respond;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                try
                {
                    return Task.FromResult(respond(request));
                }
                catch (HttpRequestException exception)
                {
                    return Task.FromException<HttpResponseMessage>(exception);
                }
            }
        }
    }
}
