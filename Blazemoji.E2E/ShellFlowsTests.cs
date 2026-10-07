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
        public async Task The_editor_loads_on_every_one_of_many_cold_page_loads()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);

            // Monaco defines its global a little after its script has run. Blazor used to be able
            // to ask for the editor inside that gap, roughly once in four cold loads.
            for (var load = 1; load <= 15; load++)
            {
                await using var editor = await EditorPage.OpenAsync(browser, readyTimeoutMilliseconds: 10_000);
                editor.ConsoleErrors.ShouldBeEmpty($"page load {load}");
            }
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
            await editor.Page.Locator(".emoji-box-hover").First.WaitForAsync();

            (await editor.Page.Locator(".emoji-box-hover").CountAsync()).ShouldBeGreaterThan(20);
        }

        [Fact]
        public async Task Copy_in_the_toolbox_puts_the_emoji_on_the_clipboard()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
            await editor.OpenTabAsync("Toolbox");
            await editor.Page.Locator(".emoji-box-hover").First.WaitForAsync();

            await editor.Page.Locator("button[title=Copy]").First.ClickAsync();

            // The click goes to the server and comes back as a call to the library's script, so
            // the clipboard is read until it has something. Playwright's own wait cannot do
            // this: it does not wait for a promise, and reading the clipboard returns one.
            var copied = string.Empty;
            var giveUp = DateTime.UtcNow.AddSeconds(15);
            while (string.IsNullOrEmpty(copied) && DateTime.UtcNow < giveUp)
            {
                copied = await editor.Page.EvaluateAsync<string>("() => navigator.clipboard.readText()") ?? string.Empty;
                if (copied.Length == 0)
                    await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            copied.ShouldNotBeNullOrWhiteSpace($"nothing reached the clipboard; console errors: {string.Join(" | ", editor.ConsoleErrors)}");
            copied.Length.ShouldBeLessThan(12);
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task Buttons_and_tabs_are_written_as_typed_and_not_in_capitals()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            var transforms = await editor.Page.EvaluateAsync<string[]>(
                """
                () => [
                    document.querySelector('[data-testid=run-button]'),
                    document.querySelector('[data-testid=stop-button]'),
                    ...document.querySelectorAll('[role=tab]'),
                ].map(element => `${element.textContent.trim()}: ${getComputedStyle(element).textTransform}`)
                """);

            transforms.Length.ShouldBe(8);
            transforms.ShouldAllBe(transform => transform.EndsWith(": none"));
        }

        [Fact]
        public async Task The_count_on_the_problems_tab_is_whole_and_moves_nothing_when_it_appears()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            const string tabs = "() => [...document.querySelectorAll('[role=tab]')].slice(-3).map(tab => { const box = tab.getBoundingClientRect(); return [box.left, box.right, box.top, box.bottom]; })";
            var before = await editor.Page.EvaluateAsync<double[][]>(tabs);

            await editor.SetCodeAsync("🏁 🍇\n  😀 nope❗️\n🍉\n");
            await editor.RunButton.ClickAsync();
            await editor.Problems.First.WaitForAsync();
            var badge = editor.Page.Locator("[role=tab] .mud-badge");
            (await badge.InnerTextAsync()).Trim().ShouldBe("1");

            // Nothing moved: Output, Problems and Requests are where they were.
            var after = await editor.Page.EvaluateAsync<double[][]>(tabs);
            after.ShouldBe(before);

            // And the count lies inside its tab, where the tab does not cut it.
            var count = await badge.BoundingBoxAsync();
            var problemsTab = after[1];
            count.ShouldNotBeNull();
            count.X.ShouldBeGreaterThanOrEqualTo((float)problemsTab[0]);
            (count.X + count.Width).ShouldBeLessThanOrEqualTo((float)problemsTab[1]);
            count.Y.ShouldBeGreaterThanOrEqualTo((float)problemsTab[2]);
            (count.Y + count.Height).ShouldBeLessThanOrEqualTo((float)problemsTab[3]);
        }
    }
}
