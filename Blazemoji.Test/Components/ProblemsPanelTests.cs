using Blazemoji.Components;
using Blazemoji.Toolchain;
using Bunit;
using MudBlazor.Services;

namespace Blazemoji.Test.Components
{
    public class ProblemsPanelTests : BunitContext
    {
        private static readonly Diagnostic Error = new(DiagnosticSeverity.Error, "main.🍇", 2, 5, "Variable \"nope\" not defined.");
        private static readonly Diagnostic GlobalWarning = new(DiagnosticSeverity.Warning, string.Empty, 0, 0, "Run-time type information is not available yet.");

        public ProblemsPanelTests()
        {
            Services.AddMudServices();
            JSInterop.Mode = JSRuntimeMode.Loose;
        }

        [Fact]
        public void With_no_diagnostics_it_says_there_are_no_problems()
        {
            var cut = Render<ProblemsPanel>(parameters => parameters.Add(p => p.Diagnostics, []));

            cut.FindAll("[data-testid=problem]").ShouldBeEmpty();
            cut.Markup.ShouldContain("No problems.");
        }

        [Fact]
        public void Each_diagnostic_is_a_row_with_its_message_and_position()
        {
            var cut = Render<ProblemsPanel>(parameters => parameters.Add(p => p.Diagnostics, [Error, GlobalWarning]));

            var rows = cut.FindAll("[data-testid=problem]");
            rows.Count.ShouldBe(2);
            rows[0].TextContent.ShouldContain("Variable \"nope\" not defined.");
            rows[0].TextContent.ShouldContain("2:5");
            rows[1].TextContent.ShouldContain("Run-time type information is not available yet.");
        }

        [Fact]
        public void A_diagnostic_without_a_location_shows_no_position()
        {
            var cut = Render<ProblemsPanel>(parameters => parameters.Add(p => p.Diagnostics, [GlobalWarning]));

            cut.FindAll("[data-testid=problem-position]").ShouldBeEmpty();
        }

        [Fact]
        public async Task Choosing_a_row_raises_ProblemSelected_with_that_diagnostic()
        {
            Diagnostic? selected = null;
            var cut = Render<ProblemsPanel>(parameters => parameters
                .Add(p => p.Diagnostics, [GlobalWarning, Error])
                .Add(p => p.ProblemSelected, diagnostic => selected = diagnostic));

            await cut.FindAll("[data-testid=problem]")[1].ClickAsync(new());

            selected.ShouldBe(Error);
        }
    }
}
