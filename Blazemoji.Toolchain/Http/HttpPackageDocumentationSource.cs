using Blazemoji.Toolchain.Packmoji;
using Microsoft.Extensions.Logging;

namespace Blazemoji.Toolchain.Http
{
    /// <summary>
    /// Package documentation from the toolchain service.
    /// </summary>
    public sealed class HttpPackageDocumentationSource(HttpClient http, ILogger<HttpPackageDocumentationSource> logger) : IPackageDocumentationSource
    {
        public async Task<string?> GetAsync(string package, string? dependencies = null, CancellationToken cancellationToken = default)
        {
            // The project's own package first: the compiler searches the set ahead of the
            // stock packages, so a locked package that shares a stock one's name is the one
            // the project is compiled against. The name comes from what someone typed after
            // an import, and only one that could be a locked package's goes into a set's address.
            if (dependencies is not null
                && PackageNames.IsBareName(package)
                && await FetchAsync(ToolchainRoutes.DependencyDocumentation(dependencies, package), package, cancellationToken) is { } own)
            {
                return own;
            }

            return await FetchAsync(ToolchainRoutes.PackageDocumentation(package), package, cancellationToken);
        }

        private async Task<string?> FetchAsync(string route, string package, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await http.GetAsync(route.TrimStart('/'), cancellationToken);
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
            }
            catch (Exception exception) when (exception is HttpRequestException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                logger.LogWarning(exception, "The documentation for package {Package} could not be fetched", package);
                return null;
            }
        }
    }
}
