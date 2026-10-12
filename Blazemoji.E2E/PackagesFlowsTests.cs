namespace Blazemoji.E2E
{
    /// <summary>
    /// A project's packages, from a typed manifest to a running program, on the website as
    /// docker compose runs it: the web app fetches from GitHub, and the toolchain, which has
    /// no way out, builds what it is handed.
    /// </summary>
    public class PackagesFlowsTests(BrowserFixture browser)
    {
        private const string GrapevineRelease = "https://github.com/thatplatypus/grapevine/releases/download/grapevine-v0.1.0/grapevine-0.1.0.pmj.tar.gz";

        // Three packages are compiled the first time, one of them large. Under emulation that
        // has taken a minute and a half on a busy machine, and the toolchain allows five.
        private const float BuildPatience = 600_000;

        private const string DependsOnGrapevine = """
            {
              "package": {
                "name": "@you/app",
                "version": "0.1.0",
                "kind": "app",
                "emojicode": ">=1.0.0-beta.2"
              },
              "dependencies": {
                "@thatplatypus/grapevine": "0.1.0"
              },
              "build": {
                "entry": "main.🍇"
              }
            }
            """;

        private const string ImportsGrapevine = """
            📦 grapevine 🏠

            🏁 🍇
              😀 🔤built against a locked package🔤❗️
            🍉

            """;

        private static async Task<bool> GitHubCanBeReachedAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                using var request = new HttpRequestMessage(HttpMethod.Head, GrapevineRelease);
                using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                return false;
            }
        }

        private static string NewName() => "Packages " + Guid.NewGuid().ToString("N")[..8];

        [Fact]
        [Trait("Requires", "GitHub")]
        public async Task A_typed_manifest_is_resolved_on_Run_its_lockfile_appears_and_the_program_runs_against_the_package()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            Assert.SkipUnless(await GitHubCanBeReachedAsync(), "GitHub could not be reached, and this test fetches Grapevine's release from it.");
            await using var editor = await EditorPage.OpenAsync(browser);
            var project = NewName();
            await editor.CreateProjectAsync(project, "hello-world");
            await editor.AddFileAsync(project, "packmoji.json");
            await editor.SetCodeAsync(DependsOnGrapevine);
            await editor.OpenFileAsync(project, "main.🍇");
            await editor.SetCodeAsync(ImportsGrapevine);

            await editor.RunButton.ClickAsync();

            await editor.RunStatus.GetByText("Exited with code 0").WaitForAsync(new LocatorWaitForOptions { Timeout = BuildPatience });
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["built against a locked package"]);
            await editor.OpenTabAsync("Files");
            await editor.File("packmoji.lock").WaitForAsync();
            await editor.OpenFileAsync(project, "packmoji.lock");
            var lockfile = await editor.GetCodeAsync();
            lockfile.ShouldContain("\"@thatplatypus/grapevine\"");
            lockfile.ShouldContain("\"@thatplatypus/crypto\"");
            lockfile.ShouldContain("grapevine-0.1.0.pmj.tar.gz");

            // The set is built now, and the lockfile answers the manifest: a second run asks
            // nothing of GitHub and compiles nothing but the program.
            await editor.OpenFileAsync(project, "main.🍇");
            await editor.SetCodeAsync(ImportsGrapevine.Replace("built against a locked package", "and again, with nothing to fetch"));
            await editor.RunButton.ClickAsync();
            await editor.OutputLines.GetByText("and again, with nothing to fetch").WaitForAsync();
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_package_outside_the_websites_scopes_is_refused_by_name_and_no_lockfile_is_written()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);
            var project = NewName();
            await editor.CreateProjectAsync(project, "hello-world");
            await editor.AddFileAsync(project, "packmoji.json");
            await editor.SetCodeAsync(DependsOnGrapevine.Replace("@thatplatypus/grapevine", "@someone/thing"));

            await editor.RunButton.ClickAsync();

            await editor.PackageProblems.First.WaitForAsync();
            var problems = string.Join("\n", await editor.PackageProblems.AllInnerTextsAsync());
            problems.ShouldContain("@someone/thing");
            problems.ShouldContain("Why:");
            await editor.OpenTabAsync("Files");
            (await editor.FilePathsAsync()).ShouldNotContain("packmoji.lock");
            editor.ConsoleErrors.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_project_with_no_manifest_runs_as_it_always_has_and_has_no_package_problem()
        {
            Assert.SkipWhen(BrowserFixture.BaseUrl is null, BrowserFixture.SkipReason);
            await using var editor = await EditorPage.OpenAsync(browser);

            await editor.RunAsync("🏁 🍇\n  😀 🔤no packages here🔤❗️\n🍉\n");

            await editor.WaitForStatusAsync("Exited with code 0");
            (await editor.OutputLines.AllInnerTextsAsync()).ShouldBe(["no packages here"]);
            await editor.OpenTabAsync("Problems");
            (await editor.PackageProblems.CountAsync()).ShouldBe(0);
        }
    }
}
