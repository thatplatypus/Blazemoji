using Blazemoji.Components;
using Blazemoji.Interop;
using Blazemoji.Shared.State;
using Blazemoji.Test.State;
using Blazemoji.Toolchain;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;
using NSubstitute;

namespace Blazemoji.Test.Components
{
    public class OutputPanelTests : BunitContext
    {
        private const string Code = "🏁 🍇 🍉";
        private static readonly Diagnostic Error = new(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Variable \"nope\" not defined.");
        private static readonly Diagnostic Warning = new(DiagnosticSeverity.Warning, string.Empty, 0, 0, "Run-time type information is not available yet.");

        private readonly IToolchain _toolchain = Substitute.For<IToolchain>();
        private readonly ScriptedRun _run = new();
        private readonly FakeTimeProvider _time = new();
        private readonly RunState _state;
        private readonly BunitJSModuleInterop _inputScript;

        public OutputPanelTests()
        {
            Services.AddMudServices();
            JSInterop.Mode = JSRuntimeMode.Loose;

            _state = new RunState(_toolchain, NullLogger<RunState>.Instance, _time);
            _inputScript = JSInterop.SetupModule(InputScript);
            _inputScript.Mode = JSRuntimeMode.Loose;
            Services.AddScoped<ProgramInputInterop>();
            Services.AddSingleton(_state);
            Services.AddSingleton(new RequestState(_state));
        }

        private void CompileSucceeds(params Diagnostic[] diagnostics)
        {
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, diagnostics, "build-1"));
            _toolchain.StartRunAsync(Arg.Any<RunRequest>(), Arg.Any<CancellationToken>())
                .Returns<IToolchainRun>(_run);
        }

        private void CompileFails(params Diagnostic[] diagnostics) =>
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false, diagnostics, null));

        private static string StatusOf(IRenderedComponent<OutputPanel> cut) =>
            cut.Find("[data-testid=run-status]").TextContent.Trim();

        [Fact]
        public void Before_anything_has_run_it_says_ready()
        {
            var cut = Render<OutputPanel>();

            StatusOf(cut).ShouldBe("Ready");
            cut.FindAll("[data-testid=output-line]").ShouldBeEmpty();
        }

        private const string InputScript = "./_content/Blazemoji.Components/js/programInput.js";
        private const string InputBox = "[data-testid=program-input]";
        private const string EndInput = "[data-testid=end-input]";

        /// <summary>Starts a program and waits until it can be given input. The task is the run, which ends when the program does.</summary>
        private Task Running(IRenderedComponent<OutputPanel> cut)
        {
            CompileSucceeds();
            var running = _state.RunAsync(Code);
            cut.WaitForAssertion(() => cut.Find(InputBox).HasAttribute("disabled").ShouldBeFalse());
            return running;
        }

        /// <summary>
        /// Output is shown a moment after it arrives, so that a flood of it is not a flood of
        /// redraws, and the clock that moment is counted on is this test's. The program's
        /// output is read on another thread, so the clock is moved on again and again until
        /// what is expected has been drawn: moved once, it can be moved before the wait has begun.
        /// </summary>
        private async Task DrawnAsync(Action expected)
        {
            for (var attempt = 0; attempt < 400; attempt++)
            {
                _time.Advance(TimeSpan.FromSeconds(1));
                try
                {
                    expected();
                    return;
                }
                catch (Exception notYet) when (notYet is ShouldAssertException or ElementNotFoundException)
                {
                    await Task.Delay(10, Xunit.TestContext.Current.CancellationToken);
                }
            }

            expected();
        }

        [Fact]
        public void With_nothing_running_the_place_for_input_is_there_and_cannot_be_typed_in()
        {
            var cut = Render<OutputPanel>();

            // It keeps its place whether or not a program is running, so that one starting moves nothing.
            cut.Find(InputBox).HasAttribute("disabled").ShouldBeTrue();
            cut.Find(EndInput).HasAttribute("disabled").ShouldBeTrue();
        }

        [Fact]
        public void The_script_that_sends_a_line_on_Enter_is_set_on_the_box_once()
        {
            var cut = Render<OutputPanel>();
            cut.Render();

            // Enter and the emptying of the box are the script's, in the browser, so that a
            // slow line to the server cannot lose what is typed next. The browser tests type.
            _inputScript.Invocations.Count(call => call.Identifier == "attach").ShouldBe(1);
        }

        [Fact]
        public void The_box_is_bound_to_nothing_so_the_server_is_never_told_what_is_in_it()
        {
            var cut = Render<OutputPanel>();

            var box = cut.FindComponent<MudBlazor.MudTextField<string>>().Instance;
            box.Immediate.ShouldBeFalse();
            box.ValueChanged.HasDelegate.ShouldBeFalse();
            box.MaxLength.ShouldBe(RunState.MaxInputLength);
        }

        [Fact]
        public async Task A_line_handed_over_by_the_script_is_sent_to_the_program_and_shown_as_typed()
        {
            var cut = Render<OutputPanel>();
            var running = Running(cut);

            await cut.InvokeAsync(() => cut.Instance.SendLineAsync("🍇 grapes"));

            _run.Input.ShouldBe([("🍇 grapes\n", false)]);
            cut.WaitForAssertion(() => cut.Find("[data-testid=output-typed]").TextContent.ShouldBe("🍇 grapes"));
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task An_empty_line_is_a_line_all_the_same()
        {
            var cut = Render<OutputPanel>();
            var running = Running(cut);

            await cut.InvokeAsync(() => cut.Instance.SendLineAsync(string.Empty));

            _run.Input.ShouldBe([("\n", false)]);
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task The_words_in_the_empty_box_say_what_it_is_for_now()
        {
            var cut = Render<OutputPanel>();
            string Hint() => cut.Find(InputBox).GetAttribute("placeholder") ?? string.Empty;
            Hint().ShouldBe("A running program can be given input here");

            var running = Running(cut);
            Hint().ShouldBe("Type a line for the program and press Enter");

            await cut.Find(EndInput).ClickAsync(new());
            cut.WaitForAssertion(() => Hint().ShouldBe("The program's input has ended"));

            _run.Exit();
            await running;
            cut.WaitForAssertion(() => Hint().ShouldBe("A running program can be given input here"));
        }

        [Fact]
        public async Task A_question_asked_on_standard_error_is_shown_as_it_waits_too()
        {
            var cut = Render<OutputPanel>();
            var running = Running(cut);

            _run.Emit(new StderrEvent("Name: "));

            await DrawnAsync(() => cut.Find("[data-testid=output-unfinished-error]").TextContent.ShouldBe("Name: "));
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task The_end_of_input_button_tells_the_program_and_then_neither_can_be_used()
        {
            var cut = Render<OutputPanel>();
            var running = Running(cut);

            await cut.Find(EndInput).ClickAsync(new());

            _run.Input.ShouldBe([(string.Empty, true)]);
            cut.WaitForAssertion(() => cut.Find(InputBox).HasAttribute("disabled").ShouldBeTrue());
            cut.Find(EndInput).HasAttribute("disabled").ShouldBeTrue();
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task A_question_the_program_has_not_ended_with_a_newline_is_shown_under_the_lines()
        {
            var cut = Render<OutputPanel>();
            var running = Running(cut);

            _run.Emit(new StdoutEvent("first\nWhat is your name? "));

            await DrawnAsync(() => cut.Find("[data-testid=output-unfinished]").TextContent.ShouldBe("What is your name? "));
            cut.FindAll("[data-testid=output-line]").Select(line => line.TextContent).ShouldBe(["first"]);

            await cut.InvokeAsync(() => cut.Instance.SendLineAsync("Tom"));

            cut.WaitForAssertion(() => cut.FindAll("[data-testid=output-unfinished]").ShouldBeEmpty());
            cut.FindAll("[data-testid=output-line]").Select(line => line.TextContent).ShouldBe(["first", "What is your name? Tom"]);
            _run.Exit();
            await running;
        }

        [Fact]
        public async Task When_the_program_ends_the_place_for_input_cannot_be_typed_in_again()
        {
            var cut = Render<OutputPanel>();
            var running = Running(cut);

            _run.Exit();
            await running;

            cut.WaitForAssertion(() => cut.Find(InputBox).HasAttribute("disabled").ShouldBeTrue());
        }

        [Fact]
        public async Task Output_lines_are_shown_in_order_and_stderr_is_marked()
        {
            CompileSucceeds();
            var cut = Render<OutputPanel>();

            var running = _state.RunAsync(Code);
            _run.Emit(new StdoutEvent("out\n"));
            _run.Emit(new StderrEvent("err\n"));
            _run.Exit(0);
            await running;

            cut.WaitForAssertion(() =>
            {
                var lines = cut.FindAll("[data-testid=output-line]");
                lines.Select(line => line.TextContent).ShouldBe(["out", "err"]);
                lines[0].ClassList.ShouldNotContain("output-line-stderr");
                lines[1].ClassList.ShouldContain("output-line-stderr");
            });
        }

        [Fact]
        public async Task It_says_running_while_the_program_runs_and_the_exit_code_afterwards()
        {
            CompileSucceeds();
            var cut = Render<OutputPanel>();

            var running = _state.RunAsync(Code);
            cut.WaitForAssertion(() => StatusOf(cut).ShouldBe("Running"));

            _run.Exit(3);
            await running;

            cut.WaitForAssertion(() => StatusOf(cut).ShouldBe("Exited with code 3 after 0.12 s"));
        }

        [Theory]
        [InlineData(RunEndReason.Stopped, "Stopped after 0.12 s")]
        [InlineData(RunEndReason.TimedOut, "Time limit reached after 0.12 s")]
        [InlineData(RunEndReason.OutputLimit, "Output limit reached after 0.12 s")]
        [InlineData(RunEndReason.FailedToStart, "Could not start")]
        public async Task It_says_how_a_run_that_was_cut_short_ended(RunEndReason reason, string expected)
        {
            CompileSucceeds();
            var cut = Render<OutputPanel>();

            var running = _state.RunAsync(Code);
            _run.Exit(null, reason);
            await running;

            cut.WaitForAssertion(() => StatusOf(cut).ShouldBe(expected));
        }

        [Fact]
        public async Task It_says_when_earlier_lines_were_dropped()
        {
            CompileSucceeds();
            var cut = Render<OutputPanel>();
            var total = RunState.MaxLines + 5;

            var running = _state.RunAsync(Code);
            _run.Emit(new StdoutEvent(string.Concat(Enumerable.Range(1, total).Select(i => $"{i}\n"))));
            _run.Exit();
            await running;

            cut.WaitForAssertion(() =>
                cut.Find("[data-testid=output-dropped]").TextContent.Trim().ShouldBe("Showing the last 5,000 of 5,005 lines."));
        }

        [Fact]
        public async Task A_failed_build_brings_the_problems_forward()
        {
            CompileFails(Error);
            var cut = Render<OutputPanel>();

            await _state.RunAsync(Code);

            cut.WaitForAssertion(() =>
                cut.FindAll("[data-testid=problem]").ShouldHaveSingleItem().TextContent.ShouldContain("Variable \"nope\" not defined."));
        }

        [Fact]
        public async Task Problems_found_while_typing_are_counted_on_the_tab_but_do_not_take_over_the_panel()
        {
            CompileFails(Error);
            var cut = Render<OutputPanel>();

            await cut.InvokeAsync(() => _state.CheckAsync(new RunTarget(new Dictionary<string, string> { ["main.🍇"] = Code }, "main.🍇", Server: false)));

            cut.WaitForAssertion(() => cut.Find(".mud-badge").TextContent.Trim().ShouldBe("1"));
            StatusOf(cut).ShouldBe("Ready");
            cut.FindAll("[data-testid=problem]").ShouldBeEmpty();
        }

        [Fact]
        public async Task A_failed_build_is_named_in_the_status_once_the_output_is_shown_again()
        {
            CompileFails(Error);
            var cut = Render<OutputPanel>();
            await _state.RunAsync(Code);
            cut.WaitForAssertion(() => cut.FindAll("[data-testid=problem]").Count.ShouldBe(1));

            await cut.FindAll("[role=tab]")[0].ClickAsync(new());

            cut.WaitForAssertion(() => StatusOf(cut).ShouldBe("Build failed"));
        }

        [Fact]
        public async Task A_build_with_only_warnings_stays_on_the_output()
        {
            CompileSucceeds(Warning);
            var cut = Render<OutputPanel>();

            var running = _state.RunAsync(Code);
            _run.Emit(new StdoutEvent("ran\n"));
            _run.Exit();
            await running;

            cut.WaitForAssertion(() =>
            {
                cut.FindAll("[data-testid=output-line]").Select(line => line.TextContent).ShouldBe(["ran"]);
                cut.FindAll("[data-testid=problem]").ShouldBeEmpty();
            });
        }

        [Fact]
        public async Task Choosing_a_problem_raises_ProblemSelected()
        {
            CompileFails(Error);
            Diagnostic? selected = null;
            var cut = Render<OutputPanel>(parameters => parameters.Add(p => p.ProblemSelected, diagnostic => selected = diagnostic));
            await _state.RunAsync(Code);
            cut.WaitForAssertion(() => cut.FindAll("[data-testid=problem]").Count.ShouldBe(1));

            await cut.Find("[data-testid=problem]").ClickAsync(new());

            selected.ShouldBe(Error);
        }

        [Fact]
        public async Task A_new_run_returns_to_the_output()
        {
            CompileFails(Error);
            var cut = Render<OutputPanel>();
            await _state.RunAsync(Code);
            cut.WaitForAssertion(() => cut.FindAll("[data-testid=problem]").Count.ShouldBe(1));

            CompileSucceeds();
            var running = _state.RunAsync(Code);
            _run.Emit(new StdoutEvent("fixed\n"));
            _run.Exit();
            await running;

            cut.WaitForAssertion(() =>
                cut.FindAll("[data-testid=output-line]").Select(line => line.TextContent).ShouldBe(["fixed"]));
        }
    }
}
