using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Interop;
using Blazemoji.Pages;
using Blazemoji.Services.Library;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Shared.State;
using Blazemoji.Toolchain;
using BlazorMonaco.Editor;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;

namespace Blazemoji.Test.Components
{
    /// <summary>
    /// The page keeps the editor in line with the project through JS interop, one round trip
    /// at a time. These hold a round trip open to stand where a slow connection would, and
    /// check that nothing else the page does in the meantime acts on a half-switched editor.
    /// </summary>
    public sealed class HomePageTests : BunitContext
    {
        private const string CreateModel = "blazorMonaco.editor.createModel";
        private const string Reveal = "blazorMonaco.editor.revealLineInCenter";

        private static readonly Project Stored = new(
            "p1", "Two", ProjectKind.Program, "a.🍇",
            [new ProjectFile("a.🍇", "text of a"), new ProjectFile("b.🍇", "text of b\nline 2\nline 3")]);

        private readonly IToolchain _toolchain = Substitute.For<IToolchain>();
        private readonly IProjectStore _store = Substitute.For<IProjectStore>();

        public HomePageTests()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            JSInterop.SetupModule().SetupModule("register", _ => true);

            var templates = Substitute.For<IProjectTemplates>();
            templates.All.Returns([new ProjectTemplate("hello-world", "Hello World", "One file.", ProjectKind.Program, "main.🍇", [new ProjectFile("main.🍇", "🏁 🍇 🍉")])]);
            _store.ListAsync().Returns([new ProjectSummary(Stored.Id, Stored.Name)]);
            _store.GetLastOpenedAsync().Returns(Stored.Id);
            _store.LoadAsync(Stored.Id).Returns(Stored);

            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            Services.AddSingleton(_toolchain);
            Services.AddSingleton(TimeProvider.System);
            Services.AddSingleton(_store);
            Services.AddSingleton(templates);
            Services.AddSingleton(Substitute.For<ILibraryService>());
            Services.AddSingleton(new LocalStorageFiles());
            Services.AddScoped<RunState>();
            Services.AddScoped<ProjectState>();
            Services.AddScoped<RequestState>();
            Services.AddSingleton(Substitute.For<ICodeIntelligence>());
            Services.AddSingleton(Substitute.For<IPackageLibrary>());
            Services.AddScoped<EmojicodeLanguageInterop>();
        }

        private ProjectState Project => Services.GetRequiredService<ProjectState>();

        private static string Uri(int created) => $"inmemory://blazemoji/{created}";

        /// <summary>The call that makes the editor's model for the nth file shown. It stays open until given a result.</summary>
        private JSRuntimeInvocationHandler<TextModel> ModelCreation(int created) =>
            JSInterop.Setup<TextModel>(CreateModel, invocation => (string?)invocation.Arguments[2] == Uri(created));

        private static TextModel Model(int created) => new() { Id = "model-" + created, Uri = Uri(created) };

        private void ModelHolds(int created, string text) =>
            JSInterop.Setup<string>("blazorMonaco.editor.model.getValue", invocation => (string?)invocation.Arguments[0] == Uri(created)).SetResult(text);

        private IRenderedComponent<Home> RenderShowingTheFirstFile()
        {
            ModelCreation(1).SetResult(Model(1));
            var cut = Render<Home>();
            cut.WaitForAssertion(() => cut.Find("[data-testid=open-file]").TextContent.ShouldContain("a.🍇"));
            cut.WaitForAssertion(() => JSInterop.Invocations[CreateModel].Count.ShouldBe(1));
            return cut;
        }

        [Fact]
        public async Task Text_from_the_file_the_editor_is_leaving_is_never_kept_as_the_file_it_is_switching_to()
        {
            var second = ModelCreation(2);
            ModelHolds(1, "a, edited");
            ModelHolds(2, "text of b\nline 2\nline 3");
            // Until the switch has finished, this is what the editor itself still shows.
            JSInterop.Setup<string>("blazorMonaco.editor.getValue", _ => true).SetResult("a, edited");
            var cut = RenderShowingTheFirstFile();

            await cut.Find("[data-testid=file][data-path='b.🍇']").ClickAsync();
            cut.WaitForAssertion(() => JSInterop.Invocations[CreateModel].Count.ShouldBe(2));
            var backToTheFirst = cut.Find("[data-testid=file][data-path='a.🍇']").ClickAsync();
            second.SetResult(Model(2));
            await backToTheFirst;

            cut.WaitForAssertion(() => Project.OpenPath.ShouldBe("a.🍇"));
            Project.Current.Find("b.🍇")!.Content.ShouldBe("text of b\nline 2\nline 3");
            Project.Current.Find("a.🍇")!.Content.ShouldBe("a, edited");
        }

        [Fact]
        public async Task A_problem_in_a_file_not_shown_yet_is_revealed_only_once_that_file_is_in_the_editor()
        {
            var problem = new Diagnostic(DiagnosticSeverity.Error, "b.🍇", 3, 1, "Broken.");
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>()).Returns(new CompileResult(false, [problem], null));
            var second = ModelCreation(2);
            ModelHolds(1, "text of a");
            var cut = RenderShowingTheFirstFile();
            await cut.Find("[data-testid=run-button]").ClickAsync();
            cut.WaitForElement("[data-testid=problem]");

            await cut.Find("[data-testid=problem]").ClickAsync();
            cut.WaitForAssertion(() => JSInterop.Invocations[CreateModel].Count.ShouldBe(2));
            // Anything that draws the page again while the file is still being opened.
            await cut.InvokeAsync(() => Project.SelectFile("b.🍇"));
            cut.Render();

            JSInterop.Invocations[Reveal].ShouldBeEmpty("the editor is still showing the other file");

            second.SetResult(Model(2));

            cut.WaitForAssertion(() => JSInterop.Invocations[Reveal].ShouldHaveSingleItem().Arguments[1].ShouldBe(3));
        }
    }
}
