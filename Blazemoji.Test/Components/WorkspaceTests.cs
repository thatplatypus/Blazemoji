using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Interop;
using Blazemoji.Components;
using Blazemoji.Components.Shared;
using Blazemoji.Services.Layout;
using Blazemoji.Services.Library;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Layout;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Shared.State;
using Blazemoji.Toolchain;
using BlazorMonaco.Editor;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.Components
{
    /// <summary>
    /// The workspace keeps the editor in line with the project through JS interop, one round trip
    /// at a time. These hold a round trip open to stand where a slow connection would, and
    /// check that nothing else the workspace does in the meantime acts on a half-switched editor.
    /// </summary>
    public sealed class WorkspaceTests : BunitContext
    {
        private const string CreateModel = "blazorMonaco.editor.createModel";
        private const string Reveal = "blazorMonaco.editor.revealLineInCenter";

        private static readonly Project Stored = new(
            "p1", "Two", ProjectKind.Program, "a.🍇",
            [new ProjectFile("a.🍇", "text of a"), new ProjectFile("b.🍇", "text of b\nline 2\nline 3")]);

        private readonly IToolchain _toolchain = Substitute.For<IToolchain>();
        private readonly IProjectStore _store = Substitute.For<IProjectStore>();
        private readonly ILibraryService _library = Substitute.For<ILibraryService>();
        private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
        private readonly ILayoutStore _layoutStore = Substitute.For<ILayoutStore>();

        public WorkspaceTests()
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
            Services.AddSingleton(_library);
            Services.AddSingleton(_dialogs);
            Services.AddSingleton(new LocalStorageFiles());
            Services.AddScoped<RunState>();
            Services.AddScoped<ProjectState>();
            Services.AddScoped<RequestState>();
            Services.AddSingleton(Substitute.For<ICodeIntelligence>());
            Services.AddSingleton(Substitute.For<IPackageLibrary>());
            Services.AddScoped<EmojicodeLanguageInterop>();
            Services.AddSingleton(_layoutStore);
            Services.AddScoped<LayoutState>();
            Services.AddScoped<SplitViewInterop>();
            Services.AddScoped<ProgramInputInterop>();
        }

        private const string Columns = "[data-testid=workspace-columns]";
        private const string Rows = "[data-testid=workspace-rows]";
        private const string SidebarToggle = "[data-testid=sidebar-toggle]";

        private ProjectState Project => Services.GetRequiredService<ProjectState>();

        private LayoutState Layout => Services.GetRequiredService<LayoutState>();

        private static string Uri(int created) => $"inmemory://blazemoji/{created}";

        /// <summary>The call that makes the editor's model for the nth file shown. It stays open until given a result.</summary>
        private JSRuntimeInvocationHandler<TextModel> ModelCreation(int created) =>
            JSInterop.Setup<TextModel>(CreateModel, invocation => (string?)invocation.Arguments[2] == Uri(created));

        private static TextModel Model(int created) => new() { Id = "model-" + created, Uri = Uri(created) };

        private void ModelHolds(int created, string text) =>
            JSInterop.Setup<string>("blazorMonaco.editor.model.getValue", invocation => (string?)invocation.Arguments[0] == Uri(created)).SetResult(text);

        private IRenderedComponent<Workspace> RenderShowingTheFirstFile()
        {
            ModelCreation(1).SetResult(Model(1));
            var cut = Render<Workspace>();
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
        public async Task Text_the_browser_was_still_reading_when_another_project_was_opened_is_not_kept_in_that_project()
        {
            // Both projects have a file of the same name.
            var other = new Project("p2", "Other", ProjectKind.Program, "a.🍇", [new ProjectFile("a.🍇", "the other project's own text")]);
            _store.LoadAsync(other.Id).Returns(other);
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>()).Returns(new CompileResult(false, [], null));
            var reading = JSInterop.Setup<string>("blazorMonaco.editor.model.getValue", invocation => (string?)invocation.Arguments[0] == Uri(1));
            ModelCreation(2).SetResult(Model(2));
            var cut = RenderShowingTheFirstFile();
            var running = cut.Find("[data-testid=run-button]").ClickAsync();
            cut.WaitForAssertion(() => reading.Invocations.Count.ShouldBe(1));

            await cut.InvokeAsync(() => Project.OpenAsync(other.Id));
            reading.SetResult("typed into the first project");
            await running;

            Project.Current.Id.ShouldBe(other.Id);
            Project.Current.Find("a.🍇")!.Content.ShouldBe("the other project's own text");
        }

        [Fact]
        public async Task Running_does_not_wait_for_a_file_that_is_still_being_opened()
        {
            _toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>()).Returns(new CompileResult(false, [], null));
            ModelCreation(2);
            ModelHolds(1, "a, edited");
            var cut = RenderShowingTheFirstFile();
            await cut.Find("[data-testid=file][data-path='b.🍇']").ClickAsync();
            cut.WaitForAssertion(() => JSInterop.Invocations[CreateModel].Count.ShouldBe(2));

            // Not awaited: behind a file that never finishes opening, this click would never finish either.
            var running = cut.Find("[data-testid=run-button]").ClickAsync();

            // The second file's model never arrives in this test. The run went ahead all the
            // same, with what had been typed into the file the editor was still showing.
            cut.WaitForAssertion(() => _toolchain.ReceivedCalls().ShouldContain(call => call.GetMethodInfo().Name == nameof(IToolchain.CompileAsync)));
            Project.Current.Find("a.🍇")!.Content.ShouldBe("a, edited");
            await running;
        }

        private void TheSaveDialogIsAnsweredWith(string name)
        {
            var dialog = Substitute.For<IDialogReference>();
            dialog.Result.Returns(Task.FromResult<DialogResult?>(DialogResult.Ok(name)));
            _dialogs.ShowAsync<SaveFileDialog>(Arg.Any<string?>(), Arg.Any<DialogOptions?>()).Returns(Task.FromResult(dialog));
        }

        [Fact]
        public async Task The_editors_text_is_saved_to_the_library_under_the_name_given()
        {
            JSInterop.Setup<string>("blazorMonaco.editor.getValue", _ => true).SetResult("what is in the editor");
            TheSaveDialogIsAnsweredWith("Mine.🍇");
            _library.GetSavedAsync().Returns([]);
            var cut = RenderShowingTheFirstFile();

            await cut.Find("[data-testid=save-to-library]").ClickAsync();

            await _library.Received(1).SaveAsync(Arg.Is<Blazemoji.Shared.Models.Library.EmojicFile>(file => file.Name == "Mine.🍇" && file.Code == "what is in the editor"));
            Services.GetRequiredService<ISnackbar>().ShownSnackbars.ShouldBeEmpty();
        }

        [Fact]
        public async Task Text_that_cannot_be_saved_to_the_library_is_reported_and_the_workspace_stays_up()
        {
            JSInterop.Setup<string>("blazorMonaco.editor.getValue", _ => true).SetResult("what is in the editor");
            TheSaveDialogIsAnsweredWith("Mine.🍇");
            _library.SaveAsync(Arg.Any<Blazemoji.Shared.Models.Library.EmojicFile>()).ThrowsAsync(new ProjectStoreException("full", new IOException("/secret/path")));
            var cut = RenderShowingTheFirstFile();

            await cut.Find("[data-testid=save-to-library]").ClickAsync();

            Services.GetRequiredService<ISnackbar>().ShownSnackbars.Single().Message.ShouldBe("The file could not be saved.");
            cut.Find("[data-testid=run-button]").ShouldNotBeNull();
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

        [Fact]
        public void The_sidebar_is_beside_everything_else_and_the_output_is_under_the_editor()
        {
            var cut = RenderShowingTheFirstFile();

            cut.Find(Columns).ClassList.ShouldNotContain("stacked");
            cut.Find(Columns + " > .split-view-panels > .split-view-first [aria-label='Files, toolbox and library']").ShouldNotBeNull();

            var beside = Columns + " > .split-view-panels > .split-view-second " + Rows;
            cut.Find(beside).ClassList.ShouldContain("stacked");
            cut.Find(beside + " > .split-view-panels > .split-view-first [data-testid=open-file]").ShouldNotBeNull();
            cut.Find(beside + " > .split-view-panels > .split-view-second [aria-label='Output, problems and requests']").ShouldNotBeNull();
        }

        [Fact]
        public void Until_a_layout_is_kept_the_dividers_are_where_the_default_puts_them()
        {
            var cut = RenderShowingTheFirstFile();

            var views = cut.FindComponents<SplitView>();
            views.Single(view => !view.Instance.Stacked).Instance.Share.ShouldBe(WorkspaceLayout.Default.SidebarShare);
            views.Single(view => view.Instance.Stacked).Instance.Share.ShouldBe(WorkspaceLayout.Default.EditorShare);
            cut.Find(Columns).ClassList.ShouldNotContain("first-hidden");
        }

        [Fact]
        public void The_layout_that_was_kept_is_taken_up()
        {
            _layoutStore.LoadAsync().Returns(new WorkspaceLayout(0.4, 0.5, true));

            var cut = RenderShowingTheFirstFile();

            cut.WaitForAssertion(() =>
            {
                var views = cut.FindComponents<SplitView>();
                views.Single(view => !view.Instance.Stacked).Instance.Share.ShouldBe(0.4);
                views.Single(view => view.Instance.Stacked).Instance.Share.ShouldBe(0.5);
                cut.Find(Columns).ClassList.ShouldContain("first-hidden");
            });
        }

        [Fact]
        public async Task A_divider_let_go_somewhere_new_is_kept_as_part_of_the_layout()
        {
            var cut = RenderShowingTheFirstFile();
            var views = cut.FindComponents<SplitView>();

            await cut.InvokeAsync(() => views.Single(view => !view.Instance.Stacked).Instance.DividerMovedAsync(0.4));
            await cut.InvokeAsync(() => views.Single(view => view.Instance.Stacked).Instance.DividerMovedAsync(0.5));

            Layout.Current.ShouldBe(new WorkspaceLayout(0.4, 0.5, false));
            await _layoutStore.Received(1).SaveAsync(new WorkspaceLayout(0.4, 0.5, false));
        }

        [Fact]
        public async Task A_double_click_on_a_divider_puts_it_back_where_the_default_has_it()
        {
            _layoutStore.LoadAsync().Returns(new WorkspaceLayout(0.4, 0.5, false));
            var cut = RenderShowingTheFirstFile();
            cut.WaitForAssertion(() => Layout.Current.SidebarShare.ShouldBe(0.4));
            var views = cut.FindComponents<SplitView>();

            await cut.InvokeAsync(() => views.Single(view => !view.Instance.Stacked).Instance.DividerResetAsync());
            await cut.InvokeAsync(() => views.Single(view => view.Instance.Stacked).Instance.DividerResetAsync());

            Layout.Current.ShouldBe(WorkspaceLayout.Default);
        }

        [Fact]
        public async Task The_sidebar_button_hides_the_sidebar_without_taking_it_off_the_page_and_then_shows_it_again()
        {
            var cut = RenderShowingTheFirstFile();
            cut.Find(SidebarToggle).GetAttribute("aria-label").ShouldBe("Hide sidebar");
            cut.Find(SidebarToggle).GetAttribute("aria-expanded").ShouldBe("true");

            await cut.Find(SidebarToggle).ClickAsync(new());

            cut.Find(Columns).ClassList.ShouldContain("first-hidden");
            cut.FindComponents<Sidebar>().Count.ShouldBe(1);
            cut.Find(SidebarToggle).GetAttribute("aria-label").ShouldBe("Show sidebar");
            cut.Find(SidebarToggle).GetAttribute("aria-expanded").ShouldBe("false");
            Layout.Current.SidebarHidden.ShouldBeTrue();

            await cut.Find(SidebarToggle).ClickAsync(new());

            cut.Find(Columns).ClassList.ShouldNotContain("first-hidden");
            cut.Find(SidebarToggle).GetAttribute("aria-label").ShouldBe("Hide sidebar");
            await _layoutStore.Received(1).SaveAsync(WorkspaceLayout.Default with { SidebarHidden = true });
            await _layoutStore.Received(1).SaveAsync(WorkspaceLayout.Default);
        }

        private static IReadOnlyList<string> TabNames(IRenderedComponent<Workspace> cut) =>
            cut.FindAll("[data-testid=editor-tab]").Select(tab => tab.TextContent.Trim()).ToList();

        private static AngleSharp.Dom.IElement TabOf(IRenderedComponent<Workspace> cut, string path) =>
            cut.FindAll("[data-testid=editor-tab]").Single(tab => tab.GetAttribute("data-path") == path).Closest("[role=tab]")!;

        [Fact]
        public void The_file_a_project_opens_on_has_the_only_tab()
        {
            var cut = RenderShowingTheFirstFile();

            TabNames(cut).ShouldBe(["a.🍇"]);
        }

        [Fact]
        public async Task A_file_shown_from_the_files_list_gets_a_tab_and_a_click_on_the_first_tab_brings_the_first_back()
        {
            ModelCreation(2).SetResult(Model(2));
            ModelHolds(1, "text of a");
            ModelHolds(2, "text of b\nline 2\nline 3");
            var cut = RenderShowingTheFirstFile();

            await cut.InvokeAsync(() => Project.SelectFile("b.🍇"));
            cut.WaitForAssertion(() => TabNames(cut).ShouldBe(["a.🍇", "b.🍇"]));
            TabOf(cut, "b.🍇").GetAttribute("aria-selected").ShouldBe("true");

            await TabOf(cut, "a.🍇").ClickAsync(new());

            cut.WaitForAssertion(() => Project.OpenPath.ShouldBe("a.🍇"));
            cut.Find("[data-testid=open-file]").TextContent.ShouldContain("a.🍇");
            TabNames(cut).ShouldBe(["a.🍇", "b.🍇"]);
        }

        [Fact]
        public async Task The_cross_on_a_tab_closes_the_tab_and_leaves_the_file_in_the_project()
        {
            ModelCreation(2).SetResult(Model(2));
            ModelHolds(1, "text of a");
            ModelHolds(2, "text of b\nline 2\nline 3");
            var cut = RenderShowingTheFirstFile();
            await cut.InvokeAsync(() => Project.SelectFile("b.🍇"));
            cut.WaitForAssertion(() => TabNames(cut).ShouldBe(["a.🍇", "b.🍇"]));

            await TabOf(cut, "b.🍇").QuerySelector(".editor-tab-close")!.ClickAsync(new());

            cut.WaitForAssertion(() => TabNames(cut).ShouldBe(["a.🍇"]));
            Project.OpenPath.ShouldBe("a.🍇");
            Project.Current.Files.Select(file => file.Path).ShouldBe(["a.🍇", "b.🍇"]);
        }

        [Fact]
        public async Task What_was_typed_in_a_file_is_kept_before_its_tab_is_left_for_another()
        {
            ModelCreation(2).SetResult(Model(2));
            ModelHolds(1, "a, edited and not yet saved");
            ModelHolds(2, "text of b\nline 2\nline 3");
            var cut = RenderShowingTheFirstFile();
            await cut.InvokeAsync(() => Project.SelectFile("b.🍇"));
            cut.WaitForAssertion(() => TabNames(cut).ShouldBe(["a.🍇", "b.🍇"]));
            await cut.InvokeAsync(() => Project.SelectFile("a.🍇"));
            cut.WaitForAssertion(() => Project.OpenPath.ShouldBe("a.🍇"));

            await TabOf(cut, "b.🍇").ClickAsync(new());

            cut.WaitForAssertion(() => Project.OpenPath.ShouldBe("b.🍇"));
            Project.Current.Find("a.🍇")!.Content.ShouldBe("a, edited and not yet saved");
        }

        private static readonly Project ThreeFiles = new(
            "p1", "Three", ProjectKind.Program, "a.🍇",
            [new ProjectFile("a.🍇", "text of a"), new ProjectFile("b.🍇", "text of b"), new ProjectFile("c.🍇", "text of c")]);

        /// <summary>A workspace on a project of three files with every one of them open, the last shown.</summary>
        private async Task<IRenderedComponent<Workspace>> RenderWithThreeTabsAsync()
        {
            _store.ListAsync().Returns([new ProjectSummary(ThreeFiles.Id, ThreeFiles.Name)]);
            _store.LoadAsync(ThreeFiles.Id).Returns(ThreeFiles);
            foreach (var created in new[] { 1, 2, 3, 4 })
            {
                ModelCreation(created).SetResult(Model(created));
                ModelHolds(created, "text");
            }

            var cut = Render<Workspace>();
            cut.WaitForAssertion(() => cut.Find("[data-testid=open-file]").TextContent.ShouldContain("a.🍇"));
            await cut.InvokeAsync(() => Project.SelectFile("b.🍇"));
            await cut.InvokeAsync(() => Project.SelectFile("c.🍇"));
            cut.WaitForAssertion(() => TabNames(cut).ShouldBe(["a.🍇", "b.🍇", "c.🍇"]));
            return cut;
        }

        private static string SelectedTab(IRenderedComponent<Workspace> cut) =>
            cut.FindAll("[role=tab][aria-selected=true] [data-testid=editor-tab]").Select(tab => tab.TextContent.Trim()).ShouldHaveSingleItem();

        [Fact]
        public async Task Closing_a_tab_well_to_the_left_of_the_shown_one_leaves_the_shown_file_where_it_is()
        {
            var cut = await RenderWithThreeTabsAsync();

            await TabOf(cut, "a.🍇").QuerySelector(".editor-tab-close")!.ClickAsync(new());

            cut.WaitForAssertion(() => TabNames(cut).ShouldBe(["b.🍇", "c.🍇"]));
            Project.OpenPath.ShouldBe("c.🍇");
            SelectedTab(cut).ShouldBe("c.🍇");
        }

        [Fact]
        public async Task Deleting_a_file_well_to_the_left_of_the_shown_one_leaves_the_shown_file_where_it_is()
        {
            var cut = await RenderWithThreeTabsAsync();

            await cut.InvokeAsync(() => Project.DeleteFileAsync("b.🍇"));
            await cut.InvokeAsync(() => Project.DeleteFileAsync("a.🍇"));

            cut.WaitForAssertion(() => TabNames(cut).ShouldBe(["c.🍇"]));
            Project.OpenPath.ShouldBe("c.🍇");
            SelectedTab(cut).ShouldBe("c.🍇");
        }

        [Fact]
        public async Task A_file_renamed_while_another_is_shown_keeps_its_tab_in_its_place_and_each_tab_still_opens_its_own_file()
        {
            var cut = await RenderWithThreeTabsAsync();

            (await cut.InvokeAsync(() => Project.RenameFileAsync("a.🍇", "first.🍇"))).ShouldBeNull();

            // Files are listed by name, and the tabs stay in the order they were opened.
            cut.WaitForAssertion(() => TabNames(cut).ShouldBe(["first.🍇", "b.🍇", "c.🍇"]));
            Project.OpenPath.ShouldBe("c.🍇");
            SelectedTab(cut).ShouldBe("c.🍇");

            await TabOf(cut, "b.🍇").ClickAsync(new());
            cut.WaitForAssertion(() => Project.OpenPath.ShouldBe("b.🍇"));
            SelectedTab(cut).ShouldBe("b.🍇");

            await TabOf(cut, "first.🍇").ClickAsync(new());
            cut.WaitForAssertion(() => Project.OpenPath.ShouldBe("first.🍇"));
            SelectedTab(cut).ShouldBe("first.🍇");
        }

        [Fact]
        public async Task The_shown_file_renamed_keeps_its_tab_selected_in_its_place()
        {
            var cut = await RenderWithThreeTabsAsync();
            await cut.InvokeAsync(() => Project.SelectFile("b.🍇"));
            cut.WaitForAssertion(() => SelectedTab(cut).ShouldBe("b.🍇"));

            (await cut.InvokeAsync(() => Project.RenameFileAsync("b.🍇", "second.🍇"))).ShouldBeNull();

            cut.WaitForAssertion(() => TabNames(cut).ShouldBe(["a.🍇", "second.🍇", "c.🍇"]));
            Project.OpenPath.ShouldBe("second.🍇");
            SelectedTab(cut).ShouldBe("second.🍇");
        }

        [Fact]
        public async Task What_was_typed_in_the_shown_file_is_kept_before_its_tab_is_closed()
        {
            ModelCreation(2).SetResult(Model(2));
            ModelHolds(1, "text of a");
            ModelHolds(2, "b, edited and not yet saved");
            var cut = RenderShowingTheFirstFile();
            await cut.InvokeAsync(() => Project.SelectFile("b.🍇"));
            cut.WaitForAssertion(() => TabNames(cut).ShouldBe(["a.🍇", "b.🍇"]));
            cut.WaitForAssertion(() => JSInterop.Invocations[CreateModel].Count.ShouldBe(2));

            await TabOf(cut, "b.🍇").QuerySelector(".editor-tab-close")!.ClickAsync(new());

            cut.WaitForAssertion(() => TabNames(cut).ShouldBe(["a.🍇"]));
            Project.Current.Find("b.🍇")!.Content.ShouldBe("b, edited and not yet saved");
        }

        [Fact]
        public async Task The_editor_is_given_the_keys_with_the_file_a_tab_asks_for_and_not_before_it_is_shown()
        {
            const string showModel = "showModel";
            var language = JSInterop.SetupModule("./_content/Blazemoji.Components/js/emojicodeLanguage.js");
            language.Mode = JSRuntimeMode.Loose;
            language.SetupModule("register", _ => true);
            language.Setup<bool>(showModel, _ => true).SetResult(true);
            var cut = await RenderWithThreeTabsAsync();
            var before = language.Invocations[showModel].Count;
            var focusedBefore = JSInterop.Invocations.Count(call => call.Identifier == "blazorMonaco.editor.focus");

            await TabOf(cut, "a.🍇").ClickAsync(new());

            cut.WaitForAssertion(() => language.Invocations[showModel].Count.ShouldBe(before + 1));
            language.Invocations[showModel].Last().Arguments[2].ShouldBe(true, "the keys go with the file, in the same step");
            JSInterop.Invocations.Count(call => call.Identifier == "blazorMonaco.editor.focus").ShouldBe(focusedBefore, "the editor is not given the keys while it still shows the other file");
        }

        [Fact]
        public async Task A_click_on_the_tab_of_the_file_already_shown_gives_the_editor_the_keys_at_once()
        {
            var cut = await RenderWithThreeTabsAsync();
            var focusedBefore = JSInterop.Invocations.Count(call => call.Identifier == "blazorMonaco.editor.focus");

            await TabOf(cut, "c.🍇").ClickAsync(new());

            cut.WaitForAssertion(() => JSInterop.Invocations.Count(call => call.Identifier == "blazorMonaco.editor.focus").ShouldBe(focusedBefore + 1));
            Project.OpenPath.ShouldBe("c.🍇");
        }
    }
}
