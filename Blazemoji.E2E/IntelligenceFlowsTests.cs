using System.Diagnostics;

namespace Blazemoji.E2E
{
    /// <summary>
    /// The editor's help: suggestions, parameter lists, and problems while typing.
    /// </summary>
    public class IntelligenceFlowsTests(BrowserFixture browser)
    {
        private const string NoGrapevine = "The Grapevine package is not built into this stack. Run scripts/build-grapevine.sh.";

        private static async Task<EditorPage> OpenTodoStartupAsync(BrowserFixture browser)
        {
            var editor = await EditorPage.OpenAsync(browser);
            if (!await editor.HasTemplateAsync("grapevine-todo"))
            {
                await editor.DisposeAsync();
                Assert.Skip(NoGrapevine);
            }

            await editor.CreateProjectAsync("Todo API", "grapevine-todo");
            await editor.OpenFileAsync("Todo API", "startup.🍇");
            return editor;
        }

        [Fact]
        public async Task A_dot_after_the_app_offers_its_routing_methods_and_says_what_they_do()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await OpenTodoStartupAsync(browser);

            await editor.StartLineAfterAsync("➡️ app");
            await editor.TypeAsync("app.");

            var offered = await editor.SuggestionTextsAsync();
            offered.ShouldContain(text => text.StartsWith("🧱 layer") && text.Contains("🍷"));
            offered.ShouldContain(text => text.StartsWith("📥 path handler"));
            offered.ShouldContain(text => text.StartsWith("📮 path handler"));
            offered.ShouldContain(text => text.StartsWith("✏ path handler"));
            offered.ShouldContain(text => text.StartsWith("🗑 path handler"));

            await editor.ShowSuggestionDetailsAsync();
            await editor.SuggestionDetails.GetByText("Adds middleware to the pipeline").WaitForAsync();
            (await editor.SuggestionDetails.InnerTextAsync()).ShouldContain("🧱 app layer");
            editor.ConsoleErrors.ShouldBeEmpty();
            await editor.ScreenshotAsync("p4-01-methods-after-a-dot");
        }

        [Fact]
        public async Task Accepting_a_method_writes_the_call_and_shows_its_parameters_one_at_a_time()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await OpenTodoStartupAsync(browser);
            await editor.StartLineAfterAsync("➡️ app");

            await editor.TypeAsync("app.inb");
            (await editor.SuggestionTextsAsync()).ShouldHaveSingleItem().ShouldStartWith("📥 path handler");
            await editor.Page.Keyboard.PressAsync("Tab");

            await editor.ParameterHints.GetByText("handler").First.WaitForAsync();
            (await editor.LineAtCursorAsync()).Trim().ShouldBe("📥 app");
            (await editor.ActiveParameter.InnerTextAsync()).ShouldBe("path 🔡");
            await editor.ScreenshotAsync("p4-02-parameters");

            await editor.TypeAsync("🔤/health🔤 ");

            await editor.ParameterHints.Locator(".parameter.active", new LocatorLocatorOptions { HasTextString = "handler" }).WaitForAsync();
            (await editor.ActiveParameter.InnerTextAsync()).ShouldBe("handler 🍇📨➡️📬🍉");
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_mistake_is_marked_about_a_second_after_it_is_typed_and_cleared_when_it_is_fixed()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.WaitForMarkerCountAsync(0);

            await editor.StartLineAfterAsync("Hello World!");
            await editor.TypeAsync("😀 nope❗️");
            var typed = Stopwatch.StartNew();
            await editor.WaitForMarkerCountAsync(1);
            var marked = typed.Elapsed;

            (await editor.MarkersAsync())[0].Message.ShouldBe("Variable \"nope\" not defined.");
            (await editor.RunStatus.InnerTextAsync()).ShouldBe("Ready");
            (await editor.Page.Locator("[role=tab] .mud-badge").InnerTextAsync()).Trim().ShouldBe("1");
            await editor.ScreenshotAsync("p4-03-marked-while-typing");
            TestContext.Current.TestOutputHelper?.WriteLine($"Marked {marked.TotalMilliseconds:0} ms after the last key.");
            marked.ShouldBeLessThan(TimeSpan.FromSeconds(3));

            for (var i = 0; i < "😀 nope❗️".EnumerateRunes().Count(); i++)
                await editor.Page.Keyboard.PressAsync("Backspace");

            await editor.WaitForMarkerCountAsync(0);
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task An_emoji_is_found_by_its_name_after_a_colon()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.StartLineAfterAsync("Hello World!");
            await editor.TypeAsync(":grap");

            (await editor.SuggestionTextsAsync())[0].ShouldStartWith("🍇");
            await editor.Page.Keyboard.PressAsync("Tab");
            await editor.Page.WaitForFunctionAsync("() => { const editor = monaco.editor.getEditors()[0]; return editor.getModel().getLineContent(editor.getPosition().lineNumber).trim() === '🍇'; }");
            await editor.ScreenshotAsync("p4-04-emoji-by-name");
        }

        [Fact]
        public async Task Hovering_a_keyword_says_what_it_does()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.Page.EvaluateAsync(
                """
                () => {
                    const editor = monaco.editor.getEditors()[0];
                    const found = editor.getModel().findMatches('😀', false, false, true, null, false)[0];
                    editor.setPosition({ lineNumber: found.range.startLineNumber, column: found.range.startColumn });
                    editor.focus();
                    editor.trigger('test', 'editor.action.showHover', {});
                }
                """);

            await editor.Page.Locator(".monaco-hover-content").GetByText("Prints output").WaitForAsync();
        }
    }
}
