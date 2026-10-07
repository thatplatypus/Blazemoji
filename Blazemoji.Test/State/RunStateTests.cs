using System.Diagnostics;
using Blazemoji.Shared.State;
using Blazemoji.Toolchain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.State
{
    public class RunStateTests
    {
        private const string Code = "🏁 🍇 🍉";

        private readonly IToolchain _toolchain = Substitute.For<IToolchain>();
        private readonly FakeTimeProvider _time = new();
        private readonly ScriptedRun _run = new();

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private RunState CreateState() => new(_toolchain, NullLogger<RunState>.Instance, _time);

        private void CompileSucceeds(params Diagnostic[] diagnostics)
        {
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, diagnostics, "build-1"));
            _toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>())
                .Returns<IToolchainRun>(_run);
        }

        /// <summary>
        /// Waits for the state's own async flow to catch up. Advancing the fake clock is what
        /// lets a pending output notification fire.
        /// </summary>
        private async Task UntilAsync(Func<bool> condition, bool advanceTime = true)
        {
            var waited = Stopwatch.StartNew();
            while (!condition())
            {
                waited.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), "the expected state was never reached");
                if (advanceTime)
                    _time.Advance(TimeSpan.FromMilliseconds(50));
                await Task.Delay(5, Cancellation);
            }
        }

        [Fact]
        public async Task A_compile_error_publishes_diagnostics_and_does_not_run()
        {
            var error = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Variable \"nope\" not defined.");
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false, [error], null));
            await using var state = CreateState();
            var diagnosticsChanged = 0;
            state.DiagnosticsChanged += () => diagnosticsChanged++;

            await state.RunAsync(Code);

            state.Status.ShouldBe(RunStatus.Idle);
            state.Diagnostics.ShouldBe([error]);
            state.DiagnosticsSource.ShouldBe(Code);
            diagnosticsChanged.ShouldBeGreaterThanOrEqualTo(1);
            state.LastRun.ShouldBeNull();
            await _toolchain.DidNotReceive().StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task The_code_is_compiled_as_a_single_entry_file()
        {
            CompileSucceeds();
            await using var state = CreateState();

            var running = state.RunAsync(Code);
            _run.Exit();
            await running;

            await _toolchain.Received(1).CompileAsync(
                Arg.Is<CompileRequest>(request => request.Entry == "main.🍇" && request.Files.Count == 1 && request.Files["main.🍇"] == Code),
                Arg.Any<CancellationToken>());
            await _toolchain.Received(1).StartRunAsync(Arg.Is<RunRequest>(request => request.BuildId == "build-1"), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_run_moves_through_compiling_and_running_back_to_idle()
        {
            CompileSucceeds();
            await using var state = CreateState();
            var statuses = new List<RunStatus>();
            state.StateChanged += () =>
            {
                if (statuses.Count == 0 || statuses[^1] != state.Status)
                    statuses.Add(state.Status);
            };

            var running = state.RunAsync(Code);
            _run.Emit(new StdoutEvent("one\ntwo\n"));
            _run.Exit(0);
            await running;

            statuses.ShouldBe([RunStatus.Compiling, RunStatus.Running, RunStatus.Idle]);
            state.Lines.ShouldBe(
            [
                new OutputLine(1, OutputStream.Stdout, "one"),
                new OutputLine(2, OutputStream.Stdout, "two"),
            ]);
            state.TotalLines.ShouldBe(2);
            state.LastRun.ShouldBe(new RunSummary(0, RunEndReason.Exited, TimeSpan.FromMilliseconds(120)));
        }

        [Fact]
        public async Task The_build_is_released_and_the_run_disposed_when_the_run_ends()
        {
            CompileSucceeds();
            await using var state = CreateState();

            var running = state.RunAsync(Code);
            _run.Exit();
            await running;

            await _toolchain.Received(1).ReleaseBuildAsync("build-1");
            _run.Disposed.ShouldBeTrue();
        }

        [Fact]
        public async Task The_programs_input_is_closed_at_once_because_the_page_cannot_supply_any()
        {
            CompileSucceeds();
            await using var state = CreateState();

            var running = state.RunAsync(Code);
            _run.Exit();
            await running;

            _run.Input.ShouldBe([(string.Empty, true)]);
        }

        [Fact]
        public async Task Stderr_text_becomes_stderr_lines()
        {
            CompileSucceeds();
            await using var state = CreateState();

            var running = state.RunAsync(Code);
            _run.Emit(new StdoutEvent("out\n"));
            _run.Emit(new StderrEvent("err\n"));
            _run.Exit(1);
            await running;

            state.Lines.ShouldBe(
            [
                new OutputLine(1, OutputStream.Stdout, "out"),
                new OutputLine(2, OutputStream.Stderr, "err"),
            ]);
            state.LastRun!.ExitCode.ShouldBe(1);
        }

        [Fact]
        public async Task Text_without_a_final_newline_is_shown_when_the_run_ends()
        {
            CompileSucceeds();
            await using var state = CreateState();

            var running = state.RunAsync(Code);
            _run.Emit(new StdoutEvent("no newline"));
            _run.Exit();
            await running;

            state.Lines.ShouldHaveSingleItem().Text.ShouldBe("no newline");
        }

        [Fact]
        public async Task Only_the_last_lines_are_kept_and_numbering_stays_true()
        {
            CompileSucceeds();
            await using var state = CreateState();
            var total = RunState.MaxLines + 10;

            var running = state.RunAsync(Code);
            _run.Emit(new StdoutEvent(string.Concat(Enumerable.Range(1, total).Select(i => $"line {i}\n"))));
            _run.Exit();
            await running;

            state.Lines.Count.ShouldBe(RunState.MaxLines);
            state.TotalLines.ShouldBe(total);
            state.Lines[0].ShouldBe(new OutputLine(11, OutputStream.Stdout, "line 11"));
            state.Lines[^1].Number.ShouldBe(total);
        }

        [Fact]
        public async Task A_burst_followed_by_silence_is_still_announced()
        {
            CompileSucceeds();
            await using var state = CreateState();
            var running = state.RunAsync(Code);
            var notifications = 0;
            state.StateChanged += () => notifications++;

            _run.Emit(new StdoutEvent("one\n"));
            _run.Emit(new StdoutEvent("two\n"));
            await UntilAsync(() => state.TotalLines == 2, advanceTime: false);
            notifications.ShouldBe(0, "output inside the throttle window is announced together, not once per chunk");

            await UntilAsync(() => notifications > 0);

            notifications.ShouldBe(1);
            state.Status.ShouldBe(RunStatus.Running);
            state.Lines.Select(line => line.Text).ShouldBe(["one", "two"]);

            _run.Exit();
            await running;
        }

        [Fact]
        public async Task Stop_stops_the_run_and_says_so()
        {
            CompileSucceeds();
            await using var state = CreateState();
            var running = state.RunAsync(Code);

            await state.StopAsync();
            await running;

            _run.StopCalls.ShouldBe(1);
            state.Status.ShouldBe(RunStatus.Idle);
            state.LastRun!.Reason.ShouldBe(RunEndReason.Stopped);
            state.Lines.ShouldHaveSingleItem().ShouldBe(new OutputLine(1, OutputStream.System, "Stopped."));
        }

        [Fact]
        public async Task A_project_is_compiled_from_all_its_files_starting_at_its_entry()
        {
            CompileSucceeds();
            await using var state = CreateState();
            var files = new Dictionary<string, string> { ["app/main.🍇"] = "main", ["shared/util.🍇"] = "util" };

            var running = state.RunAsync(new RunTarget(files, "app/main.🍇", Server: false));
            _run.Exit();
            await running;

            await _toolchain.Received(1).CompileAsync(
                Arg.Is<CompileRequest>(request => request.Entry == "app/main.🍇" && request.Files.Count == 2 && request.Files["shared/util.🍇"] == "util"),
                Arg.Any<CancellationToken>());
            await _toolchain.Received(1).StartRunAsync(Arg.Is<RunRequest>(request => !request.Server), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_server_project_is_started_as_a_server_and_says_so_while_it_runs()
        {
            CompileSucceeds();
            await using var state = CreateState();
            state.ServerRunning.ShouldBeFalse();

            var running = state.RunAsync(new RunTarget(new Dictionary<string, string> { ["main.🍇"] = Code }, "main.🍇", Server: true));
            await UntilAsync(() => state.Status == RunStatus.Running, advanceTime: false);

            state.ServerRunning.ShouldBeTrue();
            await _toolchain.Received(1).StartRunAsync(Arg.Is<RunRequest>(request => request.Server), Arg.Any<CancellationToken>());

            _run.Exit();
            await running;
            state.ServerRunning.ShouldBeFalse();
        }

        [Fact]
        public async Task A_plain_program_is_not_a_server_while_it_runs()
        {
            CompileSucceeds();
            await using var state = CreateState();

            var running = state.RunAsync(Code);
            await UntilAsync(() => state.Status == RunStatus.Running, advanceTime: false);

            state.ServerRunning.ShouldBeFalse();
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task A_request_is_passed_to_the_running_server_and_its_answer_returned()
        {
            CompileSucceeds();
            _run.Respond = _ => new ProgramResponse(ProgramResponseOutcome.Answered, 200, "OK", [], "[]"u8.ToArray(), TimeSpan.FromMilliseconds(3));
            await using var state = CreateState();
            var running = state.RunAsync(new RunTarget(new Dictionary<string, string> { ["main.🍇"] = Code }, "main.🍇", Server: true));
            await UntilAsync(() => state.ServerRunning, advanceTime: false);
            var request = new ProgramRequest("GET", "/todos", [], []);

            var response = await state.SendHttpAsync(request, Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Answered);
            response.StatusCode.ShouldBe(200);
            _run.Requests.ShouldHaveSingleItem().ShouldBe(request);
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task A_request_with_nothing_running_says_the_run_has_ended()
        {
            await using var state = CreateState();

            var response = await state.SendHttpAsync(new ProgramRequest("GET", "/", [], []), Cancellation);

            response.Outcome.ShouldBe(ProgramResponseOutcome.Ended);
        }

        [Fact]
        public async Task A_server_that_went_idle_says_so()
        {
            CompileSucceeds();
            await using var state = CreateState();
            var running = state.RunAsync(new RunTarget(new Dictionary<string, string> { ["main.🍇"] = Code }, "main.🍇", Server: true));

            _run.Exit(null, RunEndReason.Idle);
            await running;

            state.LastRun!.Reason.ShouldBe(RunEndReason.Idle);
            state.Lines.ShouldHaveSingleItem().Text.ShouldBe("Stopped after going too long without a request.");
        }

        [Fact]
        public async Task A_problem_knows_the_text_of_the_file_it_is_in()
        {
            var inUtil = new Diagnostic(DiagnosticSeverity.Error, "shared/util.🍇", 1, 1, "Broken.");
            var nowhere = new Diagnostic(DiagnosticSeverity.Warning, string.Empty, 0, 0, "A warning.");
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false, [inUtil, nowhere], null));
            await using var state = CreateState();
            var files = new Dictionary<string, string> { ["app/main.🍇"] = "main text", ["shared/util.🍇"] = "util text" };

            await state.RunAsync(new RunTarget(files, "app/main.🍇", Server: false));

            state.SourceOf(inUtil).ShouldBe("util text");
            state.SourceOf(nowhere).ShouldBe("main text");
            state.DiagnosticsEntry.ShouldBe("app/main.🍇");
        }

        [Fact]
        public async Task The_files_that_were_compiled_are_remembered_even_if_the_caller_changes_its_own_copy()
        {
            var error = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 1, 1, "Broken.");
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false, [error], null));
            await using var state = CreateState();
            var files = new Dictionary<string, string> { ["main.🍇"] = "as compiled" };

            await state.RunAsync(new RunTarget(files, "main.🍇", Server: false));
            files["main.🍇"] = "edited since";

            state.SourceOf(error).ShouldBe("as compiled");
        }

        [Fact]
        public async Task A_run_that_fails_to_stop_does_not_throw_into_the_page()
        {
            CompileSucceeds();
            _run.StopFailure = new HttpRequestException("Connection refused");
            var logger = new RecordingLogger<RunState>();
            await using var state = new RunState(_toolchain, logger, _time);
            var running = state.RunAsync(Code);
            await UntilAsync(() => state.Status == RunStatus.Running, advanceTime: false);

            await Should.NotThrowAsync(() => state.StopAsync());

            logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error && entry.Exception is HttpRequestException);
            _run.StopFailure = null;
            _run.Exit();
            await running;
        }

        private static RunTarget Target(string code = Code) =>
            new(new Dictionary<string, string> { ["main.🍇"] = code }, "main.🍇", Server: false);

        [Fact]
        public async Task A_check_publishes_the_problems_without_running_anything()
        {
            var error = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Variable \"nope\" not defined.");
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>()).Returns(new CompileResult(false, [error], null));
            await using var state = CreateState();
            var diagnosticsChanged = 0;
            var stateChanged = 0;
            state.DiagnosticsChanged += () => diagnosticsChanged++;
            state.StateChanged += () => stateChanged++;

            await state.CheckAsync(Target("broken"));

            state.Diagnostics.ShouldBe([error]);
            state.SourceOf(error).ShouldBe("broken");
            state.Status.ShouldBe(RunStatus.Idle);
            state.Lines.ShouldBeEmpty();
            state.LastRun.ShouldBeNull();
            diagnosticsChanged.ShouldBe(1);
            stateChanged.ShouldBe(0);
            await _toolchain.Received(1).CompileAsync(Arg.Is<CompileRequest>(request => request.CheckOnly), Arg.Any<CancellationToken>());
            await _toolchain.DidNotReceive().StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task The_state_says_whether_its_problems_came_from_a_run_or_from_a_check()
        {
            var error = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Broken.");
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>()).Returns(new CompileResult(false, [error], null));
            await using var state = CreateState();

            await state.CheckAsync(Target("broken"));
            var afterCheck = state.DiagnosticsAreFromARun;
            await state.RunAsync(Target("broken"));
            var afterRun = state.DiagnosticsAreFromARun;
            await state.CheckAsync(Target("broken again"));

            afterCheck.ShouldBeFalse();
            afterRun.ShouldBeTrue();
            state.DiagnosticsAreFromARun.ShouldBeFalse();
        }

        [Fact]
        public async Task A_clean_check_clears_the_problems_of_an_earlier_one()
        {
            var error = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Broken.");
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false, [error], null), new CompileResult(true, [], null));
            await using var state = CreateState();
            await state.CheckAsync(Target("broken"));

            await state.CheckAsync(Target("fixed"));

            state.Diagnostics.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_check_that_finds_the_same_problems_again_tells_nobody()
        {
            var error = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Broken.");
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(_ => new CompileResult(false, [error], null));
            await using var state = CreateState();
            await state.CheckAsync(Target("broken"));
            var diagnosticsChanged = 0;
            state.DiagnosticsChanged += () => diagnosticsChanged++;

            await state.CheckAsync(Target("broken"));

            diagnosticsChanged.ShouldBe(0);
        }

        [Fact]
        public async Task A_newer_check_wins_over_an_older_one_that_answers_late()
        {
            var late = new TaskCompletionSource<CompileResult>();
            var fromOld = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 1, 1, "From the old text.");
            var fromNew = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 1, 1, "From the new text.");
            _toolchain.CompileAsync(Arg.Is<CompileRequest>(request => request.Files["main.🍇"] == "old"), Arg.Any<CancellationToken>()).Returns(late.Task);
            _toolchain.CompileAsync(Arg.Is<CompileRequest>(request => request.Files["main.🍇"] == "new"), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false, [fromNew], null));
            await using var state = CreateState();

            var older = state.CheckAsync(Target("old"));
            await state.CheckAsync(Target("new"));
            late.SetResult(new CompileResult(false, [fromOld], null));
            await older;

            state.Diagnostics.ShouldBe([fromNew]);
            state.SourceOf(fromNew).ShouldBe("new");
        }

        [Fact]
        public async Task A_check_goes_ahead_while_a_program_is_running_and_shows_what_it_finds()
        {
            var typed = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Typed while the server runs.");
            _toolchain.CompileAsync(Arg.Is<CompileRequest>(request => !request.CheckOnly), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [], "build-1"));
            _toolchain.CompileAsync(Arg.Is<CompileRequest>(request => request.CheckOnly), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false, [typed], null));
            _toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);
            await using var state = CreateState();
            var running = state.RunAsync(Code);
            await UntilAsync(() => state.Status == RunStatus.Running, advanceTime: false);

            await state.CheckAsync(Target("typed while running"));

            state.Status.ShouldBe(RunStatus.Running);
            state.Diagnostics.ShouldBe([typed]);
            state.DiagnosticsAreFromARun.ShouldBeFalse();
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task A_check_does_not_start_while_a_program_is_being_built()
        {
            var building = new TaskCompletionSource<CompileResult>();
            _toolchain.CompileAsync(Arg.Is<CompileRequest>(request => !request.CheckOnly), Arg.Any<CancellationToken>()).Returns(building.Task);
            _toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);
            await using var state = CreateState();
            var running = state.RunAsync(Code);
            state.Status.ShouldBe(RunStatus.Compiling);

            await state.CheckAsync(Target("typed while building"));

            await _toolchain.DidNotReceive().CompileAsync(Arg.Is<CompileRequest>(request => request.CheckOnly), Arg.Any<CancellationToken>());
            building.SetResult(new CompileResult(true, [], "build-1"));
            await UntilAsync(() => state.Status == RunStatus.Running, advanceTime: false);
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task A_run_started_while_a_check_is_out_is_not_overwritten_by_the_checks_answer()
        {
            var late = new TaskCompletionSource<CompileResult>();
            var fromCheck = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 1, 1, "From the check.");
            _toolchain.CompileAsync(Arg.Is<CompileRequest>(request => request.CheckOnly), Arg.Any<CancellationToken>()).Returns(late.Task);
            _toolchain.CompileAsync(Arg.Is<CompileRequest>(request => !request.CheckOnly), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [], "build-1"));
            _toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(_run);
            await using var state = CreateState();

            var check = state.CheckAsync(Target("being typed"));
            var running = state.RunAsync(Code);
            await UntilAsync(() => state.Status == RunStatus.Running, advanceTime: false);
            late.SetResult(new CompileResult(false, [fromCheck], null));
            await check;

            state.Diagnostics.ShouldBeEmpty();
            _run.Exit();
            await running;
        }

        [Theory]
        [InlineData("The toolchain service could not be reached.")]
        [InlineData("Too many programs are being compiled. Try again shortly.")]
        public async Task A_check_that_never_reached_the_compiler_leaves_the_problems_as_they_were(string message)
        {
            var real = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Broken.");
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(
                    new CompileResult(false, [real], null),
                    new CompileResult(false, [new Diagnostic(DiagnosticSeverity.Error, string.Empty, 0, 0, message)], null, ReachedCompiler: false));
            await using var state = CreateState();
            await state.CheckAsync(Target("broken"));

            await state.CheckAsync(Target("broken still"));

            state.Diagnostics.ShouldBe([real]);
        }

        [Theory]
        [InlineData("No 🏁 block was found.")]
        [InlineData("The compiler failed without reporting an error.")]
        public async Task What_the_compiler_says_without_naming_a_line_still_replaces_the_problems_that_were_there(string message)
        {
            var fixedSince = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Broken.");
            var noPlace = new Diagnostic(DiagnosticSeverity.Error, string.Empty, 0, 0, message);
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false, [fixedSince], null), new CompileResult(false, [noPlace], null));
            await using var state = CreateState();
            await state.CheckAsync(Target("broken"));

            await state.CheckAsync(Target("the broken line deleted"));

            state.Diagnostics.ShouldBe([noPlace]);
        }

        [Fact]
        public async Task The_first_check_shows_what_the_compiler_said_even_without_a_line()
        {
            var crashed = new Diagnostic(DiagnosticSeverity.Error, string.Empty, 0, 0, "The compiler failed without reporting an error.");
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>()).Returns(new CompileResult(false, [crashed], null));
            await using var state = CreateState();

            await state.CheckAsync(Target("something the compiler chokes on"));

            state.Diagnostics.ShouldBe([crashed]);
        }

        [Fact]
        public async Task A_check_that_throws_is_logged_and_changes_nothing()
        {
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
            var logger = new RecordingLogger<RunState>();
            await using var state = new RunState(_toolchain, logger, _time);

            await Should.NotThrowAsync(() => state.CheckAsync(Target()));

            state.Diagnostics.ShouldBeEmpty();
            logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning && entry.Exception is InvalidOperationException);
        }

        [Fact]
        public async Task Stop_with_nothing_running_does_nothing()
        {
            await using var state = CreateState();

            await Should.NotThrowAsync(() => state.StopAsync());

            state.Status.ShouldBe(RunStatus.Idle);
        }

        [Theory]
        [InlineData(RunEndReason.TimedOut, "Stopped after reaching the time limit.")]
        [InlineData(RunEndReason.OutputLimit, "Stopped after reaching the output limit.")]
        [InlineData(RunEndReason.FailedToStart, "The run could not be started.")]
        public async Task A_run_ended_by_the_toolchain_says_why(RunEndReason reason, string message)
        {
            CompileSucceeds();
            await using var state = CreateState();

            var running = state.RunAsync(Code);
            _run.Exit(null, reason);
            await running;

            state.Lines.ShouldHaveSingleItem().ShouldBe(new OutputLine(1, OutputStream.System, message));
            state.LastRun!.Reason.ShouldBe(reason);
        }

        [Fact]
        public async Task A_second_run_request_while_running_is_ignored()
        {
            CompileSucceeds();
            await using var state = CreateState();
            var running = state.RunAsync(Code);

            await state.RunAsync("other code");

            await _toolchain.Received(1).CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>());
            state.DiagnosticsSource.ShouldBe(Code);

            _run.Exit();
            await running;
        }

        [Fact]
        public async Task A_toolchain_failure_shows_a_fixed_message_and_no_internal_detail()
        {
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("connection string at /secret/path"));
            await using var state = CreateState();

            await state.RunAsync(Code);

            state.Status.ShouldBe(RunStatus.Idle);
            var line = state.Lines.ShouldHaveSingleItem();
            line.Stream.ShouldBe(OutputStream.System);
            line.Text.ShouldBe("The run could not be completed. The details are in the server log.");
            line.Text.ShouldNotContain("secret");
        }

        [Fact]
        public async Task A_failure_to_release_the_build_does_not_fail_the_run()
        {
            CompileSucceeds();
            _toolchain.ReleaseBuildAsync(Arg.Any<string>()).ThrowsAsync(new IOException("disk"));
            await using var state = CreateState();

            var running = state.RunAsync(Code);
            _run.Emit(new StdoutEvent("fine\n"));
            _run.Exit();
            await running;

            state.Status.ShouldBe(RunStatus.Idle);
            state.Lines.ShouldHaveSingleItem().Text.ShouldBe("fine");
        }

        [Fact]
        public async Task Warnings_are_published_and_the_program_still_runs()
        {
            var warning = new Diagnostic(DiagnosticSeverity.Warning, string.Empty, 0, 0, "Run-time type information is not available yet.");
            CompileSucceeds(warning);
            await using var state = CreateState();
            var diagnosticsChanged = 0;
            state.DiagnosticsChanged += () => diagnosticsChanged++;

            var running = state.RunAsync(Code);
            _run.Emit(new StdoutEvent("ran\n"));
            _run.Exit();
            await running;

            state.Diagnostics.ShouldBe([warning]);
            diagnosticsChanged.ShouldBeGreaterThanOrEqualTo(1);
            state.Lines.ShouldHaveSingleItem().Text.ShouldBe("ran");
        }

        [Fact]
        public async Task Each_run_starts_with_empty_output()
        {
            var second = new ScriptedRun();
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [], "build-1"), new CompileResult(true, [], "build-2"));
            _toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>())
                .Returns<IToolchainRun>(_run, second);
            await using var state = CreateState();
            var first = state.RunAsync(Code);
            _run.Emit(new StdoutEvent("first\n"));
            _run.Exit(3);
            await first;

            var running = state.RunAsync(Code);

            state.Lines.ShouldBeEmpty();
            state.TotalLines.ShouldBe(0);
            state.LastRun.ShouldBeNull();

            second.Emit(new StdoutEvent("second\n"));
            second.Exit();
            await running;

            state.Lines.ShouldHaveSingleItem().ShouldBe(new OutputLine(1, OutputStream.Stdout, "second"));
        }

        [Fact]
        public async Task Problems_from_an_earlier_run_are_gone_as_soon_as_a_new_run_starts()
        {
            var error = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Variable \"nope\" not defined.");
            var compiling = new TaskCompletionSource<CompileResult>();
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new CompileResult(false, [error], null)), compiling.Task);
            await using var state = CreateState();
            await state.RunAsync(Code);
            state.Diagnostics.ShouldBe([error]);

            var running = state.RunAsync(Code);

            state.Status.ShouldBe(RunStatus.Compiling);
            state.Diagnostics.ShouldBeEmpty();

            compiling.SetResult(new CompileResult(false, [], null));
            await running;
        }

        [Fact]
        public async Task Clearing_the_diagnostics_empties_them_and_tells_subscribers()
        {
            var error = new Diagnostic(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Variable \"nope\" not defined.");
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false, [error], null));
            await using var state = CreateState();
            await state.RunAsync(Code);
            var diagnosticsChanged = 0;
            state.DiagnosticsChanged += () => diagnosticsChanged++;

            state.ClearDiagnostics();

            state.Diagnostics.ShouldBeEmpty();
            state.DiagnosticsSource.ShouldBeEmpty();
            diagnosticsChanged.ShouldBe(1);
        }

        [Fact]
        public async Task A_stop_that_arrives_while_the_program_is_being_started_still_stops_it()
        {
            var starting = new TaskCompletionSource<IToolchainRun>();
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [], "build-1"));
            _toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns(starting.Task);
            await using var state = CreateState();
            var running = state.RunAsync(Code);
            state.Status.ShouldBe(RunStatus.Running);

            await state.StopAsync();
            starting.SetResult(_run);
            await running;

            _run.StopCalls.ShouldBe(1);
            state.LastRun!.Reason.ShouldBe(RunEndReason.Stopped);
        }

        [Fact]
        public async Task Closing_the_session_while_output_is_arriving_is_not_reported_as_a_failure()
        {
            var logger = new RecordingLogger<RunState>();
            var slowToStop = new SlowToCancelRun();
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [], "build-1"));
            _toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>()).Returns<IToolchainRun>(slowToStop);
            var state = new RunState(_toolchain, logger, _time);
            var running = state.RunAsync(Code);
            await UntilAsync(() => state.TotalLines == 1, advanceTime: false);

            await state.DisposeAsync();
            await running;

            logger.Entries.Where(entry => entry.Level >= LogLevel.Warning).ShouldBeEmpty();
            state.Lines.Select(line => line.Stream).ShouldNotContain(OutputStream.System);
        }

        /// <summary>
        /// Emits one line, then takes a moment to notice cancellation, the way a real stream
        /// does when the request to stop it has to travel somewhere.
        /// </summary>
        private sealed class SlowToCancelRun : IToolchainRun
        {
            public string RunId => "slow";

            public async IAsyncEnumerable<RunEvent> ReadEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return new StdoutEvent("line\n");

                var cancelled = new TaskCompletionSource();
                await using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
                await cancelled.Task;
                await Task.Delay(150, CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
            }

            public Task StopAsync() => Task.CompletedTask;

            public Task WriteInputAsync(string text, bool endOfInput = false, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<ProgramResponse> SendHttpAsync(ProgramRequest request, CancellationToken cancellationToken = default) =>
                Task.FromResult(ProgramResponse.Without(ProgramResponseOutcome.NotAServer));

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        [Fact]
        public async Task Disposing_the_state_ends_a_run_in_progress()
        {
            CompileSucceeds();
            var state = CreateState();
            var running = state.RunAsync(Code);

            await state.DisposeAsync();
            await running;

            _run.Disposed.ShouldBeTrue();
            state.Status.ShouldBe(RunStatus.Idle);
        }
    }
}
