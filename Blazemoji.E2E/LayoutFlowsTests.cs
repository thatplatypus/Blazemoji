namespace Blazemoji.E2E
{
    /// <summary>
    /// The workspace's three parts and the two dividers between them: the sidebar beside the
    /// editor, the output under it. Only a real browser lays anything out, so where things
    /// are, and where a dragged divider ends up, is checked here.
    /// </summary>
    public class LayoutFlowsTests(BrowserFixture browser)
    {
        private const string Columns = "[data-testid=workspace-columns]";
        private const string Rows = "[data-testid=workspace-rows]";
        private const string SidebarToggle = "[data-testid=sidebar-toggle]";
        private const string Monaco = ".monaco-editor";

        /// <summary>How far two measurements of one thing may be apart: a pixel of rounding on each side.</summary>
        private const double Rounding = 2;

        private static string First(string split) => split + " > * > .split-view-first";

        private static string Second(string split) => split + " > * > .split-view-second";

        private static string Divider(string split) => split + " > * > .split-view-divider";

        private static async Task<LocatorBoundingBoxResult> BoxAsync(IPage page, string selector) =>
            await page.Locator(selector).First.BoundingBoxAsync() ?? throw new ShouldAssertException($"Nothing is drawn for {selector}");

        /// <summary>Presses on a divider, moves it and lets go, as a hand on a mouse does.</summary>
        private static async Task DragAsync(IPage page, string divider, float right = 0, float down = 0)
        {
            var box = await BoxAsync(page, divider);
            var x = box.X + box.Width / 2;
            var y = box.Y + box.Height / 2;
            await page.Mouse.MoveAsync(x, y);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync(x + right / 2, y + down / 2, new MouseMoveOptions { Steps = 4 });
            await page.Mouse.MoveAsync(x + right, y + down, new MouseMoveOptions { Steps = 4 });
            await page.Mouse.UpAsync();
        }

        /// <summary>
        /// Waits until the server has heard where the divider was let go and the page has
        /// settled on it: the split's share is no longer the one it had, and its parts are
        /// no longer held at the sizes in pixels the drag ended on. A divider that was only
        /// roughly where it was let go would show from then on.
        /// </summary>
        private static async Task<string> ShareAfterAsync(IPage page, string split, string before)
        {
            await page.WaitForFunctionAsync(
                @"([split, first, before]) => document.querySelector(split).style.getPropertyValue('--split-share').trim() !== before
                    && document.querySelector(first).style.width === '100%' && document.querySelector(first).style.height === '100%'",
                new[] { split, First(split), before });
            return await ShareAsync(page, split);
        }

        private static Task<string> ShareAsync(IPage page, string split) =>
            page.EvalOnSelectorAsync<string>(split, "split => split.style.getPropertyValue('--split-share').trim()");

        private static async Task ReloadAsync(EditorPage editor)
        {
            await editor.Page.ReloadAsync();
            await editor.EditorText.GetByText("Hello World!").WaitForAsync();
        }

        private static async Task ThePageDoesNotScrollAsync(IPage page)
        {
            var beyond = await page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth - innerWidth, document.documentElement.scrollHeight - innerHeight]");
            beyond.ShouldBe([0, 0], "the page is wider or taller than the window");
        }

        [Fact]
        public async Task The_sidebar_is_beside_the_editor_and_the_output_is_under_it_and_as_wide()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            var sidebar = await BoxAsync(editor.Page, First(Columns));
            var monaco = await BoxAsync(editor.Page, Monaco);
            var output = await BoxAsync(editor.Page, Second(Rows));

            (sidebar.X + sidebar.Width).ShouldBeLessThanOrEqualTo(monaco.X);
            output.Y.ShouldBeGreaterThanOrEqualTo(monaco.Y + monaco.Height);
            ((double)output.X).ShouldBe(monaco.X, Rounding);
            ((double)output.Width).ShouldBe(monaco.Width, Rounding);
            (await editor.Page.GetByRole(AriaRole.Tab, new() { Name = "Output" }).BoundingBoxAsync())!.Y.ShouldBeGreaterThan(monaco.Y + monaco.Height);
            await ThePageDoesNotScrollAsync(editor.Page);
        }

        [Fact]
        public async Task Dragging_the_divider_beside_the_sidebar_widens_it_and_the_editor_gives_up_the_same()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var sidebar = await BoxAsync(editor.Page, First(Columns));
            var monaco = await BoxAsync(editor.Page, Monaco);
            var share = await ShareAsync(editor.Page, Columns);

            await DragAsync(editor.Page, Divider(Columns), right: 120);
            await ShareAfterAsync(editor.Page, Columns, share);

            ((double)(await BoxAsync(editor.Page, First(Columns))).Width).ShouldBe(sidebar.Width + 120, Rounding);
            ((double)(await BoxAsync(editor.Page, Monaco)).Width).ShouldBe(monaco.Width - 120, Rounding);
            await ThePageDoesNotScrollAsync(editor.Page);
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task Dragging_the_divider_under_the_editor_gives_the_output_what_the_editor_gives_up()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var monaco = await BoxAsync(editor.Page, Monaco);
            var output = await BoxAsync(editor.Page, Second(Rows));
            var share = await ShareAsync(editor.Page, Rows);

            await DragAsync(editor.Page, Divider(Rows), down: -150);
            await ShareAfterAsync(editor.Page, Rows, share);

            ((double)(await BoxAsync(editor.Page, Second(Rows))).Height).ShouldBe(output.Height + 150, Rounding);
            ((double)(await BoxAsync(editor.Page, Monaco)).Height).ShouldBe(monaco.Height - 150, Rounding);
            await ThePageDoesNotScrollAsync(editor.Page);
        }

        [Fact]
        public async Task Both_dividers_are_where_they_were_left_after_the_page_is_loaded_again()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var columns = await ShareAsync(editor.Page, Columns);
            var rows = await ShareAsync(editor.Page, Rows);
            await DragAsync(editor.Page, Divider(Columns), right: 90);
            columns = await ShareAfterAsync(editor.Page, Columns, columns);
            await DragAsync(editor.Page, Divider(Rows), down: -110);
            rows = await ShareAfterAsync(editor.Page, Rows, rows);
            var sidebar = await BoxAsync(editor.Page, First(Columns));
            var output = await BoxAsync(editor.Page, Second(Rows));

            await ReloadAsync(editor);

            await Expect(editor.Page.Locator(Columns)).ToHaveAttributeAsync("style", new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(columns)));
            await Expect(editor.Page.Locator(Rows)).ToHaveAttributeAsync("style", new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(rows)));
            ((double)(await BoxAsync(editor.Page, First(Columns))).Width).ShouldBe(sidebar.Width, Rounding);
            ((double)(await BoxAsync(editor.Page, Second(Rows))).Height).ShouldBe(output.Height, Rounding);
        }

        [Fact]
        public async Task A_dragged_divider_keeps_its_share_of_the_window_when_the_window_changes_size()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var share = await ShareAsync(editor.Page, Columns);
            await DragAsync(editor.Page, Divider(Columns), right: 150);
            await ShareAfterAsync(editor.Page, Columns, share);
            var before = await BoxAsync(editor.Page, First(Columns));
            var roomBefore = await BoxAsync(editor.Page, Columns);

            await editor.Page.SetViewportSizeAsync(1200, 700);

            var after = await BoxAsync(editor.Page, First(Columns));
            var roomAfter = await BoxAsync(editor.Page, Columns);
            ((double)(after.Width / roomAfter.Width)).ShouldBe(before.Width / roomBefore.Width, 0.005);
            await ThePageDoesNotScrollAsync(editor.Page);

            // Monaco measures itself a moment after its surroundings change size.
            await editor.Page.WaitForFunctionAsync(
                "([editor, beside]) => Math.abs(document.querySelector(editor).getBoundingClientRect().width - document.querySelector(beside).getBoundingClientRect().width) < 2",
                new[] { Monaco, Second(Columns) });
        }

        [Fact]
        public async Task A_double_click_on_a_divider_puts_it_back_where_it_started()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var sidebar = await BoxAsync(editor.Page, First(Columns));
            var share = await ShareAsync(editor.Page, Columns);
            await DragAsync(editor.Page, Divider(Columns), right: 140);
            var dragged = await ShareAfterAsync(editor.Page, Columns, share);

            await editor.Page.Locator(Divider(Columns)).DblClickAsync();

            (await ShareAfterAsync(editor.Page, Columns, dragged)).ShouldBe(share);
            ((double)(await BoxAsync(editor.Page, First(Columns))).Width).ShouldBe(sidebar.Width, Rounding);
        }

        [Fact]
        public async Task The_arrow_keys_move_a_divider_that_has_the_focus_and_it_says_where_it_is()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var sidebar = await BoxAsync(editor.Page, First(Columns));
            var share = await ShareAsync(editor.Page, Columns);
            var divider = editor.Page.Locator(Divider(Columns));

            await divider.FocusAsync();
            for (var press = 0; press < 3; press++)
                await editor.Page.Keyboard.PressAsync("ArrowRight");
            await ShareAfterAsync(editor.Page, Columns, share);

            var moved = await BoxAsync(editor.Page, First(Columns));
            ((double)moved.Width).ShouldBe(sidebar.Width + 30, Rounding);

            // What a screen reader is read is the sidebar's part of the width, in hundredths.
            var room = await BoxAsync(editor.Page, Columns);
            var said = double.Parse((await divider.GetAttributeAsync("aria-valuenow"))!, System.Globalization.CultureInfo.InvariantCulture);
            said.ShouldBe(moved.Width / room.Width * 100, 0.5);
        }

        [Fact]
        public async Task Hiding_the_sidebar_gives_the_editor_its_room_and_showing_it_brings_it_back_as_it_was()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var sidebar = editor.Page.Locator(First(Columns));
            var toolbox = editor.Page.GetByRole(AriaRole.Tab, new() { Name = "Toolbox" });
            await toolbox.ClickAsync();
            var shown = await BoxAsync(editor.Page, First(Columns));
            var room = await BoxAsync(editor.Page, Columns);

            await editor.Page.Locator(SidebarToggle).ClickAsync();

            await Expect(sidebar).ToBeHiddenAsync();
            ((double)(await BoxAsync(editor.Page, Monaco)).Width).ShouldBe(room.Width, Rounding);
            await ThePageDoesNotScrollAsync(editor.Page);

            // It is still hidden after the page is loaded again ...
            await ReloadAsync(editor);
            await Expect(sidebar).ToBeHiddenAsync();
            await editor.Page.Locator(SidebarToggle).ClickAsync();
            await Expect(sidebar).ToBeVisibleAsync();
            ((double)(await BoxAsync(editor.Page, First(Columns))).Width).ShouldBe(shown.Width, Rounding);

            // ... and within one visit it comes back showing what it was showing.
            await editor.Page.GetByRole(AriaRole.Tab, new() { Name = "Toolbox" }).ClickAsync();
            await editor.Page.Locator(SidebarToggle).ClickAsync();
            await Expect(sidebar).ToBeHiddenAsync();
            await editor.Page.Locator(SidebarToggle).ClickAsync();
            await Expect(editor.Page.GetByRole(AriaRole.Tab, new() { Name = "Toolbox" })).ToHaveAttributeAsync("aria-selected", "true");
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task Under_the_editor_a_request_and_its_answer_are_side_by_side_and_Send_needs_no_scrolling()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.Page.GetByRole(AriaRole.Tab, new() { Name = "Requests" }).ClickAsync();

            var form = await BoxAsync(editor.Page, ".request-form");
            var answers = await BoxAsync(editor.Page, ".request-answers");
            var send = await BoxAsync(editor.Page, "[data-testid=request-send]");
            var panel = await BoxAsync(editor.Page, Second(Rows));
            (form.X + form.Width).ShouldBeLessThanOrEqualTo(answers.X);
            (send.Y + send.Height).ShouldBeLessThanOrEqualTo(panel.Y + panel.Height);
            send.Y.ShouldBeGreaterThanOrEqualTo(panel.Y);
        }

        private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
    }
}
