namespace Blazemoji.E2E
{
    /// <summary>
    /// The row of tabs above the editor: one for each file that is open, the shown one marked,
    /// a cross to close each. What is in the editor after a click is only knowable in a browser.
    /// </summary>
    public class EditorTabsFlowsTests(BrowserFixture browser)
    {
        private const string Project = "Greeter";
        private const string Main = "main.🍇";
        private const string Greeter = "greeter.🍇";

        private static ILocator Tabs(EditorPage editor) => editor.Page.GetByTestId("editor-tab");

        private static ILocator TabOf(EditorPage editor, string path) =>
            editor.Page.Locator($"[role=tab]:has([data-testid=editor-tab][data-path=\"{path}\"])");

        private static ILocator CrossOn(EditorPage editor, string path) => TabOf(editor, path).Locator(".editor-tab-close");

        private static async Task<EditorPage> WithBothFilesOpenAsync(BrowserFixture browser)
        {
            var editor = await EditorPage.OpenAsync(browser);
            await editor.CreateProjectAsync(Project, "two-files");
            await editor.WaitForOpenFileAsync(Project, Main);
            await editor.OpenFileAsync(Project, Greeter);
            return editor;
        }

        private static Task ClickTabAsync(EditorPage editor, string path) =>
            editor.ShowsAnotherFileAsync(() => TabOf(editor, path).ClickAsync(), Project, path);

        [Fact]
        public async Task A_project_opens_with_one_tab_and_a_file_opened_from_the_list_gets_the_next()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            (await Tabs(editor).AllInnerTextsAsync()).ShouldBe(["main.🍇"]);
            (await editor.Page.Locator(".editor-tab-close").CountAsync()).ShouldBe(0, "the only tab cannot be closed");

            await editor.CreateProjectAsync(Project, "two-files");
            await editor.WaitForOpenFileAsync(Project, Main);
            await editor.OpenFileAsync(Project, Greeter);

            (await Tabs(editor).AllInnerTextsAsync()).ShouldBe([Main, Greeter]);
            await Assertions.Expect(TabOf(editor, Greeter)).ToHaveAttributeAsync("aria-selected", "true");
            await Assertions.Expect(TabOf(editor, Main)).ToHaveAttributeAsync("aria-selected", "false");
            (await editor.GetCodeAsync()).ShouldContain("🐇 🙋");
            editor.ConsoleErrors.ShouldBeEmpty();
            await editor.ScreenshotAsync("p7-01-two-tabs");
        }

        [Fact]
        public async Task A_click_on_a_tab_shows_its_file_with_what_was_typed_in_it_and_typing_goes_to_the_editor()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await WithBothFilesOpenAsync(browser);
            await editor.SetCodeAsync((await editor.GetCodeAsync()).Replace("Hello", "Howdy"));

            // Straight away, before the pause after which typing is kept anyway.
            await ClickTabAsync(editor, Main);
            (await editor.GetCodeAsync()).ShouldContain("📜");
            await Assertions.Expect(TabOf(editor, Main)).ToHaveAttributeAsync("aria-selected", "true");

            // The editor has the keys after a click on a tab, as it has after a click in the list.
            await editor.Page.Keyboard.PressAsync("Control+End");
            await editor.Page.Keyboard.TypeAsync("💭 typed after the click");
            (await editor.GetCodeAsync()).ShouldContain("💭 typed after the click");

            await ClickTabAsync(editor, Greeter);
            (await editor.GetCodeAsync()).ShouldContain("Howdy");
            await editor.RunButton.ClickAsync();
            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["Howdy, World!"]);
        }

        [Fact]
        public async Task The_cursor_and_the_scroll_are_where_they_were_left_when_a_tab_is_come_back_to()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await WithBothFilesOpenAsync(browser);
            await editor.SetCodeAsync(string.Join("\n", Enumerable.Range(1, 200).Select(line => $"💭 line {line}")) + "\n");
            await editor.Page.EvaluateAsync(@"() => {
                const editor = monaco.editor.getEditors()[0];
                editor.setPosition({ lineNumber: 150, column: 4 });
                editor.revealLineInCenter(150);
            }");
            var left = await editor.Page.EvaluateAsync<int[]>("() => { const e = monaco.editor.getEditors()[0]; return [e.getPosition().lineNumber, e.getPosition().column, Math.round(e.getScrollTop())]; }");
            left[2].ShouldBeGreaterThan(0, "the file should have been scrolled down");

            await ClickTabAsync(editor, Main);
            (await editor.CursorAsync()).LineNumber.ShouldBe(1);
            await ClickTabAsync(editor, Greeter);

            var back = await editor.Page.EvaluateAsync<int[]>("() => { const e = monaco.editor.getEditors()[0]; return [e.getPosition().lineNumber, e.getPosition().column, Math.round(e.getScrollTop())]; }");
            back.ShouldBe(left);
        }

        [Fact]
        public async Task The_cross_closes_a_tab_shows_the_one_beside_it_and_leaves_the_file_in_the_project()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await WithBothFilesOpenAsync(browser);
            await editor.SetCodeAsync((await editor.GetCodeAsync()).Replace("Hello", "Howdy"));

            await editor.ShowsAnotherFileAsync(() => CrossOn(editor, Greeter).ClickAsync(), Project, Main);

            (await Tabs(editor).AllInnerTextsAsync()).ShouldBe([Main]);
            (await editor.Page.Locator(".editor-tab-close").CountAsync()).ShouldBe(0);
            await editor.WaitForFilesAsync(Greeter, Main);

            // What was typed just before the cross was pressed went with the file, not with the tab.
            await editor.OpenFileAsync(Project, Greeter);
            (await editor.GetCodeAsync()).ShouldContain("Howdy");
            (await Tabs(editor).AllInnerTextsAsync()).ShouldBe([Main, Greeter]);
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task Closing_a_tab_that_is_not_the_one_shown_leaves_the_editor_as_it_is()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await WithBothFilesOpenAsync(browser);
            var shown = await editor.GetCodeAsync();

            await CrossOn(editor, Main).ClickAsync();

            await Assertions.Expect(Tabs(editor)).ToHaveCountAsync(1);
            (await Tabs(editor).AllInnerTextsAsync()).ShouldBe([Greeter]);
            await editor.WaitForOpenFileAsync(Project, Greeter);
            (await editor.GetCodeAsync()).ShouldBe(shown);
        }

        [Fact]
        public async Task The_tabs_are_one_row_between_the_toolbar_and_the_editor_and_the_page_still_fits_the_window()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await WithBothFilesOpenAsync(browser);

            var caption = await editor.Page.GetByTestId("open-file").BoundingBoxAsync();
            var tabs = await editor.Page.GetByTestId("editor-tabs").BoundingBoxAsync();
            var monaco = await editor.Page.Locator(".monaco-editor").First.BoundingBoxAsync();

            tabs!.Y.ShouldBeGreaterThanOrEqualTo(caption!.Y + caption.Height);
            monaco!.Y.ShouldBeGreaterThanOrEqualTo(tabs.Y + tabs.Height - 1);
            tabs.Height.ShouldBeLessThan(60);
            ((double)tabs.Width).ShouldBe(monaco.Width, 2);
            (await editor.Page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth - innerWidth, document.documentElement.scrollHeight - innerHeight]")).ShouldBe([0, 0]);
        }
    }
}
