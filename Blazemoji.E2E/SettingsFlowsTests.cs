namespace Blazemoji.E2E
{
    /// <summary>
    /// Settings from the panel to the editor and back after a reload. Only a real browser has
    /// local storage, draws Monaco's text at a size, and gives a dialog's focus back.
    /// </summary>
    public class SettingsFlowsTests(BrowserFixture browser)
    {
        // After a reload the editor is not there at first, and a predicate that throws ends the wait.
        private const string FontSizeIs = "size => { const lines = document.querySelector('.monaco-editor .view-lines'); return !!lines && getComputedStyle(lines).fontSize === size; }";
        private const string MinimapIs = "on => monaco.editor.getEditors()[0].getOption(monaco.editor.EditorOption.minimap).enabled === on";

        // Records the size of the editor's text at the moment the editor is first shown, which
        // is too brief a moment to catch from outside the page.
        private const string RememberTheSizeWhenShown = @"(() => {
            const look = () => {
                const editor = document.querySelector('.editor');
                const lines = document.querySelector('.monaco-editor .view-lines');
                if (editor && lines && !editor.classList.contains('invisible')) {
                    window.__fontSizeWhenShown = getComputedStyle(lines).fontSize;
                    return;
                }
                requestAnimationFrame(look);
            };
            requestAnimationFrame(look);
        })();";

        private static async Task SetFontSizeAsync(EditorPage editor, string size)
        {
            var field = editor.Setting("FontSize").Locator("input");
            await field.FillAsync(size);
            await field.PressAsync("Tab");
            await editor.Page.WaitForFunctionAsync(FontSizeIs, size + "px");
        }

        [Theory]
        [InlineData(1920, 1080)]
        [InlineData(1024, 768)]
        public async Task A_font_size_chosen_in_the_panel_changes_the_editors_text_without_a_reload(int width, int height)
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.Page.SetViewportSizeAsync(width, height);
            (await editor.FontSizeAsync()).ShouldBe("14px");

            await editor.OpenSettingsAsync();
            await SetFontSizeAsync(editor, "20");

            (await editor.FontSizeAsync()).ShouldBe("20px");
            await editor.CloseSettingsAsync();
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Theory]
        [InlineData(1920, 1080)]
        [InlineData(1024, 768)]
        public async Task Turning_the_minimap_off_takes_it_away_and_restoring_defaults_brings_it_back(int width, int height)
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.Page.SetViewportSizeAsync(width, height);
            await editor.OpenSettingsAsync();

            await editor.Setting("Minimap").Locator(".mud-switch").ClickAsync();
            await editor.Page.WaitForFunctionAsync(MinimapIs, false);

            await editor.Page.GetByTestId("restore-defaults").ClickAsync();
            await editor.Page.WaitForFunctionAsync(MinimapIs, true);
        }

        [Fact]
        public async Task After_a_reload_the_text_is_the_chosen_size_when_the_editor_first_shows()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.OpenSettingsAsync();
            await SetFontSizeAsync(editor, "20");
            await editor.CloseSettingsAsync();

            await editor.Page.AddInitScriptAsync(RememberTheSizeWhenShown);
            await editor.Page.ReloadAsync();
            await editor.Page.WaitForFunctionAsync("() => window.__fontSizeWhenShown !== undefined");

            (await editor.Page.EvaluateAsync<string>("() => window.__fontSizeWhenShown")).ShouldBe("20px");
            (await editor.FontSizeAsync()).ShouldBe("20px");
        }

        [Fact]
        public async Task Another_visitor_has_the_defaults()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var one = await EditorPage.OpenAsync(browser);
            await one.OpenSettingsAsync();
            await SetFontSizeAsync(one, "20");

            await using var other = await EditorPage.OpenAsync(browser);

            (await other.FontSizeAsync()).ShouldBe("14px");
            (await one.FontSizeAsync()).ShouldBe("20px");
        }

        [Fact]
        public async Task Clearing_the_saved_files_in_the_library_leaves_the_settings_alone()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.OpenSettingsAsync();
            await SetFontSizeAsync(editor, "20");
            await editor.CloseSettingsAsync();

            await editor.OpenTabAsync("Library");
            await editor.Page.GetByTestId("clear-saved").ClickAsync();
            await editor.Dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();
            await editor.Dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
            await editor.Page.ReloadAsync();

            await editor.Page.WaitForFunctionAsync(FontSizeIs, "20px");
        }

        [Fact]
        public async Task After_the_panel_is_closed_no_tooltip_is_left_over_the_title_bar()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.OpenSettingsAsync();

            await editor.CloseSettingsAsync();
            await editor.Page.WaitForTimeoutAsync(600);

            (await editor.Page.Locator(".mud-tooltip.mud-popover-open").CountAsync()).ShouldBe(0);
            await editor.ThemeToggle.ClickAsync();
            await editor.Page.WaitForFunctionAsync("() => document.querySelector('.monaco-editor.vs-dark') !== null");
        }
    }
}
