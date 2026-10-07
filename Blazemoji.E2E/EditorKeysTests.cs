namespace Blazemoji.E2E
{
    /// <summary>
    /// What single keys do in the editor where Emojicode needs something other than what
    /// Monaco does by itself.
    /// </summary>
    public class EditorKeysTests(BrowserFixture browser)
    {
        private static Task PutCursorAfterAsync(EditorPage editor, string text) =>
            editor.Page.EvaluateAsync(
                "text => { const editor = monaco.editor.getEditors()[0]; editor.getModel().setValue('x ' + text); editor.setPosition({ lineNumber: 1, column: 1000 }); editor.focus(); }",
                text);

        private static async Task<string> LeftAfterAsync(EditorPage editor, string expected)
        {
            await editor.Page.WaitForFunctionAsync("expected => monaco.editor.getEditors()[0].getModel().getValue() === 'x ' + expected", expected, new PageWaitForFunctionOptions { Timeout = 3000 });
            return (await editor.GetCodeAsync())[2..];
        }

        // Each of these is one emoji made of several characters. Monaco knows the first four
        // kinds only when the first character is in a table it carries, and these are not.
        [Theory]
        [InlineData("↩️", "return: an arrow and the selector that makes it an emoji")]
        [InlineData("↪️", "if")]
        [InlineData("◀️", "less than")]
        [InlineData("▶️", "greater than")]
        [InlineData("⬅️", "assign by operator")]
        [InlineData("⁉️", "call a closure")]
        [InlineData("⤴️", "the superclass")]
        [InlineData("🤷‍♀️", "no value: two emoji joined")]
        [InlineData("1️⃣", "a keycap")]
        [InlineData("🇩🇪", "a flag")]
        [InlineData("➡️", "one Monaco already took whole")]
        [InlineData("👍🏽", "one with a skin tone")]
        public async Task Backspace_takes_a_whole_emoji_and_leaves_no_part_of_it(string emoji, string what)
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutCursorAfterAsync(editor, emoji);

            await editor.Page.Keyboard.PressAsync("Backspace");

            (await LeftAfterAsync(editor, string.Empty)).ShouldBe(string.Empty, what);
        }

        [Fact]
        public async Task Undo_brings_the_whole_emoji_back()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutCursorAfterAsync(editor, "↩️");
            await editor.Page.Keyboard.PressAsync("Backspace");
            await LeftAfterAsync(editor, string.Empty);

            await editor.Page.Keyboard.PressAsync("ControlOrMeta+z");

            (await LeftAfterAsync(editor, "↩️")).ShouldBe("↩️");
        }

        [Theory]
        [InlineData("ab", "a", "a plain letter")]
        [InlineData("é", "e", "an accent typed after its letter comes off by itself, as Monaco has it")]
        [InlineData("🍇🍉", "🍇", "one emoji of two")]
        public async Task Backspace_takes_no_more_than_it_did_before(string text, string left, string what)
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutCursorAfterAsync(editor, text);

            await editor.Page.Keyboard.PressAsync("Backspace");

            (await LeftAfterAsync(editor, left)).ShouldBe(left, what);
        }

        [Fact]
        public async Task Backspace_on_a_selection_removes_the_selection_and_nothing_else()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await PutCursorAfterAsync(editor, "↩️ab");
            await editor.Page.EvaluateAsync("() => monaco.editor.getEditors()[0].setSelection(new monaco.Range(1, 5, 1, 7))");

            await editor.Page.Keyboard.PressAsync("Backspace");

            (await LeftAfterAsync(editor, "↩️")).ShouldBe("↩️");
        }

        [Fact]
        public async Task The_exclamation_key_is_a_plain_mark_inside_a_string_and_the_emoji_outside()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.SetCodeAsync("🏁 🍇\n  😀 🔤Hello\n🍉\n");

            // The end of line 2 is inside the string, which has not been closed yet.
            await editor.Page.EvaluateAsync("() => { const editor = monaco.editor.getEditors()[0]; editor.setPosition({ lineNumber: 2, column: 100 }); editor.focus(); }");
            await editor.Page.Keyboard.PressAsync("Shift+Digit1");
            await editor.Page.WaitForFunctionAsync("() => monaco.editor.getEditors()[0].getModel().getLineContent(2).endsWith('Hello!')");

            // Closing the string puts the cursor back in code, where the same key ends the call.
            await editor.Page.Keyboard.PressAsync("Shift+Quote");
            await editor.Page.WaitForFunctionAsync("() => monaco.editor.getEditors()[0].getModel().getLineContent(2).endsWith('Hello!🔤')");
            await editor.Page.Keyboard.PressAsync("Shift+Digit1");
            await editor.Page.WaitForFunctionAsync("() => monaco.editor.getEditors()[0].getModel().getLineContent(2).endsWith('Hello!🔤❗')");

            await editor.RunButton.ClickAsync();
            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["Hello!"]);
        }
    }
}
