using Blazemoji.Components.Projects;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Shared.State;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Blazemoji.Test.Components
{
    public sealed class ProjectPanelTests : BunitContext
    {
        private static readonly ProjectTemplate Api = new(
            "api", "An API", "A server.", ProjectKind.Server, "app/main.🍇",
            [new ProjectFile("app/main.🍇", "main"), new ProjectFile("app/routes.🍇", "routes"), new ProjectFile("readme.🍇", "notes")]);

        private readonly IProjectStore _store = Substitute.For<IProjectStore>();
        private readonly ProjectState _state;

        public ProjectPanelTests()
        {
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
            JSInterop.Mode = JSRuntimeMode.Loose;

            var templates = Substitute.For<IProjectTemplates>();
            templates.All.Returns([Api]);
            _store.ListAsync().Returns([]);
            _state = new ProjectState(_store, templates, NullLogger<ProjectState>.Instance);
            Services.AddSingleton(_state);
        }

        private static List<string?> Paths(IRenderedComponent<ProjectPanel> cut) =>
            cut.FindAll("[data-testid=file]").Select(row => row.GetAttribute("data-path")).ToList();

        [Fact]
        public void Every_file_of_the_project_is_a_row_with_folders_first()
        {
            var cut = Render<ProjectPanel>();

            Paths(cut).ShouldBe(["app/main.🍇", "app/routes.🍇", "readme.🍇"]);
            cut.FindAll("[data-testid=file-name]").Select(name => name.TextContent).ShouldBe(["main.🍇", "routes.🍇", "readme.🍇"]);
            cut.Markup.ShouldContain("app");
        }

        [Fact]
        public void Only_the_entry_file_carries_the_flag_and_every_row_keeps_room_for_it()
        {
            var cut = Render<ProjectPanel>();

            var flagged = cut.FindAll("[data-testid=entry-flag]").ShouldHaveSingleItem();
            flagged.Closest("[data-testid=file]")!.GetAttribute("data-path").ShouldBe("app/main.🍇");
            cut.FindAll(".entry-flag").Count.ShouldBe(3);
            cut.FindAll(".entry-flag-absent").Count.ShouldBe(2);
        }

        [Fact]
        public async Task Making_another_file_the_entry_moves_the_flag()
        {
            var cut = Render<ProjectPanel>();

            await cut.InvokeAsync(() => _state.SetEntryAsync("readme.🍇"));

            cut.WaitForAssertion(() =>
                cut.Find("[data-testid=entry-flag]").Closest("[data-testid=file]")!.GetAttribute("data-path").ShouldBe("readme.🍇"));
        }

        [Fact]
        public async Task A_file_added_to_the_project_appears()
        {
            var cut = Render<ProjectPanel>();

            await cut.InvokeAsync(() => _state.AddFileAsync("app/todos.🍇"));

            cut.WaitForAssertion(() => Paths(cut).ShouldBe(["app/main.🍇", "app/routes.🍇", "app/todos.🍇", "readme.🍇"]));
        }

        [Fact]
        public async Task Clicking_a_file_opens_it_after_letting_the_editor_hand_over_its_text()
        {
            var order = new List<string>();
            _state.StateChanged += () => order.Add("opened " + _state.OpenPath);
            var cut = Render<ProjectPanel>(parameters => parameters
                .Add(panel => panel.BeforeChange, EventCallback.Factory.Create(this, () => order.Add("handed over"))));

            await cut.Find("[data-testid=file][data-path='readme.🍇']").ClickAsync();

            cut.WaitForAssertion(() => _state.OpenPath.ShouldBe("readme.🍇"));
            order.ShouldBe(["handed over", "opened readme.🍇"]);
        }

        [Fact]
        public void The_way_the_project_runs_is_shown_with_what_it_means()
        {
            var cut = Render<ProjectPanel>();

            cut.Markup.ShouldContain("Keeps running and answers requests from the Requests tab.");
        }

        [Fact]
        public void There_is_no_warning_while_saving_works()
        {
            var cut = Render<ProjectPanel>();

            cut.FindAll("[data-testid=save-failed]").ShouldBeEmpty();
        }

        [Fact]
        public async Task A_failed_save_is_shown_as_a_warning()
        {
            _store.SaveAsync(Arg.Any<Project>()).ThrowsAsync(new ProjectStoreException("full", new InvalidOperationException()));
            var cut = Render<ProjectPanel>();

            await cut.InvokeAsync(() => _state.UpdateContentAsync("readme.🍇", "edited"));

            cut.WaitForAssertion(() => cut.Find("[data-testid=save-failed]").TextContent.Trim().ShouldBe("Changes are not being saved. They will last until Blazemoji is closed."));
        }
    }
}
