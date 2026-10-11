using System.Net;
using System.Text;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Blazemoji.Test.Service
{
    /// <summary>
    /// The client talking to the real service hosted in memory, with a substitute behind it.
    /// </summary>
    public sealed class HttpToolchainPackagesTests : IDisposable
    {
        private const string Digest = "8502cea823dc837cf74985e796e3d5604035095ec715289ad5c10540c73759f8";
        private const string SetId = "5d41402abc4b2a76b9719d911017c592ae2f3c1e7b4f3a1d2c0b9a8f7e6d5c4b";
        private static readonly LockedPackage Words = new("@thatplatypus/hello_words", "0.1.0", Digest);
        private static readonly DependencyRequest Request = new("the manifest", "the lockfile");

        private readonly ToolchainServiceFactory _factory = new();

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        public void Dispose() => _factory.Dispose();

        private HttpToolchainPackages Client() => new(_factory.CreateClient(), NullLogger<HttpToolchainPackages>.Instance);

        private static HttpToolchainPackages ClientOver(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer, TimeSpan? timeout = null) =>
            new(new HttpClient(new Scripted(answer)) { BaseAddress = new Uri("http://toolchain.test/"), Timeout = timeout ?? TimeSpan.FromSeconds(100) },
                NullLogger<HttpToolchainPackages>.Instance);

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        private void Answers(DependencyAnswer answer) =>
            _factory.Packages.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>()).Returns(answer);

        [Fact]
        public async Task The_two_files_go_to_the_service_and_the_archives_it_needs_come_back()
        {
            Answers(new DependencyAnswer.Needed([Words]));

            var answer = await Client().PrepareAsync(Request, Cancellation);

            answer.ShouldBeOfType<DependencyAnswer.Needed>().Archives.ShouldBe([Words]);
            await _factory.Packages.Received(1).PrepareAsync(Request, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_set_that_is_ready_comes_back_with_its_id_and_its_packages()
        {
            Answers(new DependencyAnswer.Ready(SetId, ["hello_greeter", "hello_words"]));

            var ready = (await Client().PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Ready>();

            ready.Id.ShouldBe(SetId);
            ready.Packages.ShouldBe(["hello_greeter", "hello_words"]);
        }

        [Fact]
        public async Task What_is_wrong_comes_back_whole_with_its_place()
        {
            var problem = new PackageProblem(PackageProblem.Error, "json.syntax", "The file is not valid JSON.", "strict JSON is required", "correct it", "packmoji.json", 3, 14);
            var warning = new PackageProblem(PackageProblem.Warning, "resolve.yanked-locked", "A version was yanked.", "its author withdrew it", "update it");
            Answers(new DependencyAnswer.Failed([problem, warning]));

            var failed = (await Client().PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Failed>();

            failed.Problems.ShouldBe([problem, warning]);
        }

        [Fact]
        public async Task A_service_with_too_much_to_build_is_busy()
        {
            Answers(new DependencyAnswer.Busy());

            (await Client().PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Busy>();
        }

        [Fact]
        public async Task A_service_that_does_not_answer_in_time_is_busy_and_not_broken()
        {
            var client = ClientOver(
                async (_, cancellation) =>
                {
                    await Task.Delay(Timeout.Infinite, cancellation);
                    return Json("{}");
                },
                timeout: TimeSpan.FromMilliseconds(200));

            (await client.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Busy>();
        }

        [Fact]
        public async Task A_caller_who_stops_waiting_is_told_so_and_is_not_given_an_answer()
        {
            var client = ClientOver(async (_, cancellation) =>
            {
                await Task.Delay(Timeout.Infinite, cancellation);
                return Json("{}");
            });
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            stop.CancelAfter(TimeSpan.FromMilliseconds(200));

            await Should.ThrowAsync<OperationCanceledException>(() => client.PrepareAsync(Request, stop.Token));
        }

        [Fact]
        public async Task A_service_that_cannot_be_reached_is_unavailable()
        {
            var client = ClientOver((_, _) => throw new HttpRequestException("connection refused to 10.0.0.7"));

            (await client.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Unavailable>();
            (await client.AddArchiveAsync(Words, new MemoryStream([1]), Cancellation)).ShouldBe(ArchiveOutcome.Unavailable);
            (await client.GetDocumentationAsync(SetId, "hello_words", Cancellation)).ShouldBeNull();
        }

        [Theory]
        [InlineData("""{"state":"ready"}""")]
        [InlineData("""{"state":"needed","needed":[]}""")]
        [InlineData("""{"state":"failed"}""")]
        [InlineData("""{"state":"something new"}""")]
        [InlineData("""{}""")]
        [InlineData("""<html>a proxy's error page</html>""")]
        public async Task An_answer_that_is_not_what_the_service_gives_is_unavailable(string body)
        {
            var client = ClientOver((_, _) => Task.FromResult(Json(body)));

            (await client.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Unavailable>();
        }

        [Fact]
        public async Task A_server_error_is_unavailable()
        {
            var client = ClientOver((_, _) => Task.FromResult(Json("""{"title":"boom"}""", HttpStatusCode.InternalServerError)));

            (await client.PrepareAsync(Request, Cancellation)).ShouldBeOfType<DependencyAnswer.Unavailable>();
        }

        [Fact]
        public async Task An_archive_reaches_the_service_whole()
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
            var bytes = Enumerable.Range(0, 70_000).Select(index => (byte)(index * 7)).ToArray();

            var outcome = await Client().AddArchiveAsync(Words, new MemoryStream(bytes), Cancellation);

            outcome.ShouldBe(ArchiveOutcome.Stored);
            received.ShouldBe(bytes);
            await _factory.Packages.Received(1).AddArchiveAsync(Words, Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(ArchiveOutcome.WrongDigest)]
        [InlineData(ArchiveOutcome.Malformed)]
        [InlineData(ArchiveOutcome.NotAllowed)]
        [InlineData(ArchiveOutcome.TooLarge)]
        [InlineData(ArchiveOutcome.Failed)]
        public async Task Why_an_archive_was_refused_comes_back_as_it_was(ArchiveOutcome refused)
        {
            _factory.Packages.AddArchiveAsync(Arg.Any<LockedPackage>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(refused);

            (await Client().AddArchiveAsync(Words, new MemoryStream([1, 2, 3]), Cancellation)).ShouldBe(refused);
        }

        [Fact]
        public async Task A_refusal_that_does_not_say_why_is_unavailable()
        {
            var client = ClientOver((_, _) => Task.FromResult(Json("""{"title":"no"}""", HttpStatusCode.BadGateway)));

            (await client.AddArchiveAsync(Words, new MemoryStream([1]), Cancellation)).ShouldBe(ArchiveOutcome.Unavailable);
        }

        [Fact]
        public async Task The_report_of_a_package_in_a_set_comes_back_as_text()
        {
            _factory.Packages.GetDocumentationAsync(SetId, "hello_words", Arg.Any<CancellationToken>()).Returns("""{"types":[{"name":"📖"}]}""");

            (await Client().GetDocumentationAsync(SetId, "hello_words", Cancellation)).ShouldBe("""{"types":[{"name":"📖"}]}""");
        }

        [Fact]
        public async Task A_report_that_is_not_there_is_nothing()
        {
            _factory.Packages.GetDocumentationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);

            (await Client().GetDocumentationAsync(SetId, "nope", Cancellation)).ShouldBeNull();
        }

        private sealed class Scripted(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                answer(request, cancellationToken);
        }
    }
}
