using System.Text;
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
        /// Registers <see cref="IToolchain"/> as a client of the toolchain service at
        /// <see cref="ToolchainClientOptions.BaseUrl"/>.
        /// </summary>
        public static IHttpClientBuilder AddToolchainClient(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<ToolchainClientOptions>(configuration.GetSection(ToolchainClientOptions.SectionName));

            return services.AddHttpClient<IToolchain, HttpToolchain>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<ToolchainClientOptions>>().Value;
                var baseUrl = options.BaseUrl;

                // A trailing slash makes the routes resolve under any path the address carries.
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

                client.Timeout = options.RequestTimeout > TimeSpan.Zero ? options.RequestTimeout : TimeSpan.FromMinutes(2);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // What comes back through the service can be a program's own response. A
                // redirect or a cookie in it is something to show, never something to act
                // on: following one would send this client wherever the program says.
                AllowAutoRedirect = false,
                UseCookies = false,
                RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8,
                ResponseHeaderEncodingSelector = (_, _) => Encoding.UTF8,
            });
        }
    }
}
