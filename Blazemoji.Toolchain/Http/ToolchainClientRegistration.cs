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

        /// <summary>
        /// How long to wait for the service to answer one request. Longer than the slowest
        /// compile the service allows. Reading a run's event stream is not subject to it once
        /// the stream has opened.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(2);
    }

    public static class ToolchainClientRegistration
    {
        /// <summary>
        /// Registers <see cref="IToolchain"/> and <see cref="IPackageDocumentationSource"/> as
        /// clients of the toolchain service at <see cref="ToolchainClientOptions.BaseUrl"/>.
        /// </summary>
        public static IHttpClientBuilder AddToolchainClient(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<ToolchainClientOptions>(configuration.GetSection(ToolchainClientOptions.SectionName));

            services.AddHttpClient<IPackageDocumentationSource, HttpPackageDocumentationSource>(PointAtTheService);
            return services.AddHttpClient<IToolchain, HttpToolchain>(PointAtTheService);
        }

        private static void PointAtTheService(IServiceProvider provider, HttpClient client)
        {
            var options = provider.GetRequiredService<IOptions<ToolchainClientOptions>>().Value;

            // A trailing slash makes the routes resolve under any path the address carries.
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");

            client.Timeout = options.RequestTimeout > TimeSpan.Zero ? options.RequestTimeout : TimeSpan.FromMinutes(2);
        }
    }
}
