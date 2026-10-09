using Blazemoji.Components;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Blazemoji.Test.Components
{
    public sealed class EditorTabsTests : BunitContext
    {
        private const string Tab = "[data-testid=editor-tab]";
        private const string Close = ".editor-tab-close";

        public EditorTabsTests()
        {
            JSInterop.Mode = JSRuntimeMode.Loose;
            Services.AddMudServices(options => options.PopoverOptions.CheckForPopoverProvider = false);
        }

        private IRenderedComponent<EditorTabs> RenderTabs(
            IReadOnlyList<string> paths,
            string shown,
            Action<string>? selected = null,
            Action<string>? closed = null)
        {
            return Render<EditorTabs>(parameters =>
            {
                parameters.Add(tabs => tabs.Paths, paths);
                parameters.Add(tabs => tabs.Shown, shown);
                if (selected is not null)
                    parameters.Add(tabs => tabs.Selected, selected);
                if (closed is not null)
                    parameters.Add(tabs => tabs.Closed, closed);
            });
        }

        private static IReadOnlyList<string> Names(IRenderedComponent<EditorTabs> cut) =>
            cut.FindAll(Tab).Select(tab => tab.TextContent.Trim()).ToList();

        private static AngleSharp.Dom.IElement TabOf(IRenderedComponent<EditorTabs> cut, string path) =>
            cut.FindAll(Tab).Single(tab => tab.GetAttribute("data-path") == path).Closest("[role=tab]")
                ?? throw new ShouldAssertException($"There is no tab for {path}");

        [Fact]
        public void There_is_a_tab_for_each_open_file_in_the_order_given_named_by_the_file_alone()
        {
            var cut = RenderTabs(["app/main.🍇", "shared/util.🍇", "README.md"], shown: "shared/util.🍇");

            Names(cut).ShouldBe(["main.🍇", "util.🍇", "README.md"]);
        }

        [Fact]
        public void The_tab_of_the_file_that_is_shown_is_the_selected_one()
        {
            var cut = RenderTabs(["app/main.🍇", "shared/util.🍇"], shown: "shared/util.🍇");

            TabOf(cut, "shared/util.🍇").GetAttribute("aria-selected").ShouldBe("true");
            TabOf(cut, "app/main.🍇").GetAttribute("aria-selected").ShouldBe("false");
        }

        [Fact]
        public void Two_open_files_of_one_name_say_which_folder_each_is_in()
        {
            var cut = RenderTabs(["app/greeter.🍇", "lib/greeter.🍇", "main.🍇"], shown: "main.🍇");

            Names(cut).ShouldBe(["app/greeter.🍇", "lib/greeter.🍇", "main.🍇"]);
        }

        [Fact]
        public async Task Clicking_a_tab_asks_for_its_file()
        {
            var asked = new List<string>();
            var cut = RenderTabs(["app/main.🍇", "shared/util.🍇"], shown: "app/main.🍇", selected: asked.Add);

            await TabOf(cut, "shared/util.🍇").ClickAsync(new());

            asked.ShouldBe(["shared/util.🍇"]);
        }

        [Fact]
        public async Task Clicking_the_tab_that_is_already_shown_asks_for_nothing()
        {
            var asked = new List<string>();
            var cut = RenderTabs(["app/main.🍇", "shared/util.🍇"], shown: "app/main.🍇", selected: asked.Add);

            await TabOf(cut, "app/main.🍇").ClickAsync(new());

            asked.ShouldBeEmpty();
        }

        [Fact]
        public async Task The_cross_on_a_tab_asks_for_that_tab_to_close_and_not_for_its_file_to_be_shown()
        {
            var asked = new List<string>();
            var closed = new List<string>();
            var cut = RenderTabs(["app/main.🍇", "shared/util.🍇"], shown: "app/main.🍇", selected: asked.Add, closed: closed.Add);

            await TabOf(cut, "shared/util.🍇").QuerySelector(Close)!.ClickAsync(new());

            closed.ShouldBe(["shared/util.🍇"]);
            asked.ShouldBeEmpty();
        }

        [Fact]
        public void The_only_tab_has_no_cross_because_the_editor_always_shows_a_file()
        {
            var cut = RenderTabs(["main.🍇"], shown: "main.🍇");

            cut.FindAll(Close).ShouldBeEmpty();
        }

        [Fact]
        public void With_more_than_one_every_tab_has_a_cross_that_is_always_there()
        {
            var cut = RenderTabs(["app/main.🍇", "shared/util.🍇"], shown: "app/main.🍇");

            cut.FindAll(Close).Count.ShouldBe(2);
        }

        [Fact]
        public void A_file_name_keeps_its_own_letters()
        {
            var cut = RenderTabs(["Notes.MD"], shown: "Notes.MD");

            // MudBlazor writes a tab's text in capitals. A file's name is not a heading.
            cut.Find(Tab).ClassList.ShouldContain("editor-tab-name");
            Names(cut).ShouldBe(["Notes.MD"]);
        }

        [Fact]
        public void The_tabs_follow_when_the_open_files_change()
        {
            var cut = RenderTabs(["app/main.🍇", "shared/util.🍇"], shown: "shared/util.🍇");

            cut.Render(parameters => parameters
                .Add(tabs => tabs.Paths, new[] { "app/main.🍇" })
                .Add(tabs => tabs.Shown, "app/main.🍇"));

            Names(cut).ShouldBe(["main.🍇"]);
            TabOf(cut, "app/main.🍇").GetAttribute("aria-selected").ShouldBe("true");
        }
    }
}
