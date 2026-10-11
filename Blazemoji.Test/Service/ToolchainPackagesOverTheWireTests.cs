using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Blazemoji.Test.Service
{
    /// <summary>
    /// The packages client as a host registers it, talking to the real service over a real socket.
    /// </summary>
    public sealed class ToolchainPackagesOverTheWireTests : IDisposable
    {
        private static readonly LockedPackage Words = new("@thatplatypus/hello_words", "0.1.0", "8502cea823dc837cf74985e796e3d5604035095ec715289ad5c10540c73759f8");

        private readonly ToolchainServiceFactory _factory = new();
        private readonly ServiceProvider _clientServices;

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        public ToolchainPackagesOverTheWireTests()
        {
            _factory.UseKestrel();
            _factory.StartServer();
            var address = _factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddToolchainClient(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ToolchainClient:BaseUrl"] = address })
                .Build());
            _clientServices = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            _clientServices.Dispose();
            _factory.Dispose();
        }

        [Fact]
        public async Task An_archive_larger_than_any_other_request_may_be_is_taken_whole()
        {
            long received = 0;
            _factory.Packages.AddArchiveAsync(Arg.Any<LockedPackage>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    received = call.Arg<Stream>().Length;
                    return ArchiveOutcome.Stored;
                });
            var fiveMebibytes = new byte[5 * 1024 * 1024];

            var outcome = await _clientServices.GetRequiredService<IToolchainPackages>().AddArchiveAsync(Words, new MemoryStream(fiveMebibytes), Cancellation);

            outcome.ShouldBe(ArchiveOutcome.Stored);
            received.ShouldBe(fiveMebibytes.Length);
        }

        [Fact]
        public async Task An_archive_over_its_own_limit_is_refused_as_too_large_and_not_as_a_broken_connection()
        {
            var overTheLimit = new byte[16 * 1024 * 1024 + 1];

            var outcome = await _clientServices.GetRequiredService<IToolchainPackages>().AddArchiveAsync(Words, new MemoryStream(overTheLimit), Cancellation);

            outcome.ShouldBe(ArchiveOutcome.TooLarge);
            await _factory.Packages.DidNotReceive().AddArchiveAsync(Arg.Any<LockedPackage>(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task The_registered_client_asks_the_service_it_was_pointed_at()
        {
            _factory.Packages.PrepareAsync(Arg.Any<DependencyRequest>(), Arg.Any<CancellationToken>()).Returns(new DependencyAnswer.Busy());

            var answer = await _clientServices.GetRequiredService<IToolchainPackages>().PrepareAsync(new DependencyRequest("m", "l"), Cancellation);

            answer.ShouldBeOfType<DependencyAnswer.Busy>();
        }
    }
}
