namespace Blazemoji.E2E
{
    public class RunnerFlowsTests(BrowserFixture browser)
    {
        private const string SlowerTwoLines =
            "🏁 🍇\n  😀 🔤one🔤❗️\n  ⏲🐇🧵 4000000❗️\n  😀 🔤two🔤❗️\n🍉\n";

        [Fact]
        public async Task Running_a_sample_shows_its_output_and_exit_status()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.LoadSampleAsync("HelloWorld");
            await editor.RunButton.ClickAsync();

            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["Hello World!"]);
            (await editor.RunButton.IsEnabledAsync()).ShouldBeTrue();
            (await editor.StopButton.IsDisabledAsync()).ShouldBeTrue();
            editor.ConsoleErrors.ShouldBeEmpty();
            await editor.ScreenshotAsync("p1-01-sample-run");
        }

        [Fact]
        public async Task Output_appears_before_the_program_ends()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.RunAsync(SlowerTwoLines);
            await editor.OutputLines.GetByText("one").WaitForAsync();

            (await editor.RunStatus.InnerTextAsync()).ShouldBe("Running");
            (await editor.StopButton.IsEnabledAsync()).ShouldBeTrue();
            (await editor.RunButton.IsDisabledAsync()).ShouldBeTrue();
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["one"]);
            await editor.ScreenshotAsync("p1-02-output-while-running");

            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["one", "two"]);
        }

        [Fact]
        public async Task Stop_ends_a_program_that_would_run_forever()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.RunAsync(Programs.Forever);
            await editor.WaitForStatusAsync("Running");
            await editor.StopButton.ClickAsync();

            await editor.WaitForStatusAsync("Stopped after");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["Stopped."]);
            (await editor.RunButton.IsEnabledAsync()).ShouldBeTrue();
            await editor.ScreenshotAsync("p1-03-stopped");
        }

        [Fact]
        public async Task A_flooding_program_is_cut_off_and_the_page_stays_usable()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.LoadSampleAsync("InfiniteLoop");
            await editor.RunButton.ClickAsync();

            await editor.WaitForStatusAsync("Output limit reached");
            (await editor.DroppedLinesNote.InnerTextAsync()).ShouldStartWith("Showing the last 5,000 of ");
            (await editor.OutputLines.Last.InnerTextAsync()).ShouldBe("Stopped after reaching the output limit.");
            await editor.ScreenshotAsync("p1-04-output-limit");

            await editor.RunAsync(Programs.Hello);
            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["Hello World!"]);
        }

        [Fact]
        public async Task More_than_a_megabyte_of_output_completes()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.RunAsync(Programs.OneMegabyte);

            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.DroppedLinesNote.InnerTextAsync()).ShouldBe("Showing the last 5,000 of 20,000 lines.");
            await editor.ScreenshotAsync("p1-05-one-megabyte");
        }

        [Fact]
        public async Task A_compile_error_shows_a_marker_and_a_problem_that_leads_to_it()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.RunAsync(Programs.UndefinedVariable);

            var problem = editor.Problems;
            await problem.WaitForAsync();
            (await problem.InnerTextAsync()).ShouldContain("Variable \"nope\" not defined.");
            (await problem.InnerTextAsync()).ShouldContain("2:5");
            await editor.WaitForMarkerCountAsync(1);
            (await editor.MarkersAsync()).ShouldBe(
                [new EditorMarker(2, 6, 2, 10, "Variable \"nope\" not defined.", 8)]);
            await editor.Page.Locator(".monaco-editor .squiggly-error").First.WaitForAsync();
            (await editor.StopButton.IsDisabledAsync()).ShouldBeTrue();
            await editor.ScreenshotAsync("p1-06-compile-error");

            await problem.ClickAsync();

            await editor.WaitForCursorAsync(2, 6);
        }

        [Fact]
        public async Task Fixing_the_error_clears_the_marker_and_runs()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            await editor.RunAsync(Programs.UndefinedVariable);
            await editor.Problems.WaitForAsync();
            await editor.WaitForMarkerCountAsync(1);

            await editor.RunAsync(Programs.Hello);

            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["Hello World!"]);
            await editor.WaitForMarkerCountAsync(0);
        }

        [Fact]
        public async Task A_build_with_a_compiler_warning_runs_and_lists_the_warning()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.RunAsync(Programs.RttiWarning);

            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["stored"]);
            (await editor.MarkersAsync()).ShouldBeEmpty();

            await editor.OpenTabAsync("Problems");
            (await editor.Problems.InnerTextAsync()).ShouldContain("Run-time type information");
            await editor.ScreenshotAsync("p1-07-warning-build");
        }

        [Fact]
        public async Task A_panic_shows_its_message_and_exit_code()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.RunAsync(Programs.Crashes);

            await editor.WaitForStatusAsync("Exited with code 134");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(
            [
                "before the crash",
                "🤯 Program panicked: Unwrapped an optional that contained no value. (main.🍇:3:5)",
            ]);
        }

        [Fact]
        public async Task Two_sessions_running_at_once_keep_their_own_output()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var first = await EditorPage.OpenAsync(browser);
            await using var second = await EditorPage.OpenAsync(browser);
            await first.SetCodeAsync(Programs.PrintsMarker("first"));
            await second.SetCodeAsync(Programs.PrintsMarker("second"));

            await Task.WhenAll(first.RunButton.ClickAsync(), second.RunButton.ClickAsync());
            await Task.WhenAll(first.WaitForStatusAsync("Exited with code 0"), second.WaitForStatusAsync("Exited with code 0"));

            (await first.OutputLines.AllInnerTextsAsync()).ShouldBe(["marker-first"]);
            (await second.OutputLines.AllInnerTextsAsync()).ShouldBe(["marker-second"]);
        }

        // 👄 prints without a newline, as a program does when it asks something and waits.
        private const string AsksForAName =
            "🏁 🍇\n  👄 🔤What is your name? 🔤❗️\n  🆕🔡▶️👂🏼❗️ ➡️ name\n  😀 🔤Hello, 🧲name🧲!🔤❗️\n🍉\n";

        private const string ReadsTwoLines =
            "🏁 🍇\n  🆕🔡▶️👂🏼❗️ ➡️ one\n  🆕🔡▶️👂🏼❗️ ➡️ two\n  😀 🔤Read [🧲one🧲] and [🧲two🧲]🔤❗️\n🍉\n";

        [Fact]
        public async Task A_program_that_asks_a_question_is_seen_asking_and_is_given_the_line_that_is_typed()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var input = editor.Page.GetByTestId("program-input");
            (await input.IsDisabledAsync()).ShouldBeTrue();

            await editor.RunAsync(AsksForAName);

            // The question has no newline after it, and has to be there before it can be answered.
            await Assertions.Expect(editor.Page.GetByTestId("output-unfinished")).ToHaveTextAsync("What is your name?");
            (await editor.RunStatus.InnerTextAsync()).ShouldBe("Running");
            await editor.ScreenshotAsync("p6-01-a-program-asking");

            await input.FillAsync("Tom 🍇");
            await input.PressAsync("Enter");

            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["What is your name? Tom 🍇", "Hello, Tom 🍇!"]);
            (await input.InputValueAsync()).ShouldBeEmpty();
            (await input.IsDisabledAsync()).ShouldBeTrue();
            editor.ConsoleErrors.ShouldBeEmpty();
            await editor.ScreenshotAsync("p6-02-answered");
        }

        [Fact]
        public async Task Lines_typed_one_after_another_reach_the_program_in_order_and_an_empty_one_counts()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var input = editor.Page.GetByTestId("program-input");

            await editor.RunAsync(ReadsTwoLines);
            await Assertions.Expect(input).ToBeEnabledAsync();
            await input.FillAsync("first");
            await input.PressAsync("Enter");
            await input.PressAsync("Enter");

            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["first", string.Empty, "Read [first] and []"]);
        }

        [Fact]
        public async Task Ending_the_input_lets_a_program_that_is_waiting_to_read_carry_on()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var end = editor.Page.GetByTestId("end-input");

            await editor.RunAsync(ReadsTwoLines);
            await Assertions.Expect(end).ToBeEnabledAsync();
            await end.ClickAsync();

            // With no more to read, each read comes back with nothing and the program goes on to its end.
            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["Read [] and []"]);
            (await end.IsDisabledAsync()).ShouldBeTrue();
        }
    }
}
