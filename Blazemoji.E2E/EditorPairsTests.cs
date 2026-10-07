namespace Blazemoji.E2E
{
    /// <summary>
    /// What the editor does with the things Emojicode writes in pairs: 🍇🍉, 🤜🤛, 🍿🍆, 🐚🍆 and
    /// the 🔤 at both ends of a string. Monaco does these for brackets and not for emoji, so
    /// the app does them, and this is where that is tried in a real browser.
    /// </summary>
    public class EditorPairsTests(BrowserFixture browser)
    {
        private static Task PutAsync(EditorPage editor, string text, int line, int column) =>
            editor.Page.EvaluateAsync(
                "([text, line, column]) => { const editor = monaco.editor.getEditors()[0]; editor.getModel().setValue(text); editor.setPosition({ lineNumber: line, column }); editor.focus(); }",
                new object[] { text, line, column });

        // Playwright fills this in, for which it needs to be able to make an empty one.
        private sealed record Shown
        {
            public string Text { get; set; } = string.Empty;

            public string Cursor { get; set; } = string.Empty;
        }

        private static Task<Shown> ShownAsync(EditorPage editor) =>
            editor.Page.EvaluateAsync<Shown>(
                """
                () => {
                    const editor = monaco.editor.getEditors()[0];
                    const selection = editor.getSelection();
                    const cursor = selection.isEmpty()
                        ? `${selection.startLineNumber}:${selection.startColumn}`
                        : `${selection.startLineNumber}:${selection.startColumn}-${selection.endLineNumber}:${selection.endColumn}`;
                    return { text: editor.getModel().getValue(monaco.editor.EndOfLinePreference.LF), cursor };
                }
                """);

        /// <param name="cursor">Line and column, or two of them with a dash between for a selection.</param>
        private static async Task ShouldShowAsync(EditorPage editor, string text, string cursor)
        {
            var expected = new Shown { Text = text, Cursor = cursor };
            var shown = await ShownAsync(editor);
            for (var attempt = 0; attempt < 100 && shown != expected; attempt++)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
                shown = await ShownAsync(editor);
            }

            shown.ShouldBe(expected);
        }

        [Fact]
        public async Task The_key_for_grapes_brings_the_watermelon_and_Enter_between_them_opens_the_block()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, "🏁 ", 1, 4);

            await editor.Page.Keyboard.PressAsync("Shift+BracketLeft");
            await ShouldShowAsync(editor, "🏁 🍇🍉", "1:6");

            await editor.Page.Keyboard.PressAsync("Enter");
            await ShouldShowAsync(editor, "🏁 🍇\n  \n🍉", "2:3");
        }

        [Fact]
        public async Task Enter_after_grapes_that_are_still_open_indents_the_next_line()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, "🏁 🍇", 1, 6);

            await editor.Page.Keyboard.PressAsync("Enter");

            await ShouldShowAsync(editor, "🏁 🍇\n  ", "2:3");
        }

        [Theory]
        [InlineData("Shift+Digit9", "↪️ ", "↪️ 🤜🤛", "the brackets around part of an expression")]
        [InlineData("Shift+Quote", "😀 ", "😀 🔤🔤", "a string")]
        public async Task The_key_for_an_opener_brings_its_closer(string key, string before, string after, string what)
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, before, 1, 100);

            await editor.Page.Keyboard.PressAsync(key);

            (await ShownAsync(editor)).ShouldNotBeNull(what);
            await ShouldShowAsync(editor, after, "1:6");
        }

        [Theory]
        [InlineData("Shift+BracketRight", "🏁 🍇🍉", 6, "1:8")]
        [InlineData("Shift+Digit0", "↪️ 🤜a🤛", 7, "1:9")]
        [InlineData("Shift+Quote", "😀 🔤Hi🔤", 8, "1:10")]
        public async Task The_key_for_a_closer_that_is_already_there_steps_over_it(string key, string text, int column, string cursorAfter)
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, text, 1, column);

            await editor.Page.Keyboard.PressAsync(key);

            await ShouldShowAsync(editor, text, cursorAfter);
        }

        [Fact]
        public async Task The_watermelon_typed_on_an_empty_line_lines_up_with_its_grapes()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, "🏁 🍇\n  ↪️ 👍 🍇\n    😀 🔤hi🔤❗️\n    ", 4, 5);

            await editor.Page.Keyboard.PressAsync("Shift+BracketRight");

            await ShouldShowAsync(editor, "🏁 🍇\n  ↪️ 👍 🍇\n    😀 🔤hi🔤❗️\n  🍉", "4:5");
        }

        [Fact]
        public async Task The_key_for_an_opener_wraps_what_is_selected_and_keeps_it_selected()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, "a ➕ b ➕ c", 1, 1);
            await editor.Page.EvaluateAsync("() => monaco.editor.getEditors()[0].setSelection(new monaco.Range(1, 5, 1, 10))");

            await editor.Page.Keyboard.PressAsync("Shift+Digit9");

            await ShouldShowAsync(editor, "a ➕ 🤜b ➕ c🤛", "1:7-1:12");
        }

        [Fact]
        public async Task Two_keys_pressed_one_straight_after_the_other_are_typed_in_that_order()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, "🏁 ", 1, 4);

            // The second is pressed before the server has answered the first.
            await editor.Page.Keyboard.PressAsync("Shift+BracketLeft");
            await editor.Page.Keyboard.PressAsync("Shift+BracketRight");

            await ShouldShowAsync(editor, "🏁 🍇🍉", "1:8");
        }

        [Theory]
        [InlineData("🏁 🍇🍉", 6, "🏁 ")]
        [InlineData("↪️ 🤜🤛", 6, "↪️ ")]
        [InlineData("➡️ 🍿🍆", 6, "➡️ ")]
        [InlineData("🍨🐚🍆", 5, "🍨")]
        [InlineData("😀 🔤🔤", 6, "😀 ")]
        public async Task Backspace_between_an_opener_and_its_closer_takes_both_and_undo_brings_both_back(string text, int column, string left)
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, text, 1, column);

            await editor.Page.Keyboard.PressAsync("Backspace");
            await ShouldShowAsync(editor, left, $"1:{left.Length + 1}");

            await editor.Page.Keyboard.PressAsync("ControlOrMeta+z");
            (await ShownAsync(editor)).Text.ShouldBe(text);
        }

        [Theory]
        [InlineData("😀 🔤🍇🍉🔤❗️", 8, "😀 🔤🍉🔤❗️", "inside a string")]
        [InlineData("💭 🍇🍉", 6, "💭 🍉", "inside a comment")]
        [InlineData("📗 🍇🍉 📗", 6, "📗 🍉 📗", "inside documentation")]
        [InlineData("😀 🔤❌🔤🍇🍉🔤", 11, "😀 🔤❌🔤🍉🔤", "inside a string, after a quote that is part of it")]
        [InlineData("😀 🔤a🔤🔤b🔤", 9, "😀 🔤a🔤b🔤", "between two strings")]
        [InlineData("🏁 🍇 🍉", 6, "🏁  🍉", "with something between them")]
        [InlineData("😀 🔤❌🔤🔤", 9, "😀 🔤❌🔤", "between a quote that is part of a string and the one that ends it")]
        public async Task Backspace_takes_one_emoji_where_the_two_are_not_an_empty_pair(string text, int column, string left, string where)
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, text, 1, column);

            await editor.Page.Keyboard.PressAsync("Backspace");

            await ShouldShowAsync(editor, left, $"1:{column - 2}");
            where.ShouldNotBeEmpty();
        }

        [Fact]
        public async Task A_quote_typed_and_taken_back_leaves_nothing_behind()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, "😀 ", 1, 4);
            await editor.Page.Keyboard.PressAsync("Shift+Quote");
            await ShouldShowAsync(editor, "😀 🔤🔤", "1:6");

            await editor.Page.Keyboard.PressAsync("Backspace");

            // A quote left behind would make the rest of the file a string, and every key plain.
            await ShouldShowAsync(editor, "😀 ", "1:4");
        }

        [Fact]
        public async Task The_key_for_a_block_comment_leaves_the_cursor_inside_the_comment()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, "line1\n\nline3", 2, 1);

            await editor.Page.Keyboard.PressAsync("ControlOrMeta+Shift+Slash");
            await ShouldShowAsync(editor, "line1\n💭🔜\n🔚💭\nline3", "2:5");

            await editor.Page.Keyboard.TypeAsync(" note");
            await ShouldShowAsync(editor, "line1\n💭🔜 note\n🔚💭\nline3", "2:10");
        }

        [Fact]
        public async Task A_key_pressed_in_the_find_box_is_typed_there_and_not_into_the_file()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            const string text = "😀 🔤Hello World!🔤❗️";
            await PutAsync(editor, text, 1, 1);
            await editor.Page.Keyboard.PressAsync("ControlOrMeta+f");
            var find = editor.Page.Locator(".monaco-editor .find-widget .find-part textarea").First;
            await Assertions.Expect(find).ToBeFocusedAsync();
            await editor.Page.Keyboard.TypeAsync("World");

            await editor.Page.Keyboard.PressAsync("Shift+Digit1");

            await Assertions.Expect(find).ToHaveValueAsync("World!");
            (await ShownAsync(editor)).Text.ShouldBe(text);
        }

        [Fact]
        public async Task The_cursor_beside_one_half_of_a_pair_shows_the_other_half()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, "🏁 🍇\n  🍿 1 2 🍆 ➡️ numbers\n🍉", 1, 4);

            await Assertions.Expect(editor.Page.Locator(".monaco-editor .bracket-match")).ToHaveCountAsync(2);
        }

        [Fact]
        public async Task A_line_can_be_commented_out_and_back_with_the_thought_bubble()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutAsync(editor, "🏁 🍇\n  😀 🔤hi🔤❗️\n🍉", 2, 3);

            await editor.Page.EvaluateAsync("() => monaco.editor.getEditors()[0].getAction('editor.action.commentLine').run()");
            (await ShownAsync(editor)).Text.ShouldBe("🏁 🍇\n  💭 😀 🔤hi🔤❗️\n🍉");

            await editor.Page.EvaluateAsync("() => monaco.editor.getEditors()[0].getAction('editor.action.commentLine').run()");
            (await ShownAsync(editor)).Text.ShouldBe("🏁 🍇\n  😀 🔤hi🔤❗️\n🍉");
        }
    }
}
