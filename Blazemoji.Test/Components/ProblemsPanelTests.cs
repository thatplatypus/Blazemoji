using Blazemoji.Components;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Packmoji;
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
        public void In_a_project_with_several_files_a_problem_says_which_file_it_is_in()
        {
            var inUtil = new Diagnostic(DiagnosticSeverity.Error, "shared/util.🍇", 3, 9, "Broken.");

            var cut = Render<ProblemsPanel>(parameters => parameters
                .Add(p => p.Diagnostics, [inUtil, GlobalWarning])
                .Add(p => p.ShowFiles, true));

            cut.FindAll("[data-testid=problem-position]").ShouldHaveSingleItem().TextContent.ShouldBe("shared/util.🍇 3:9");
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

        private static readonly PackageProblem OutsideScopes = new(
            PackageProblem.Error,
            "scope.not-allowed",
            "\"@someone/thing\" is outside the scopes pmj is limited to here.",
            "packmoji.json asks for it, and PACKMOJI_SCOPES allows only emojicode",
            "depend only on packages of that scope");

        private static readonly PackageProblem BadManifest = new(
            PackageProblem.Error, "json.syntax", "The file is not valid JSON.", "strict JSON is required", "correct the JSON at this position", "packmoji.json", 4, 1);

        [Fact]
        public void A_package_problem_is_a_row_with_its_message_then_why_then_what_to_do()
        {
            var cut = Render<ProblemsPanel>(parameters => parameters.Add(p => p.Diagnostics, []).Add(p => p.PackageProblems, [OutsideScopes]));

            var row = cut.Find("[data-testid=package-problem]");
            row.TextContent.ShouldContain("\"@someone/thing\" is outside the scopes pmj is limited to here.");
            cut.Find("[data-testid=problem-why]").TextContent.ShouldBe("Why: packmoji.json asks for it, and PACKMOJI_SCOPES allows only emojicode");
            cut.Find("[data-testid=problem-fix]").TextContent.ShouldBe("Fix: depend only on packages of that scope");
            cut.Markup.ShouldNotContain("No problems.");
            cut.FindAll("[data-testid=problem]").ShouldBeEmpty();
        }

        [Fact]
        public void A_package_problem_with_a_place_says_which_file_and_where()
        {
            var cut = Render<ProblemsPanel>(parameters => parameters.Add(p => p.Diagnostics, []).Add(p => p.PackageProblems, [BadManifest, OutsideScopes]));

            var rows = cut.FindAll("[data-testid=package-problem]");
            rows[0].QuerySelector("[data-testid=problem-position]").ShouldNotBeNull().TextContent.ShouldBe("packmoji.json 4:1");
            rows[1].QuerySelector("[data-testid=problem-position]").ShouldBeNull();
        }

        [Fact]
        public void Package_problems_come_before_the_compilers_and_both_are_listed()
        {
            var cut = Render<ProblemsPanel>(parameters => parameters.Add(p => p.Diagnostics, [Error]).Add(p => p.PackageProblems, [OutsideScopes]));

            cut.FindAll("[data-testid=package-problem]").Count.ShouldBe(1);
            cut.FindAll("[data-testid=problem]").Count.ShouldBe(1);
            cut.Markup.IndexOf("package-problem", StringComparison.Ordinal).ShouldBeLessThan(cut.Markup.IndexOf("\"problem\"", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_click_on_a_package_problem_raises_it()
        {
            PackageProblem? raised = null;
            var cut = Render<ProblemsPanel>(parameters => parameters
                .Add(p => p.Diagnostics, [])
                .Add(p => p.PackageProblems, [BadManifest])
                .Add(p => p.PackageProblemSelected, problem => raised = problem));

            await cut.Find("[data-testid=package-problem]").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

            raised.ShouldBe(BadManifest);
        }
    }
}
