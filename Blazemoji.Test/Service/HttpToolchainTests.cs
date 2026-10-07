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

        private static HttpToolchain ClientOver(HttpMessageHandler handler) =>
            new(new HttpClient(handler) { BaseAddress = new Uri("http://toolchain.test") }, NullLogger<HttpToolchain>.Instance);

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

        private sealed class FailingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("Connection refused");
        }

        private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(respond(request));
        }
    }
}
