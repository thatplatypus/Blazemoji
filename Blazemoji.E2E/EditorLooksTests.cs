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

        // Every kind of text the editor draws against everything it is drawn on, as the
        // ratio WCAG asks to be 4.5 or more. Each answer is "what on what: ratio" for one that is not.
        private const string TextThatIsHardToRead =
            """
            () => {
                const parse = colour => (colour.match(/[\d.]+/g) ?? []).map(Number);
                const hex = colour => { const value = colour.trim().slice(1); const part = at => parseInt(value.slice(at, at + 2), 16); return [part(0), part(2), part(4), value.length > 6 ? part(6) / 255 : 1]; };
                const read = colour => colour.trim().startsWith('#') ? hex(colour) : (([r, g, b, a = 1]) => [r, g, b, a])(parse(colour));
                const over = (top, bottom) => top.slice(0, 3).map((part, at) => part * top[3] + bottom[at] * (1 - top[3]));
                const light = ([r, g, b]) => { const line = part => { const s = part / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; }; return 0.2126 * line(r) + 0.7152 * line(g) + 0.0722 * line(b); };
                const ratio = (a, b) => { const [more, less] = [light(a), light(b)].sort((x, y) => y - x); return (more + 0.05) / (less + 0.05); };

                const editor = document.querySelector('.monaco-editor');
                const theme = getComputedStyle(editor);
                const page = read(theme.getPropertyValue('--vscode-editor-background'));
                const span = word => [...document.querySelectorAll('.monaco-editor .view-line span span')].find(candidate => candidate.textContent.includes(word));
                const texts = { code: span('plain'), comment: span('remark'), string: span('written') };
                const grounds = ['editor-background', 'editor-selectionBackground', 'editor-inactiveSelectionBackground', 'editor-findMatchBackground', 'editor-findMatchHighlightBackground',
                    'editor-wordHighlightBackground', 'editor-wordHighlightStrongBackground', 'editor-selectionHighlightBackground', 'editor-lineHighlightBackground',
                    'editorSuggestWidget-selectedBackground'];
                const hard = [];
                for (const ground of grounds) {
                    const value = theme.getPropertyValue('--vscode-' + ground);
                    if (!value.trim()) { hard.push(ground + ': not set'); continue; }
                    const behind = over(read(value), page);
                    for (const [name, element] of Object.entries(texts)) {
                        if (!element) { hard.push(name + ': not found'); continue; }
                        const contrast = ratio(parse(getComputedStyle(element).color), behind);
                        if (contrast < 4.5) hard.push(`${name} on ${ground}: ${contrast.toFixed(2)}`);
                    }
                }
                return hard;
            }
            """;

        [Theory]
        [InlineData(ColorScheme.Light)]
        [InlineData(ColorScheme.Dark)]
        public async Task Code_comments_and_strings_can_be_read_on_everything_the_editor_draws_them_on(ColorScheme scheme)
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser, scheme);
            await ShouldHaveThePagesColoursAsync(editor, scheme == ColorScheme.Dark ? "34,34,38" : "255,255,255");
            await editor.SetCodeAsync("💭 remark\n🏁 🍇 12 ➡️ plain 🍉\n😀 🔤written🔤❗️");

            // The text is drawn first and coloured a moment later. Until it is, all of it is the
            // colour of code, and measuring then would say nothing about comments or strings.
            const string coloured =
                """
                () => {
                    const colour = word => { const span = [...document.querySelectorAll('.monaco-editor .view-line span span')].find(candidate => candidate.textContent.includes(word)); return span ? getComputedStyle(span).color : null; };
                    const [code, comment, text] = [colour('plain'), colour('remark'), colour('written')];
                    return Boolean(code && comment && text) && comment !== code && text !== code;
                }
                """;
            await editor.Page.WaitForFunctionAsync(coloured, null, new PageWaitForFunctionOptions { Timeout = 10_000 });

            var hard = await editor.Page.EvaluateAsync<string[]>(TextThatIsHardToRead);

            hard.ShouldBeEmpty();
        }

        [Fact]
        public async Task Every_flat_dark_shape_in_a_long_file_is_marked_and_the_marks_of_a_block_comment_are_among_them()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.SetCodeAsync("💭🔜 a 🔚💭\n" + string.Concat(Enumerable.Repeat("a ➕ b\n", 1100)));

            await Assertions.Expect(editor.Page.Locator(".monaco-editor .view-line .flat-dark-glyph").First).ToHaveTextAsync("🔜");
            await Assertions.Expect(editor.Page.Locator(".monaco-editor .view-line .flat-dark-glyph").Nth(1)).ToHaveTextAsync("🔚");
            // The marks are made once per frame, a moment after the text changes.
            const string marked = "() => monaco.editor.getEditors()[0].getModel().getAllDecorations().filter(mark => mark.options.inlineClassName === 'flat-dark-glyph').length";
            var count = await editor.Page.EvaluateAsync<int>(marked);
            for (var attempt = 0; attempt < 100 && count != 1102; attempt++)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
                count = await editor.Page.EvaluateAsync<int>(marked);
            }

            count.ShouldBe(1102);
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
