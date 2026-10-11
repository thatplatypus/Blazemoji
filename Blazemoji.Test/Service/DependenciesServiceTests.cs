using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using Blazemoji.Toolchain.Packmoji;
using NSubstitute;

namespace Blazemoji.Test.Service
{
    public sealed class DependenciesServiceTests : IDisposable
    {
        private const string Digest = "8502cea823dc837cf74985e796e3d5604035095ec715289ad5c10540c73759f8";
        private const string SetId = "5d41402abc4b2a76b9719d911017c592ae2f3c1e7b4f3a1d2c0b9a8f7e6d5c4b";
        private static readonly LockedPackage Words = new("@thatplatypus/hello_words", "0.1.0", Digest);

        private readonly ToolchainServiceFactory _factory = new();

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private HttpClient Client => _factory.CreateClient();

        public void Dispose() => _factory.Dispose();

        private void Answers(DependencyAnswer answer) =>
            _factory.Packages.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>()).Returns(answer);

        private void Archives(ArchiveOutcome outcome) =>
            _factory.Packages.AddArchiveAsync(Arg.Any<LockedPackage>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(outcome);

        private Task<HttpResponseMessage> AskAsync(string body = """{"manifest":"the manifest","lockfile":"the lockfile"}""") =>
            Client.PostAsync(ToolchainRoutes.Dependencies, new StringContent(body, Encoding.UTF8, "application/json"), Cancellation);

        private Task<HttpResponseMessage> PutAsync(LockedPackage package, byte[] bytes) =>
            Client.PutAsync(ToolchainRoutes.DependencyArchive(package), new ByteArrayContent(bytes), Cancellation);

        private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
            return document.RootElement.Clone();
        }

        private static async Task<string> ProblemTitleAsync(HttpResponseMessage response)
        {
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            return (await JsonAsync(response)).GetProperty("title").GetString()!;
        }

        [Fact]
        public async Task The_two_files_reach_the_toolchain_as_they_were_sent()
        {
            Answers(new DependencyAnswer.Ready(SetId, []));

            await AskAsync("""{"manifest":"{ \"a\": 🍇 }","lockfile":"line one\nline two"}""");

            await _factory.Packages.Received(1).PrepareAsync(
                Arg.Is<DependencyRequest>(request => request.Manifest == "{ \"a\": 🍇 }" && request.Lockfile == "line one\nline two"),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Archives_the_service_lacks_are_asked_for_by_name_version_and_digest()
        {
            Answers(new DependencyAnswer.Needed([Words]));

            var response = await AskAsync();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await JsonAsync(response);
            body.GetProperty("state").GetString().ShouldBe("needed");
            var needed = body.GetProperty("needed")[0];
            needed.GetProperty("name").GetString().ShouldBe("@thatplatypus/hello_words");
            needed.GetProperty("version").GetString().ShouldBe("0.1.0");
            needed.GetProperty("sha256").GetString().ShouldBe(Digest);
            body.TryGetProperty("id", out var ignored).ShouldBeFalse();
        }

        [Fact]
        public async Task A_set_that_is_ready_comes_back_with_its_id_and_its_packages()
        {
            Answers(new DependencyAnswer.Ready(SetId, ["hello_greeter", "hello_words"]));

            var body = await JsonAsync(await AskAsync());

            body.GetProperty("state").GetString().ShouldBe("ready");
            body.GetProperty("id").GetString().ShouldBe(SetId);
            body.GetProperty("packages").EnumerateArray().Select(package => package.GetString()).ShouldBe(["hello_greeter", "hello_words"]);
        }

        [Fact]
        public async Task What_is_wrong_comes_back_with_every_part_pmj_gave_it()
        {
            Answers(new DependencyAnswer.Failed(
            [
                new PackageProblem(PackageProblem.Error, "json.syntax", "The file is not valid JSON.", "strict JSON is required", "correct the JSON at this position", "packmoji.json", 3, 14),
            ]));

            var response = await AskAsync();

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await JsonAsync(response);
            body.GetProperty("state").GetString().ShouldBe("failed");
            var problem = body.GetProperty("diagnostics")[0];
            problem.GetProperty("severity").GetString().ShouldBe("error");
            problem.GetProperty("code").GetString().ShouldBe("json.syntax");
            problem.GetProperty("message").GetString().ShouldBe("The file is not valid JSON.");
            problem.GetProperty("reason").GetString().ShouldBe("strict JSON is required");
            problem.GetProperty("fix").GetString().ShouldBe("correct the JSON at this position");
            problem.GetProperty("file").GetString().ShouldBe("packmoji.json");
            problem.GetProperty("line").GetInt32().ShouldBe(3);
            problem.GetProperty("column").GetInt32().ShouldBe(14);
        }

        [Fact]
        public async Task A_service_with_too_much_to_build_says_to_ask_again()
        {
            Answers(new DependencyAnswer.Busy());

            var response = await AskAsync();

            response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
            (await ProblemTitleAsync(response)).ShouldBe("Too many sets of packages are being built. Try again shortly.");
        }

        [Theory]
        [InlineData("""{}""")]
        [InlineData("""{"manifest":"m"}""")]
        [InlineData("""{"lockfile":"l"}""")]
        [InlineData("""{"manifest":"","lockfile":"l"}""")]
        public async Task A_request_without_both_files_is_a_400_and_never_reaches_the_toolchain(string body)
        {
            var response = await AskAsync(body);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ProblemTitleAsync(response)).ShouldBe("A manifest and a lockfile are required.");
            await _factory.Packages.DidNotReceive().PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task An_archive_reaches_the_toolchain_whole_under_the_name_it_was_sent_with()
        {
            byte[]? received = null;
            _factory.Packages.AddArchiveAsync(Arg.Any<LockedPackage>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    using var copy = new MemoryStream();
                    call.Arg<Stream>().CopyTo(copy);
                    received = copy.ToArray();
                    return ArchiveOutcome.Stored;
                });
            var bytes = Enumerable.Range(0, 70_000).Select(index => (byte)index).ToArray();

            var response = await PutAsync(Words, bytes);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            received.ShouldBe(bytes);
            await _factory.Packages.Received(1).AddArchiveAsync(Words, Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(ArchiveOutcome.WrongDigest, HttpStatusCode.BadRequest, "wrong-digest", "The archive is not the bytes its digest names.")]
        [InlineData(ArchiveOutcome.Malformed, HttpStatusCode.BadRequest, "malformed", "The package's name, version or digest is not well formed.")]
        [InlineData(ArchiveOutcome.NotAllowed, HttpStatusCode.BadRequest, "not-allowed", "The package is not of a scope this service builds.")]
        [InlineData(ArchiveOutcome.TooLarge, HttpStatusCode.RequestEntityTooLarge, "too-large", "The archive is too large.")]
        public async Task An_archive_that_is_refused_says_why_in_a_way_a_client_can_read(ArchiveOutcome outcome, HttpStatusCode status, string reason, string title)
        {
            Archives(outcome);

            var response = await PutAsync(Words, [1, 2, 3]);

            response.StatusCode.ShouldBe(status);
            response.Headers.GetValues(ArchiveReasons.Header).ShouldHaveSingleItem().ShouldBe(reason);
            (await ProblemTitleAsync(response)).ShouldBe(title);
        }

        [Fact]
        public async Task An_archive_that_could_not_be_kept_is_a_500_that_gives_nothing_away()
        {
            Archives(ArchiveOutcome.Failed);

            var response = await PutAsync(Words, [1, 2, 3]);

            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            response.Headers.GetValues(ArchiveReasons.Header).ShouldHaveSingleItem().ShouldBe("failed");
            (await ProblemTitleAsync(response)).ShouldBe("The archive could not be kept.");
        }

        [Fact]
        public async Task An_archive_over_the_limit_is_refused_before_the_toolchain_sees_it()
        {
            _factory.MaxArchiveBytes = 16;

            var response = await PutAsync(Words, new byte[17]);

            response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            response.Headers.GetValues(ArchiveReasons.Header).ShouldHaveSingleItem().ShouldBe("too-large");
            await _factory.Packages.DidNotReceive().AddArchiveAsync(Arg.Any<LockedPackage>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task An_archive_exactly_at_the_limit_is_taken()
        {
            _factory.MaxArchiveBytes = 16;
            Archives(ArchiveOutcome.Stored);

            var response = await PutAsync(Words, new byte[16]);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task An_archive_with_no_name_or_version_is_for_the_toolchain_to_refuse()
        {
            Archives(ArchiveOutcome.Malformed);

            var response = await Client.PutAsync($"/dependencies/archives/{Digest}", new ByteArrayContent([1]), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            await _factory.Packages.Received(1).AddArchiveAsync(new LockedPackage(string.Empty, string.Empty, Digest), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_compile_names_its_set_to_the_toolchain()
        {
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [], "build-1"));

            var response = await Client.PostAsync(ToolchainRoutes.Compile,
                new StringContent($$"""{"files":{"main.🍇":"x"},"entry":"main.🍇","dependencies":"{{SetId}}"}""", Encoding.UTF8, "application/json"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            await _factory.Toolchain.Received(1).CompileAsync(Arg.Is<CompileRequest>(request => request.Dependencies == SetId), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_compile_with_no_set_names_none()
        {
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(true, [], "build-1"));

            await Client.PostAsync(ToolchainRoutes.Compile,
                new StringContent("""{"files":{"main.🍇":"x"},"entry":"main.🍇"}""", Encoding.UTF8, "application/json"), Cancellation);

            await _factory.Toolchain.Received(1).CompileAsync(Arg.Is<CompileRequest>(request => request.Dependencies == null), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_compile_against_a_set_the_service_does_not_hold_is_a_409()
        {
            _factory.Toolchain.CompileAsync(Arg.Any<CompileRequest>(), Arg.Any<CancellationToken>())
                .Returns(new CompileResult(false, [], null, ReachedCompiler: false, DependenciesMissing: true));

            var response = await Client.PostAsync(ToolchainRoutes.Compile,
                new StringContent($$"""{"files":{"main.🍇":"x"},"entry":"main.🍇","dependencies":"{{SetId}}"}""", Encoding.UTF8, "application/json"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await ProblemTitleAsync(response)).ShouldBe("The project's packages are not built here.");
        }

        [Fact]
        public async Task The_report_of_a_package_in_a_set_is_served_as_json()
        {
            _factory.Packages.GetDocumentationAsync(SetId, "hello_words", Arg.Any<CancellationToken>()).Returns("""{"types":[{"name":"📖"}]}""");

            var response = await Client.GetAsync(ToolchainRoutes.DependencyDocumentation(SetId, "hello_words"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
            (await response.Content.ReadAsStringAsync(Cancellation)).ShouldBe("""{"types":[{"name":"📖"}]}""");
        }

        [Fact]
        public async Task A_report_that_is_not_there_is_a_404()
        {
            _factory.Packages.GetDocumentationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);

            var response = await Client.GetAsync(ToolchainRoutes.DependencyDocumentation(SetId, "nope"), Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ProblemTitleAsync(response)).ShouldBe("No such package.");
        }

        [Fact]
        public void The_route_to_an_archive_carries_its_name_and_version_safely()
        {
            ToolchainRoutes.DependencyArchive(Words)
                .ShouldBe($"/dependencies/archives/{Digest}?name=%40thatplatypus%2Fhello_words&version=0.1.0");
        }
    }
}
