using System.Net;

namespace Blazemoji.Toolchain.ContractTests
{
    /// <summary>
    /// A project's packages, from the outside: the two files go in, the archives the service
    /// lacks are handed over, and a program that imports them is compiled, linked and run.
    /// The service is one for the whole run, so a test cannot count on being the first to ask.
    /// </summary>
    public class DependenciesContractTests
    {
        private const string WordsDigest = "8502cea823dc837cf74985e796e3d5604035095ec715289ad5c10540c73759f8";
        private const string GreeterDigest = "9da45105dcb3746b7e01d8bd0e702ef3100c90be1be7aa650ea0a9367b8fae3a";

        private const string ImportsGreeter = """
            📦 hello_greeter 🏠

            🏁 🍇
              😀 💬🐇🙋 🔤the contract🔤❗️❗️
              😀 🔡 🎲🐇🙋❗️ 10❗️❗️
            🍉

            """;

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Packages", name);

        private static string Manifest => File.ReadAllText(Asset("packmoji.json"));

        private static string Lockfile => File.ReadAllText(Asset("packmoji.lock"));

        private static Dictionary<string, byte[]> Archives => new()
        {
            [WordsDigest] = File.ReadAllBytes(Asset("hello_words-0.1.0.pmj.tar.gz")),
            [GreeterDigest] = File.ReadAllBytes(Asset("hello_greeter-0.1.0.pmj.tar.gz")),
        };

        private static async Task<string> ReadySetAsync()
        {
            var answer = await ToolchainService.PrepareAsync(Manifest, Lockfile, Archives, Cancellation);
            answer.GetProperty("state").GetString().ShouldBe("ready", answer.GetRawText());
            return answer.GetProperty("id").GetString().ShouldNotBeNull();
        }

        [Fact]
        public async Task A_set_is_asked_for_handed_its_archives_and_made_ready()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            var answer = await ToolchainService.PrepareAsync(Manifest, Lockfile, Archives, Cancellation);

            answer.GetProperty("state").GetString().ShouldBe("ready", answer.GetRawText());
            answer.GetProperty("id").GetString().ShouldNotBeNullOrWhiteSpace();
            answer.GetProperty("packages").EnumerateArray().Select(package => package.GetString()).ShouldBe(["hello_greeter", "hello_words"]);
        }

        [Fact]
        public async Task The_same_packages_asked_for_again_are_the_same_set()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            var first = await ReadySetAsync();
            var second = await ReadySetAsync();

            second.ShouldBe(first);
        }

        [Fact]
        public async Task A_program_that_imports_a_package_of_its_set_compiles_links_and_runs()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var set = await ReadySetAsync();

            using var response = await ToolchainService.PostCompileAsync(
                new { files = new Dictionary<string, string> { ["main.🍇"] = ImportsGreeter }, entry = "main.🍇", dependencies = set }, Cancellation);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var build = await ToolchainService.ReadJsonAsync(response, Cancellation);
            build.GetProperty("ok").GetBoolean().ShouldBeTrue(build.GetRawText());

            var runId = await ToolchainService.StartRunAsync(build.GetProperty("buildId").GetString()!, Cancellation);
            var events = await ToolchainService.ReadEventsAsync(runId, Cancellation);

            ToolchainService.Text(events, "stdout").ShouldBe("Hello, the contract!\n42\n");
        }

        [Fact]
        public async Task The_same_program_without_its_set_does_not_compile()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            var result = await ToolchainService.CompileAsync(ImportsGreeter, Cancellation);

            result.GetProperty("ok").GetBoolean().ShouldBeFalse();
        }

        [Fact]
        public async Task A_compile_that_names_a_set_the_service_does_not_hold_is_a_409()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.PostCompileAsync(
                new { files = new Dictionary<string, string> { ["main.🍇"] = Programs.Hello }, entry = "main.🍇", dependencies = new string('0', 64) }, Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.Conflict, Cancellation);
        }

        [Fact]
        public async Task The_report_of_a_package_in_a_set_is_json_and_one_that_is_not_there_is_a_404()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);
            var set = await ReadySetAsync();

            using var found = await ToolchainService.Http.GetAsync($"dependencies/{set}/packages/hello_greeter/documentation.json", Cancellation);
            using var missing = await ToolchainService.Http.GetAsync($"dependencies/{set}/packages/s/documentation.json", Cancellation);
            using var noSuchSet = await ToolchainService.Http.GetAsync($"dependencies/{new string('0', 64)}/packages/hello_greeter/documentation.json", Cancellation);

            found.StatusCode.ShouldBe(HttpStatusCode.OK);
            ToolchainService.MediaType(found).ShouldBe("application/json");
            (await ToolchainService.ReadJsonAsync(found, Cancellation)).GetProperty("types").GetArrayLength().ShouldBeGreaterThan(0);
            await ToolchainService.ShouldBeProblemAsync(missing, HttpStatusCode.NotFound, Cancellation);
            await ToolchainService.ShouldBeProblemAsync(noSuchSet, HttpStatusCode.NotFound, Cancellation);
        }

        [Fact]
        public async Task Bytes_that_are_not_the_digest_they_came_under_are_a_400_that_says_so()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.PutArchiveAsync("@thatplatypus/hello_words", "0.1.0", GreeterDigest, Archives[WordsDigest], Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, Cancellation);
            response.Headers.GetValues("X-Toolchain-Archive").ShouldHaveSingleItem().ShouldBe("wrong-digest");
        }

        [Theory]
        [InlineData("hello_words", "0.1.0")]
        [InlineData("@thatplatypus/../../etc", "0.1.0")]
        [InlineData("@thatplatypus/hello_words", "../0.1.0")]
        public async Task An_archive_under_a_name_or_a_version_that_is_not_one_is_a_400_that_says_so(string name, string version)
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.PutArchiveAsync(name, version, WordsDigest, Archives[WordsDigest], Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, Cancellation);
            response.Headers.GetValues("X-Toolchain-Archive").ShouldHaveSingleItem().ShouldBe("malformed");
        }

        [Theory]
        [InlineData("""{}""")]
        [InlineData("""{"manifest":"{}"}""")]
        [InlineData("""{"lockfile":"{}"}""")]
        public async Task A_request_for_a_set_without_both_files_is_a_400(string body)
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.Http.PostAsync("dependencies", new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Cancellation);

            await ToolchainService.ShouldBeProblemAsync(response, HttpStatusCode.BadRequest, Cancellation);
        }

        [Fact]
        public async Task Files_that_are_not_a_manifest_and_a_lockfile_are_a_200_that_says_what_is_wrong()
        {
            Assert.SkipWhen(ToolchainService.BaseUrl is null, ToolchainService.SkipReason);

            using var response = await ToolchainService.PostDependenciesAsync("{ not json", Lockfile, Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var answer = await ToolchainService.ReadJsonAsync(response, Cancellation);
            answer.GetProperty("state").GetString().ShouldBe("failed");
            var problem = answer.GetProperty("diagnostics")[0];
            problem.GetProperty("severity").GetString().ShouldBe("error");
            problem.GetProperty("code").GetString().ShouldNotBeNullOrWhiteSpace();
            problem.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
            problem.GetProperty("file").GetString().ShouldBe("packmoji.json");
            problem.GetProperty("line").GetInt32().ShouldBeGreaterThan(0);
        }
    }
}
