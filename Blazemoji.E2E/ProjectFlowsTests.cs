namespace Blazemoji.E2E
{
    /// <summary>
    /// Projects with several files, and programs that keep running and answer HTTP requests.
    /// </summary>
    public class ProjectFlowsTests(BrowserFixture browser)
    {
        private const string BrokenGreeter =
            "🐇 🙋 🍇\n\t🆕 🍇🍉\n\n\t❗️ 👋 name 🔡 🍇\n\t\t😀 nope❗️\n\t🍉\n🍉\n";

        [Fact]
        public async Task A_project_made_from_the_two_file_template_runs_through_both_files()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.CreateProjectAsync("Greeter", "two-files");
            await editor.WaitForOpenFileAsync("Greeter", "main.🍇");
            await editor.WaitForFilesAsync("greeter.🍇", "main.🍇");
            await editor.RunButton.ClickAsync();

            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["Hello, World!"]);
            editor.ConsoleErrors.ShouldBeEmpty();
            await editor.ScreenshotAsync("p3-01-two-file-project");
        }

        [Fact]
        public async Task Each_file_keeps_its_own_text_when_switching_between_them()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.CreateProjectAsync("Greeter", "two-files");
            await editor.WaitForOpenFileAsync("Greeter", "main.🍇");
            var main = await editor.GetCodeAsync();

            await editor.OpenFileAsync("Greeter", "greeter.🍇");
            var greeter = await editor.GetCodeAsync();
            await editor.SetCodeAsync(greeter.Replace("Hello", "Howdy"));
            await editor.OpenFileAsync("Greeter", "main.🍇");

            (await editor.GetCodeAsync()).ShouldBe(main);
            main.ShouldContain("📜");
            greeter.ShouldContain("🐇 🙋");
            await editor.RunButton.ClickAsync();
            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["Howdy, World!"]);
        }

        [Fact]
        public async Task A_project_and_its_edits_are_still_there_after_the_page_is_reloaded()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.CreateProjectAsync("Greeter", "two-files");
            await editor.OpenFileAsync("Greeter", "greeter.🍇");
            await editor.SetCodeAsync((await editor.GetCodeAsync()).Replace("Hello", "Still here"));

            // Typing is kept once it pauses. Opening another file hands it over at once.
            await editor.OpenFileAsync("Greeter", "main.🍇");
            await editor.Page.ReloadAsync();
            await editor.WaitForOpenFileAsync("Greeter", "main.🍇");

            await editor.WaitForFilesAsync("greeter.🍇", "main.🍇");
            await editor.RunButton.ClickAsync();
            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["Still here, World!"]);
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        /// <summary>Comment lines that are nearly all emoji, as Emojicode is.</summary>
        private static string EmojiText(int emoji) =>
            string.Concat(Enumerable.Range(0, emoji / 10).Select(line => $"💭 🍇🍉😀🔤🏁📦🐇❗️ {line}\n"));

        [Theory]
        [InlineData(4_500, "a project a little larger than the Todo sample")]
        [InlineData(15_000, "one file larger than Grapevine's largest")]
        public async Task A_project_with_a_lot_of_emoji_in_it_still_opens_after_a_reload(int emoji, string what)
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.CreateProjectAsync("Greeter", "two-files");
            await editor.OpenFileAsync("Greeter", "greeter.🍇");
            var text = EmojiText(emoji);
            await editor.SetCodeAsync(text);
            await editor.OpenFileAsync("Greeter", "main.🍇");

            await editor.Page.ReloadAsync();
            await editor.WaitForOpenFileAsync("Greeter", "main.🍇");
            await editor.WaitForFilesAsync("greeter.🍇", "main.🍇");
            await editor.OpenFileAsync("Greeter", "greeter.🍇");

            (await editor.GetCodeAsync()).ShouldBe(text, what);
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_problem_in_another_file_names_it_and_leads_to_it()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.CreateProjectAsync("Greeter", "two-files");
            await editor.OpenFileAsync("Greeter", "greeter.🍇");
            await editor.SetCodeAsync(BrokenGreeter);
            await editor.OpenFileAsync("Greeter", "main.🍇");

            await editor.RunButton.ClickAsync();

            // A failed build brings the Problems tab forward, so that is where to look.
            var problem = editor.Problems.First;
            await problem.WaitForAsync();
            (await problem.InnerTextAsync()).ShouldContain("Variable \"nope\" not defined.");
            // The panel shows the compiler's position, which counts 😀 as one character. The
            // editor counts it as two, which is why the cursor lands on column 6 below.
            (await problem.GetByTestId("problem-position").InnerTextAsync()).ShouldBe("greeter.🍇 5:5");
            await editor.ScreenshotAsync("p3-02-problem-in-another-file");

            await editor.ShowsAnotherFileAsync(() => problem.ClickAsync(), "Greeter", "greeter.🍇");

            await editor.WaitForCursorAsync(5, 6);
            await editor.WaitForMarkerCountAsync(1);
            (await editor.MarkersAsync())[0].StartLineNumber.ShouldBe(5);
            await editor.ScreenshotAsync("p3-03-opened-at-the-problem");
        }

        [Fact]
        public async Task A_file_can_be_added_renamed_made_the_entry_and_deleted()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.CreateProjectAsync("Files", "hello-world");
            await editor.WaitForOpenFileAsync("Files", "main.🍇");

            await editor.ShowsAnotherFileAsync(
                async () =>
                {
                    await editor.Page.GetByTestId("new-file").ClickAsync();
                    await editor.AnswerPromptAsync("lib/extra");
                },
                "Files",
                "lib/extra.🍇");
            await editor.WaitForFilesAsync("lib/extra.🍇", "main.🍇");

            await editor.ChooseFileActionAsync("lib/extra.🍇", "rename-file");
            await editor.AnswerPromptAsync("main.🍇");
            await editor.Dialog.GetByText("A file with that name already exists.").WaitForAsync();
            await editor.ScreenshotAsync("p3-04-rename-refused");
            await editor.ShowsAnotherFileAsync(() => editor.AnswerPromptAsync("lib/second.🍇"), "Files", "lib/second.🍇");

            await editor.SetCodeAsync("🏁 🍇\n\t😀 🔤from the second file🔤❗️\n🍉\n");
            await editor.ChooseFileActionAsync("lib/second.🍇", "set-entry");
            await editor.File("lib/second.🍇").GetByTestId("entry-flag").WaitForAsync();
            (await editor.EntryFlag.CountAsync()).ShouldBe(1);
            await editor.RunButton.ClickAsync();
            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["from the second file"]);

            await editor.ShowsAnotherFileAsync(
                async () =>
                {
                    await editor.ChooseFileActionAsync("lib/second.🍇", "delete-file");
                    await editor.Dialog.GetByText("Delete", new LocatorGetByTextOptions { Exact = true }).Last.ClickAsync();
                },
                "Files",
                "main.🍇");
            await editor.WaitForFilesAsync("main.🍇");
            await editor.File("main.🍇").GetByTestId("entry-flag").WaitForAsync();
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task Sending_a_request_with_no_server_running_is_not_possible_and_says_why()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.OpenTabAsync("Requests");

            await editor.Page.GetByTestId("no-server").WaitForAsync();
            (await editor.Page.GetByTestId("request-send").IsDisabledAsync()).ShouldBeTrue();
            await editor.ScreenshotAsync("p3-05-requests-without-a-server");
        }

        [Fact]
        public async Task The_grapevine_todo_sample_answers_create_read_update_and_delete_from_the_request_panel()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            Assert.SkipUnless(await editor.HasTemplateAsync("grapevine-todo"), "The Grapevine package is not built into this stack. Run scripts/build-grapevine.sh.");

            await editor.CreateProjectAsync("Todo API", "grapevine-todo");
            await editor.WaitForOpenFileAsync("Todo API", "main.🍇");
            await editor.ScreenshotAsync("p3-06-grapevine-project");
            await editor.RunButton.ClickAsync();

            await editor.OutputLines.GetByText("Grapevine listening").WaitForAsync();
            (await editor.RunStatus.InnerTextAsync()).ShouldBe("Running");

            await editor.SendRequestAsync("POST", "/todos", "{\"title\":\"Buy grapes\"}");
            (await editor.ResponseStatus.InnerTextAsync()).ShouldBe("201 Created");
            (await editor.ResponseBody.InnerTextAsync()).ShouldContain("\"title\": \"Buy grapes\"");
            (await editor.ResponseBody.InnerTextAsync()).ShouldContain("\"id\": 1");
            await editor.ScreenshotAsync("p3-07-todo-created");

            await editor.SendRequestAsync("GET", "/todos");
            (await editor.ResponseStatus.InnerTextAsync()).ShouldBe("200 OK");
            (await editor.ResponseBody.InnerTextAsync()).ShouldContain("Buy grapes");

            await editor.SendRequestAsync("PUT", "/todos/1", "{\"title\":\"Buy more grapes\",\"done\":true}");
            (await editor.ResponseStatus.InnerTextAsync()).ShouldBe("200 OK");
            (await editor.ResponseBody.InnerTextAsync()).ShouldContain("\"done\": true");

            await editor.SendRequestAsync("GET", "/todos/1");
            (await editor.ResponseBody.InnerTextAsync()).ShouldContain("Buy more grapes");
            await editor.ScreenshotAsync("p3-08-todo-updated");

            await editor.SendRequestAsync("DELETE", "/todos/1");
            (await editor.ResponseStatus.InnerTextAsync()).ShouldBe("204 No Content");

            await editor.SendRequestAsync("GET", "/todos/1");
            (await editor.ResponseStatus.InnerTextAsync()).ShouldBe("404 Not Found");
            await editor.ScreenshotAsync("p3-09-todo-gone");

            await editor.OpenTabAsync("Output");
            await editor.OutputLines.GetByText("DELETE /todos/1").WaitForAsync();
            await editor.ScreenshotAsync("p3-10-server-log");

            await editor.StopButton.ClickAsync();
            await editor.WaitForStatusAsync("Stopped after");
            await editor.OpenTabAsync("Requests");
            await editor.Page.GetByTestId("no-server").WaitForAsync();
            editor.ConsoleErrors.ShouldBeEmpty();
        }
    }
}
