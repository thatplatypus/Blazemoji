namespace Blazemoji.E2E
{
    public class ShellFlowsTests(BrowserFixture browser)
    {
        [Fact]
        public async Task The_editor_loads_when_the_Monaco_script_arrives_late()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);

            // On a first visit nothing is cached, and Monaco's main script is large. If Blazor
            // starts before it has loaded, the editor is never created.
            await using var editor = await EditorPage.OpenAsync(browser, beforeNavigation: page =>
                page.RouteAsync("**/editor.main.js", async route =>
                {
                    await Task.Delay(3000);
                    await route.ContinueAsync();
                }));

            (await editor.GetCodeAsync()).ShouldContain("Hello World!");
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task The_theme_toggle_switches_the_editor_to_dark_and_back()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var darkEditor = editor.Page.Locator(".monaco-editor.vs-dark");

            await editor.ThemeToggle.ClickAsync();
            await darkEditor.First.WaitForAsync();
            await editor.ScreenshotAsync("p1-08-dark");

            await editor.ThemeToggle.ClickAsync();
            await darkEditor.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        }

        [Fact]
        public async Task An_emoji_chosen_in_the_picker_is_inserted_into_the_editor()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var before = await editor.GetCodeAsync();

            await editor.Page.GetByLabel("Open Emoji Picker").ClickAsync();
            var firstEmoji = editor.Page.Locator(".mud-popover-open .emoji-box-hover").First;
            await firstEmoji.WaitForAsync();
            var emoji = (await firstEmoji.InnerTextAsync()).Trim();
            await firstEmoji.ClickAsync();

            await editor.Page.WaitForFunctionAsync(
                "length => monaco.editor.getModels()[0].getValue().length > length", before.Length);
            (await editor.GetCodeAsync()).ShouldContain(emoji);
        }

        [Fact]
        public async Task The_save_dialog_closes_on_Escape_without_saving()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.SaveButton.ClickAsync();
            await editor.Dialog.GetByText("Enter File Name").WaitForAsync();
            await editor.Dialog.Locator("input").First.ClickAsync();
            await editor.Page.Keyboard.PressAsync("Escape");
            await editor.Dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

            await editor.OpenTabAsync("Library");
            (await editor.Page.Locator(".mud-treeview-item", new PageLocatorOptions { HasTextString = "Untitled.🍇" }).CountAsync()).ShouldBe(0);
        }

        [Fact]
        public async Task The_key_commands_dialog_opens_and_closes()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.KeyCommandsButton.ClickAsync();
            var close = editor.Dialog.Locator(".mud-dialog-actions").GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" });
            await close.WaitForAsync();
            (await editor.Dialog.InnerTextAsync()).ShouldContain("Shift + BracketLeft");

            await close.ClickAsync();
            await editor.Dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        }

        [Fact]
        public async Task A_saved_file_can_be_loaded_back_from_the_library()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.SetCodeAsync(Programs.PrintsMarker("saved"));

            await editor.SaveButton.ClickAsync();
            await editor.Dialog.Locator("input").First.FillAsync("Kept");
            await editor.Dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Ok" }).ClickAsync();
            await editor.Dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

            await editor.LoadSampleAsync("Fizzbuzz");
            await editor.Page.WaitForFunctionAsync("() => !monaco.editor.getModels()[0].getValue().includes('marker-saved')");
            await editor.LoadSampleAsync("Kept.🍇");

            await editor.Page.WaitForFunctionAsync("() => monaco.editor.getModels()[0].getValue().includes('marker-saved')");
        }

        [Fact]
        public async Task The_toolbox_lists_the_keyword_catalog()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.OpenTabAsync("Toolbox");

            (await editor.Page.Locator(".emoji-box-hover").CountAsync()).ShouldBeGreaterThan(20);
        }
    }
}
