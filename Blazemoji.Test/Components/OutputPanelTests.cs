using Blazemoji.Components;
using Blazemoji.Shared.State;
using Blazemoji.Test.State;
using Blazemoji.Toolchain;
using Bunit;
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
        private readonly RunState _state;

        public OutputPanelTests()
        {
            Services.AddMudServices();
            JSInterop.Mode = JSRuntimeMode.Loose;

            _state = new RunState(_toolchain, NullLogger<RunState>.Instance, new FakeTimeProvider());
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
