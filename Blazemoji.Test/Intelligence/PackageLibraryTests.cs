using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Test.Service;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Blazemoji.Test.Intelligence
{
    public sealed class PackageLibraryTests : IDisposable
    {
        private const string Report = """{"documentation":" A package. ","types":[{"type":"Class","name":"🦄","documentation":"","methods":[],"typeMethods":[],"initializers":[]}]}""";

        private readonly IPackageDocumentationSource _source = Substitute.For<IPackageDocumentationSource>();
        private readonly ToolchainServiceFactory _factory = new();

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        public void Dispose() => _factory.Dispose();

        [Fact]
        public async Task The_documentation_of_each_package_is_read_and_returned_in_the_order_asked_for()
        {
            _source.GetAsync("a", Arg.Any<CancellationToken>()).Returns(Report);
            _source.GetAsync("b", Arg.Any<CancellationToken>()).Returns(Report);

            var packages = await new PackageLibrary(_source).GetAsync(["b", "a"], Cancellation);

            packages.Select(package => package.Name).ShouldBe(["b", "a"]);
            packages[0].Documentation.ShouldBe("A package.");
            packages[0].Find("🦄").ShouldNotBeNull();
        }

        [Fact]
        public async Task A_package_is_fetched_once_however_often_it_is_asked_for()
        {
            _source.GetAsync("a", Arg.Any<CancellationToken>()).Returns(Report);
            var library = new PackageLibrary(_source);

            await library.GetAsync(["a", "a"], Cancellation);
            await library.GetAsync(["a"], Cancellation);

            await _source.Received(1).GetAsync("a", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_package_without_documentation_is_left_out_and_asked_for_again_next_time()
        {
            _source.GetAsync("late", Arg.Any<CancellationToken>()).Returns(null, Report);
            _source.GetAsync("garbled", Arg.Any<CancellationToken>()).Returns("not json");
            var library = new PackageLibrary(_source);

            (await library.GetAsync(["late", "garbled"], Cancellation)).ShouldBeEmpty();
            (await library.GetAsync(["late"], Cancellation)).ShouldHaveSingleItem().Name.ShouldBe("late");
        }

        [Fact]
        public async Task The_service_hands_over_a_packages_report_and_nothing_for_one_it_does_not_have()
        {
            var source = new HttpPackageDocumentationSource(_factory.CreateClient(), NullLogger<HttpPackageDocumentationSource>.Instance);

            (await source.GetAsync("s", Cancellation)).ShouldNotBeNull().ShouldContain("🔡");
            (await source.GetAsync("no-such-package", Cancellation)).ShouldBeNull();
        }

        [Fact]
        public async Task A_service_that_cannot_be_reached_gives_nothing_and_does_not_throw()
        {
            var http = new HttpClient(new Unreachable()) { BaseAddress = new Uri("http://toolchain.test/") };
            var source = new HttpPackageDocumentationSource(http, NullLogger<HttpPackageDocumentationSource>.Instance);

            (await source.GetAsync("s", Cancellation)).ShouldBeNull();
        }

        private sealed class Unreachable : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused"));
        }
    }
}
