using System.Net;
using System.Text;
using Blazemoji.Test.Toolchain;
using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Blazemoji.Test.Service
{
    public class ToolchainClientRegistrationTests
    {
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static (IToolchain Toolchain, List<Uri> Requests) Build(Dictionary<string, string?> settings)
        {
            var requests = new List<Uri>();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddToolchainClient(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
                .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(requests));

            return (services.BuildServiceProvider().GetRequiredService<IToolchain>(), requests);
        }

        [Fact]
        public async Task The_client_talks_to_the_configured_service_address()
        {
            var (toolchain, requests) = Build(new() { ["ToolchainClient:BaseUrl"] = "http://toolchain.test:8080" });

            await toolchain.CompileAsync(ToolchainFixture.SingleFile("x"), Cancellation);

            toolchain.ShouldBeOfType<HttpToolchain>();
            requests.ShouldHaveSingleItem().ShouldBe(new Uri("http://toolchain.test:8080/compile"));
        }

        [Fact]
        public async Task Without_configuration_the_client_uses_the_local_default()
        {
            var (toolchain, requests) = Build([]);

            await toolchain.CompileAsync(ToolchainFixture.SingleFile("x"), Cancellation);

            requests.ShouldHaveSingleItem().ShouldBe(new Uri("http://localhost:5290/compile"));
        }

        [Fact]
        public void A_request_to_the_service_is_given_up_on_after_a_while()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddToolchainClient(new ConfigurationBuilder().AddInMemoryCollection([]).Build());

            var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IToolchain));

            // Long enough for the slowest compile the service allows, and not for ever.
            client.Timeout.ShouldBe(TimeSpan.FromMinutes(2));
        }

        [Fact]
        public void The_time_to_give_up_after_can_be_configured()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddToolchainClient(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ToolchainClient:RequestTimeout"] = "00:00:45" }).Build());

            var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IToolchain));

            client.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
        }

        [Fact]
        public async Task A_base_address_with_a_path_keeps_its_path()
        {
            var (toolchain, requests) = Build(new() { ["ToolchainClient:BaseUrl"] = "http://gateway.test/toolchain/" });

            await toolchain.CompileAsync(ToolchainFixture.SingleFile("x"), Cancellation);

            requests.ShouldHaveSingleItem().ShouldBe(new Uri("http://gateway.test/toolchain/compile"));
        }

        private sealed class RecordingHandler(List<Uri> requests) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                requests.Add(request.RequestUri!);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"ok":true,"diagnostics":[],"buildId":"b"}""", Encoding.UTF8, "application/json"),
                });
            }
        }
    }
}
