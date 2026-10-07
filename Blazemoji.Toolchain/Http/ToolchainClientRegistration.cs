using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Blazemoji.Toolchain.Http
{
    public sealed class ToolchainClientOptions
    {
        public const string SectionName = "ToolchainClient";

        /// <summary>
        /// Where the toolchain service is. This is the one setting a host needs in order to
        /// compile and run Emojicode: the web app and a desktop host point it at their service.
        /// </summary>
        public string BaseUrl { get; set; } = "http://localhost:5290";
    }

    public static class ToolchainClientRegistration
    {
        /// <summary>
        /// Registers <see cref="IToolchain"/> as a client of the toolchain service at
        /// <see cref="ToolchainClientOptions.BaseUrl"/>.
        /// </summary>
        public static IHttpClientBuilder AddToolchainClient(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<ToolchainClientOptions>(configuration.GetSection(ToolchainClientOptions.SectionName));

            return services.AddHttpClient<IToolchain, HttpToolchain>((provider, client) =>
            {
                var baseUrl = provider.GetRequiredService<IOptions<ToolchainClientOptions>>().Value.BaseUrl;

                // A trailing slash makes the routes resolve under any path the address carries.
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

                // An event stream lasts as long as the program runs; the run's own limits bound it.
                client.Timeout = Timeout.InfiniteTimeSpan;
            });
        }
    }
}
