using Microsoft.Extensions.Logging;

namespace Blazemoji.Toolchain.Http
{
    /// <summary>
    /// Package documentation from the toolchain service.
    /// </summary>
    public sealed class HttpPackageDocumentationSource(HttpClient http, ILogger<HttpPackageDocumentationSource> logger) : IPackageDocumentationSource
    {
        public async Task<string?> GetAsync(string package, CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await http.GetAsync(ToolchainRoutes.PackageDocumentation(package).TrimStart('/'), cancellationToken);
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
