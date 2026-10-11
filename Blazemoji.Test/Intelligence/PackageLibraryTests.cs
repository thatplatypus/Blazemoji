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
            _source.GetAsync("a", null, Arg.Any<CancellationToken>()).Returns(Report);
            _source.GetAsync("b", null, Arg.Any<CancellationToken>()).Returns(Report);

            var packages = await new PackageLibrary(_source).GetAsync(["b", "a"], cancellationToken: Cancellation);

            packages.Select(package => package.Name).ShouldBe(["b", "a"]);
            packages[0].Documentation.ShouldBe("A package.");
            packages[0].Find("🦄").ShouldNotBeNull();
        }

        [Fact]
        public async Task A_package_is_fetched_once_however_often_it_is_asked_for()
        {
            _source.GetAsync("a", null, Arg.Any<CancellationToken>()).Returns(Report);
            var library = new PackageLibrary(_source);

            await library.GetAsync(["a", "a"], cancellationToken: Cancellation);
            await library.GetAsync(["a"], cancellationToken: Cancellation);

            await _source.Received(1).GetAsync("a", null, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_package_without_documentation_is_left_out_and_asked_for_again_next_time()
        {
            _source.GetAsync("late", null, Arg.Any<CancellationToken>()).Returns(null, Report);
            _source.GetAsync("garbled", null, Arg.Any<CancellationToken>()).Returns("not json");
            var library = new PackageLibrary(_source);

            (await library.GetAsync(["late", "garbled"], cancellationToken: Cancellation)).ShouldBeEmpty();
            (await library.GetAsync(["late"], cancellationToken: Cancellation)).ShouldHaveSingleItem().Name.ShouldBe("late");
        }

        [Fact]
        public async Task The_service_hands_over_a_packages_report_and_nothing_for_one_it_does_not_have()
        {
            var source = new HttpPackageDocumentationSource(_factory.CreateClient(), NullLogger<HttpPackageDocumentationSource>.Instance);

            (await source.GetAsync("s", cancellationToken: Cancellation)).ShouldNotBeNull().ShouldContain("🔡");
            (await source.GetAsync("no-such-package", cancellationToken: Cancellation)).ShouldBeNull();
        }

        [Fact]
        public async Task A_service_that_cannot_be_reached_gives_nothing_and_does_not_throw()
        {
            var http = new HttpClient(new Unreachable()) { BaseAddress = new Uri("http://toolchain.test/") };
            var source = new HttpPackageDocumentationSource(http, NullLogger<HttpPackageDocumentationSource>.Instance);

            (await source.GetAsync("s", cancellationToken: Cancellation)).ShouldBeNull();
        }

        [Fact]
        public async Task A_report_is_kept_by_set_as_well_as_by_name_so_that_another_lockfile_shows_nothing_stale()
        {
            _source.GetAsync("greeter", "set-1", Arg.Any<CancellationToken>()).Returns(Report);
            _source.GetAsync("greeter", "set-2", Arg.Any<CancellationToken>()).Returns(Report.Replace("A package.", "Another version."));
            var library = new PackageLibrary(_source);

            var first = await library.GetAsync(["greeter"], "set-1", Cancellation);
            var again = await library.GetAsync(["greeter"], "set-1", Cancellation);
            var second = await library.GetAsync(["greeter"], "set-2", Cancellation);

            first.ShouldHaveSingleItem().Documentation.ShouldBe("A package.");
            again.ShouldHaveSingleItem().Documentation.ShouldBe("A package.");
            second.ShouldHaveSingleItem().Documentation.ShouldBe("Another version.");
            await _source.Received(1).GetAsync("greeter", "set-1", Arg.Any<CancellationToken>());
            await _source.Received(1).GetAsync("greeter", "set-2", Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task A_report_read_for_a_set_is_not_given_to_a_project_with_no_set()
        {
            _source.GetAsync("greeter", "set-1", Arg.Any<CancellationToken>()).Returns(Report);
            var library = new PackageLibrary(_source);

            await library.GetAsync(["greeter"], "set-1", Cancellation);

            (await library.GetAsync(["greeter"], cancellationToken: Cancellation)).ShouldBeEmpty();
        }

        [Fact]
        public async Task The_service_is_asked_for_a_sets_own_report_first_and_for_the_stock_one_when_the_set_has_none()
        {
            // Said outright: a stand-in that is told nothing answers with empty text, which is not "none".
            _factory.Packages.GetDocumentationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
            _factory.Packages.GetDocumentationAsync("set-1", "greeter", Arg.Any<CancellationToken>()).Returns(Report);
            var source = new HttpPackageDocumentationSource(_factory.CreateClient(), NullLogger<HttpPackageDocumentationSource>.Instance);

            var own = await source.GetAsync("greeter", "set-1", Cancellation);
            var stock = await source.GetAsync("s", "set-1", Cancellation);
            var neither = await source.GetAsync("no_such_package", "set-1", Cancellation);

            own.ShouldBe(Report);
            stock.ShouldNotBeNull().ShouldContain("\"types\"");
            neither.ShouldBeNull();
        }

        [Fact]
        public async Task A_name_that_could_not_be_a_locked_package_is_never_put_in_a_sets_address()
        {
            var source = new HttpPackageDocumentationSource(_factory.CreateClient(), NullLogger<HttpPackageDocumentationSource>.Instance);

            (await source.GetAsync("..", "set-1", Cancellation)).ShouldBeNull();
            (await source.GetAsync("Not A Name", "set-1", Cancellation)).ShouldBeNull();

            await _factory.Packages.DidNotReceive().GetDocumentationAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        private sealed class Unreachable : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused"));
        }
    }
}
