namespace Blazemoji.E2E
{
    /// <summary>The editor takes its colours from the page it is on.</summary>
    public class EditorLooksTests(BrowserFixture browser)
    {
        // The red, green and blue of the editor's background, of the text inside the first
        // string, and of the two palette colours they should be.
        private const string Colours =
            """
            () => {
                const numbers = colour => (colour.match(/[\d.]+/g) ?? []).slice(0, 3).map(Number).join(',');
                const palette = getComputedStyle(document.documentElement);
                // Monaco draws a space as one that does not break, so only one word is looked for.
                const inString = [...document.querySelectorAll('.monaco-editor .view-line span span')].find(span => span.textContent.includes('Hello'));
                return {
                    editor: numbers(getComputedStyle(document.querySelector('.monaco-editor .monaco-editor-background')).backgroundColor),
                    surface: numbers(palette.getPropertyValue('--mud-palette-surface')),
                    text: inString ? numbers(getComputedStyle(inString).color) : '',
                    accent: numbers(palette.getPropertyValue('--mud-palette-primary')),
                };
            }
            """;

        // Playwright fills this in, for which it needs to be able to make an empty one.
        private sealed record Look
        {
            public string Editor { get; set; } = string.Empty;

            public string Surface { get; set; } = string.Empty;

            public string Text { get; set; } = string.Empty;

            public string Accent { get; set; } = string.Empty;
        }

        private static async Task ShouldHaveThePagesColoursAsync(EditorPage editor, string surface)
        {
            var look = await editor.Page.EvaluateAsync<Look>(Colours);
            for (var attempt = 0; attempt < 100 && (look.Editor != surface || look.Text != look.Accent); attempt++)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
                look = await editor.Page.EvaluateAsync<Look>(Colours);
            }

            look.Surface.ShouldBe(surface);
            look.Editor.ShouldBe(look.Surface);
            look.Text.ShouldBe(look.Accent);
        }

        [Fact]
        public async Task The_editor_is_the_colour_of_the_pages_panels_and_writes_strings_in_the_accent_light_and_dark()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await ShouldHaveThePagesColoursAsync(editor, "255,255,255");

            await editor.ThemeToggle.ClickAsync();
            await ShouldHaveThePagesColoursAsync(editor, "34,34,38");

            await editor.ThemeToggle.ClickAsync();
            await ShouldHaveThePagesColoursAsync(editor, "255,255,255");
        }

        [Fact]
        public async Task A_page_that_starts_dark_has_a_dark_editor_from_the_start()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser, ColorScheme.Dark);

            await ShouldHaveThePagesColoursAsync(editor, "34,34,38");
        }

        [Fact]
        public async Task Documentation_is_written_as_quietly_as_a_comment_and_code_is_not()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.SetCodeAsync("💭 remark\n📗 Says hello.\nOn two lines. 📗\n🏁 🍇 12 ➡️ plain 🍉\n📘 About the package 📘");
            var colourOf = "word => { const span = [...document.querySelectorAll('.monaco-editor .view-line span span')].find(span => span.textContent.includes(word)); return span ? getComputedStyle(span).color : 'not found'; }";
            await Assertions.Expect(editor.EditorText.GetByText("remark")).ToBeVisibleAsync();

            // The text is drawn first and coloured a moment later.
            var code = await editor.Page.EvaluateAsync<string>(colourOf, "plain");
            var comment = await editor.Page.EvaluateAsync<string>(colourOf, "remark");
            for (var attempt = 0; attempt < 100 && comment == code; attempt++)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
                comment = await editor.Page.EvaluateAsync<string>(colourOf, "remark");
            }

            comment.ShouldNotBe(code);
            (await editor.Page.EvaluateAsync<string>(colourOf, "Says")).ShouldBe(comment);
            (await editor.Page.EvaluateAsync<string>(colourOf, "lines")).ShouldBe(comment);
            (await editor.Page.EvaluateAsync<string>(colourOf, "About")).ShouldBe(comment);
        }

        [Fact]
        public async Task The_flat_dark_operators_are_turned_light_on_a_dark_page_and_left_alone_on_a_light_one()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.SetCodeAsync("a ➕ b ➖ c ✖️ d ➗ e ➡️ f ⚫ ✖");
            var marked = editor.Page.Locator(".monaco-editor .view-line .flat-dark-glyph");

            // The arrow has colours of its own, the black circle would become the white one,
            // and the last cross has no selector after it, so it is text and not an emoji.
            await Assertions.Expect(marked).ToHaveTextAsync(["➕", "➖", "✖️", "➗"]);
            (await marked.First.EvaluateAsync<string>("glyph => getComputedStyle(glyph).filter")).ShouldBe("none");

            await editor.ThemeToggle.ClickAsync();
            await Assertions.Expect(editor.Page.Locator(".monaco-editor.vs-dark")).ToBeVisibleAsync();
            (await marked.First.EvaluateAsync<string>("glyph => getComputedStyle(glyph).filter")).ShouldBe("invert(1)");
        }
    }
}
