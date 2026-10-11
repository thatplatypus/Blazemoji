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
            // No key moves the cursor first: the one for the end of a file differs between macOS
            // and Linux, and a comment that ends a file with no newline after it does not compile.
            await editor.Page.Keyboard.TypeAsync("💭 typed after the click");
            await editor.Page.Keyboard.PressAsync("Enter");
            (await editor.GetCodeAsync()).Split('\n')[0].ShouldBe("💭 typed after the click");

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

            var toolbar = await editor.Page.Locator(".mud-paper:has(> [data-testid=open-file])").BoundingBoxAsync();
            var tabs = await editor.Page.GetByTestId("editor-tabs").BoundingBoxAsync();
            var monaco = await editor.Page.Locator(".monaco-editor").First.BoundingBoxAsync();

            // The same gap under the row as above it, so that the row is a thing of its own
            // and not the editor's top edge.
            var above = tabs!.Y - (toolbar!.Y + toolbar.Height);
            var under = monaco!.Y - (tabs.Y + tabs.Height);
            above.ShouldBeInRange(6, 12);
            ((double)under).ShouldBe(above, 1);
            tabs.Height.ShouldBeLessThan(60);
            ((double)tabs.Width).ShouldBe(monaco.Width, 2);
            (await editor.Page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth - innerWidth, document.documentElement.scrollHeight - innerHeight]")).ShouldBe([0, 0]);
        }

        /// <summary>Adds a file to the open project from the Files list. It is shown, and gets a tab.</summary>
        private static Task AddFileAsync(EditorPage editor, string path) =>
            editor.ShowsAnotherFileAsync(
                async () =>
                {
                    await editor.Page.GetByTestId("new-file").ClickAsync();
                    await editor.AnswerPromptAsync(path);
                },
                Project,
                path);

        private static async Task<string> SelectedTabAsync(EditorPage editor) =>
            (await editor.Page.Locator("[role=tab][aria-selected=true] [data-testid=editor-tab]").InnerTextAsync()).Trim();

        [Fact]
        public async Task Closing_the_first_of_three_tabs_while_the_last_is_shown_leaves_the_last_shown()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await WithBothFilesOpenAsync(browser);
            await AddFileAsync(editor, "notes.🍇");
            await editor.SetCodeAsync("💭 the third file\n");
            (await Tabs(editor).AllInnerTextsAsync()).ShouldBe([Main, Greeter, "notes.🍇"]);

            await CrossOn(editor, Main).ClickAsync();

            await Assertions.Expect(Tabs(editor)).ToHaveCountAsync(2);
            (await Tabs(editor).AllInnerTextsAsync()).ShouldBe([Greeter, "notes.🍇"]);
            await editor.Page.WaitForTimeoutAsync(500);
            await editor.WaitForOpenFileAsync(Project, "notes.🍇");
            (await editor.GetCodeAsync()).ShouldContain("the third file");
            (await SelectedTabAsync(editor)).ShouldBe("notes.🍇");
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_file_renamed_from_the_list_keeps_its_tab_in_its_place_and_every_tab_still_opens_its_own_file()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await WithBothFilesOpenAsync(browser);

            // The first tab's file, while the second is the one shown.
            await editor.ChooseFileActionAsync(Main, "rename-file");
            await editor.AnswerPromptAsync("start.🍇");
            await editor.WaitForFilesAsync(Greeter, "start.🍇");

            await Assertions.Expect(Tabs(editor).First).ToHaveTextAsync("start.🍇");
            (await Tabs(editor).AllInnerTextsAsync()).ShouldBe(["start.🍇", Greeter]);
            await editor.WaitForOpenFileAsync(Project, Greeter);
            (await SelectedTabAsync(editor)).ShouldBe(Greeter);
            (await editor.GetCodeAsync()).ShouldContain("🐇 🙋");

            await ClickTabAsync(editor, "start.🍇");
            (await editor.GetCodeAsync()).ShouldContain("📜");
            (await SelectedTabAsync(editor)).ShouldBe("start.🍇");
            await ClickTabAsync(editor, Greeter);
            (await editor.GetCodeAsync()).ShouldContain("🐇 🙋");
            (await SelectedTabAsync(editor)).ShouldBe(Greeter);
        }

        [Fact]
        public async Task A_file_deleted_from_the_list_loses_its_tab_and_the_shown_file_stays_shown()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await WithBothFilesOpenAsync(browser);
            await AddFileAsync(editor, "notes.🍇");
            await editor.SetCodeAsync("💭 the third file\n");

            await editor.ChooseFileActionAsync(Greeter, "delete-file");
            await editor.Dialog.GetByText("Delete", new LocatorGetByTextOptions { Exact = true }).Last.ClickAsync();
            await editor.WaitForFilesAsync(Main, "notes.🍇");

            await Assertions.Expect(Tabs(editor)).ToHaveCountAsync(2);
            (await Tabs(editor).AllInnerTextsAsync()).ShouldBe([Main, "notes.🍇"]);
            await editor.WaitForOpenFileAsync(Project, "notes.🍇");
            (await editor.GetCodeAsync()).ShouldContain("the third file");
            (await SelectedTabAsync(editor)).ShouldBe("notes.🍇");
        }

        [Fact]
        public async Task A_click_on_the_tab_of_the_file_already_shown_gives_the_editor_the_keys()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await WithBothFilesOpenAsync(browser);

            await TabOf(editor, Greeter).ClickAsync();
            await editor.Page.WaitForFunctionAsync("() => document.activeElement?.closest('.monaco-editor') !== null");
            await editor.Page.Keyboard.TypeAsync("💭 typed after the click");

            (await editor.GetCodeAsync()).ShouldContain("💭 typed after the click");
            (await Tabs(editor).AllInnerTextsAsync()).ShouldBe([Main, Greeter]);
        }

        [Fact]
        public async Task Two_open_files_of_one_name_show_their_folders_and_many_tabs_do_not_widen_the_page()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await WithBothFilesOpenAsync(browser);
            await editor.Page.SetViewportSizeAsync(1100, 800);

            await AddFileAsync(editor, "lib/greeter.🍇");
            (await Tabs(editor).AllInnerTextsAsync()).ShouldBe([Main, Greeter, "lib/greeter.🍇"]);
            (await Tabs(editor).Nth(2).GetAttributeAsync("title")).ShouldBe("lib/greeter.🍇");

            foreach (var name in new[] { "one", "two", "three", "four", "five", "six", "seven" })
                await AddFileAsync(editor, $"more/a-long-name-for-file-{name}.🍇");

            // More tabs than the row has room for: the row moves along inside itself.
            await Assertions.Expect(Tabs(editor)).ToHaveCountAsync(10);
            (await editor.Page.EvaluateAsync<int[]>("() => [document.documentElement.scrollWidth - innerWidth, document.documentElement.scrollHeight - innerHeight]")).ShouldBe([0, 0]);
            var row = await editor.Page.GetByTestId("editor-tabs").BoundingBoxAsync();
            var monaco = await editor.Page.Locator(".monaco-editor").First.BoundingBoxAsync();
            ((double)row!.Width).ShouldBe(monaco!.Width, 2);
            (await SelectedTabAsync(editor)).ShouldBe("a-long-name-for-file-seven.🍇");
            await editor.ScreenshotAsync("p7-02-many-tabs");
        }
    }
}
