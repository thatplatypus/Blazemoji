using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.Extensions.Logging;

namespace Blazemoji.Toolchain.Http
{
    /// <summary>
    /// A project's packages as a client of the toolchain service. Every way of failing to get
    /// an answer is an answer here, so a caller handles one shape of result and shows no
    /// text of an exception.
    /// </summary>
    public sealed class HttpToolchainPackages(HttpClient http, ILogger<HttpToolchainPackages> logger) : IToolchainPackages
    {
        public async Task<DependencyAnswer> PrepareAsync(DependencyRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await http.PostAsJsonAsync(
                    ToolchainRoutes.Dependencies.TrimStart('/'),
                    new DependenciesRequestBody(request.Manifest, request.Lockfile),
                    ToolchainJson.Options,
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    return new DependencyAnswer.Busy();

                if (response.IsSuccessStatusCode
                    && await response.Content.ReadFromJsonAsync<DependenciesResponseBody>(ToolchainJson.Options, cancellationToken) is { } body)
                {
                    switch (body.State)
                    {
                        case DependencyStates.Needed when body.Needed is { Count: > 0 } needed:
                            return new DependencyAnswer.Needed(needed.Select(package => package.ToLockedPackage()).ToList());
                        case DependencyStates.Ready when body.Id is { Length: > 0 } id:
                            return new DependencyAnswer.Ready(id, body.Packages ?? []);
                        case DependencyStates.Failed when body.Diagnostics is { Count: > 0 } diagnostics:
                            return new DependencyAnswer.Failed(diagnostics.Select(problem => problem.ToProblem()).ToList());
                    }
                }

                logger.LogError("The toolchain service answered a request for a set of packages with {StatusCode} and nothing that could be used", (int)response.StatusCode);
                return new DependencyAnswer.Unavailable();
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(exception, "The toolchain service could not be reached");
                return new DependencyAnswer.Unavailable();
            }
            catch (JsonException exception)
            {
                logger.LogError(exception, "The toolchain service answered a request for a set of packages with what is not JSON");
                return new DependencyAnswer.Unavailable();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The service waits for a build before it answers, and the first build of a
                // large package outlasts one request. The build goes on there, and asking
                // again joins it.
                return new DependencyAnswer.Busy();
            }
        }

        public async Task<ArchiveOutcome> AddArchiveAsync(LockedPackage package, Stream content, CancellationToken cancellationToken = default)
        {
            try
            {
                using var body = new StreamContent(content);
                body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                using var response = await http.PutAsync(ToolchainRoutes.DependencyArchive(package).TrimStart('/'), body, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NoContent)
                    return ArchiveOutcome.Stored;

                if (response.Headers.TryGetValues(ArchiveReasons.Header, out var reasons))
                    return ArchiveReasons.Parse(reasons.FirstOrDefault());

                logger.LogError("The toolchain service answered an archive with {StatusCode} and no reason", (int)response.StatusCode);
                return ArchiveOutcome.Unavailable;
            }
            catch (HttpRequestException exception)
            {
                logger.LogError(exception, "The toolchain service could not be reached");
                return ArchiveOutcome.Unavailable;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(exception, "The toolchain service did not take an archive in time");
                return ArchiveOutcome.Unavailable;
            }
        }

        public async Task<string?> GetDocumentationAsync(string dependencies, string package, CancellationToken cancellationToken = default)
        {
            try
            {
                using var response = await http.GetAsync(ToolchainRoutes.DependencyDocumentation(dependencies, package).TrimStart('/'), cancellationToken);
                return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
            }
            catch (Exception exception) when (exception is HttpRequestException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                logger.LogWarning(exception, "The documentation for package {Package} of a set could not be fetched", package);
                return null;
            }
        }
    }
}
