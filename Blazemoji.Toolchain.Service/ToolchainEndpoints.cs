using System.Text;
using Blazemoji.Toolchain.Http;
using Blazemoji.Toolchain.Local;
using Blazemoji.Toolchain.Packmoji;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Blazemoji.Toolchain.Service
{
    public static class ToolchainEndpoints
    {
        private const string LastEventIdHeader = "Last-Event-ID";

        public static IEndpointRouteBuilder MapToolchainEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet(ToolchainRoutes.Health, () => Results.Text("ok"));
            app.MapPost(ToolchainRoutes.Compile, CompileAsync);
            app.MapPost(ToolchainRoutes.Runs, StartRunAsync);
            app.MapGet("/runs/{runId}/events", StreamEvents);
            app.MapPost("/runs/{runId}/stdin", WriteInputAsync);
            app.MapDelete("/runs/{runId}", StopRunAsync);
            app.Map("/runs/{runId}/http/{**path}", ProxyAsync);
            app.MapGet(ToolchainRoutes.Packages, (PackageCatalog catalog) => Results.Json(catalog.Names(), ToolchainJson.Options));
            app.MapGet("/packages/{name}/documentation.json", PackageDocumentation);
            app.MapPost(ToolchainRoutes.Dependencies, PrepareDependenciesAsync);
            app.MapPut("/dependencies/archives/{sha256}", AddArchiveAsync);
            app.MapGet("/dependencies/{id}/packages/{name}/documentation.json", DependencyDocumentationAsync);

            return app;
        }

        private static async Task<IResult> CompileAsync(CompileRequestBody body, IToolchain toolchain, CompileGate gate, CancellationToken cancellationToken)
        {
            if (body.Files is null || body.Files.Count == 0)
                return BadRequest("At least one file is required.");

            if (string.IsNullOrEmpty(body.Entry) || !body.Files.ContainsKey(body.Entry))
                return BadRequest("The entry must name one of the files.");

            if (body.Files.Keys.Any(name => !SourceFileNames.IsSafe(name)))
                return BadRequest("File names must be relative and must not contain '..' segments.");

            if (!gate.TryEnter())
                return Problem("Too many programs are being compiled. Try again shortly.", StatusCodes.Status429TooManyRequests);

            try
            {
                var result = await toolchain.CompileAsync(new CompileRequest(body.Files, body.Entry, body.Check, body.Dependencies), cancellationToken);
                if (result.DependenciesMissing)
                    return Problem("The project's packages are not built here.", StatusCodes.Status409Conflict);

                var response = new CompileResponseBody(result.Ok, result.Diagnostics.Select(DiagnosticBody.From).ToList(), result.BuildId);

                return Results.Json(response, ToolchainJson.Options);
            }
            finally
            {
                gate.Leave();
            }
        }

        private static async Task<IResult> StartRunAsync(StartRunBody body, IToolchain toolchain, IBuildStore builds, RunRegistry registry, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(body.BuildId))
                return BadRequest("A build id is required.");

            if (!builds.HasBuild(body.BuildId))
                return Problem("No such build.", StatusCodes.Status404NotFound);

            if (!registry.TryReserve())
                return Problem("Too many programs are running. Try again shortly.", StatusCodes.Status429TooManyRequests);

            try
            {
                var run = await toolchain.StartRunAsync(new RunRequest(body.BuildId, body.Env, Server: body.Http), cancellationToken);
                registry.Add(run);
                return Results.Json(new RunStartedBody(run.RunId), ToolchainJson.Options, statusCode: StatusCodes.Status201Created);
            }
            catch
            {
                registry.ReleaseReservation();
                throw;
            }
        }

        private static IResult StreamEvents(string runId, HttpRequest request, RunRegistry registry)
        {
            if (registry.Find(runId) is not { } session)
                return NoSuchRun();

            var afterId = long.TryParse(request.Headers[LastEventIdHeader], out var lastSeen) && lastSeen > 0 ? lastSeen : 0;
            return new EventStreamResult(session.Log, afterId);
        }

        private static async Task<IResult> WriteInputAsync(string runId, bool? eof, HttpRequest request, RunRegistry registry, CancellationToken cancellationToken)
        {
            if (registry.Find(runId) is not { } session)
                return NoSuchRun();

            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var text = await reader.ReadToEndAsync(cancellationToken);

            try
            {
                await session.Run.WriteInputAsync(text, eof == true, cancellationToken);
                return Results.NoContent();
            }
            catch (InvalidOperationException)
            {
                return Problem("The run is not accepting input.", StatusCodes.Status409Conflict);
            }
        }

        private static async Task<IResult> StopRunAsync(string runId, RunRegistry registry)
        {
            if (registry.Find(runId) is not { } session)
                return NoSuchRun();

            await session.Run.StopAsync();
            return Results.NoContent();
        }

        /// <summary>
        /// Passes a request on to a server program and its response back. Anything this
        /// service says for itself on this route is marked with <see cref="ProxyReasons.Header"/>.
        /// </summary>
        private static async Task ProxyAsync(HttpContext context, string runId, string? path, RunRegistry registry, IOptions<ToolchainServiceOptions> options)
        {
            if (registry.Find(runId) is not { } session)
            {
                await WriteProxyProblemAsync(context, StatusCodes.Status404NotFound, ProxyReasons.UnknownRun, "No such run.");
                return;
            }

            if (await ReadBodyAsync(context.Request, options.Value.MaxProxiedRequestBytes, context.RequestAborted) is not { } body)
            {
                await WriteProxyProblemAsync(context, ProgramResponseOutcome.TooLarge);
                return;
            }

            var target = new PathString("/" + path).ToUriComponent() + context.Request.QueryString.ToUriComponent();
            var headers = context.Request.Headers
                .SelectMany(header => header.Value.Select(value => new KeyValuePair<string, string>(header.Key, value ?? string.Empty)))
                .ToList();

            var response = await session.Run.SendHttpAsync(new ProgramRequest(context.Request.Method, target, headers, body), context.RequestAborted);
            if (response.Outcome != ProgramResponseOutcome.Answered)
            {
                await WriteProxyProblemAsync(context, response.Outcome);
                return;
            }

            var passedOn = response.Headers
                .Where(header => !ProgramHttp.IsHopByHop(header.Key) && !ProgramHttp.IsContentLength(header.Key) && !header.Key.Equals(ProxyReasons.Header, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // The server refuses to send such a header. Left to it, the refusal would reach
            // the caller as a 500 that looks like the program's own.
            if (passedOn.Any(header => !ProgramHttp.CanBeSentAsHeader(header.Key, header.Value)))
            {
                await WriteProxyProblemAsync(context, ProgramResponseOutcome.BadResponse);
                return;
            }

            context.Response.StatusCode = response.StatusCode;
            if (response.ReasonPhrase is { Length: > 0 } reason && context.Features.Get<IHttpResponseFeature>() is { } feature)
                feature.ReasonPhrase = reason;

            foreach (var (name, value) in passedOn)
                context.Response.Headers.Append(name, value);

            if (!HttpMethods.IsHead(context.Request.Method) && StatusAllowsABody(response.StatusCode))
            {
                context.Response.ContentLength = response.Body.Length;
                await context.Response.Body.WriteAsync(response.Body, context.RequestAborted);
            }
        }

        private static bool StatusAllowsABody(int statusCode) =>
            statusCode >= 200 && statusCode is not (StatusCodes.Status204NoContent or StatusCodes.Status205ResetContent or StatusCodes.Status304NotModified);

        /// <returns>Null when the body is longer than <paramref name="limit"/>.</returns>
        private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, long limit, CancellationToken cancellationToken)
        {
            if (request.ContentLength > limit)
                return null;

            using var body = new MemoryStream();
            var buffer = new byte[16 * 1024];

            int read;
            while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (body.Length + read > limit)
                    return null;

                body.Write(buffer, 0, read);
            }

            return body.ToArray();
        }

        private static Task WriteProxyProblemAsync(HttpContext context, ProgramResponseOutcome outcome)
        {
            var (statusCode, title) = outcome switch
            {
                ProgramResponseOutcome.NotAServer => (StatusCodes.Status409Conflict, "The run is not a server."),
                ProgramResponseOutcome.Ended => (StatusCodes.Status409Conflict, "The run has ended."),
                ProgramResponseOutcome.NotListening => (StatusCodes.Status502BadGateway, "The program is not accepting connections."),
                ProgramResponseOutcome.TooLarge => (StatusCodes.Status413PayloadTooLarge, "The request body is too large."),
                ProgramResponseOutcome.TimedOut => (StatusCodes.Status504GatewayTimeout, "The program did not answer in time."),
                ProgramResponseOutcome.InvalidRequest => (StatusCodes.Status400BadRequest, "The request could not be sent to the program."),
                _ => (StatusCodes.Status502BadGateway, "The program did not send a usable response."),
            };

            return WriteProxyProblemAsync(context, statusCode, ProxyReasons.Name(outcome), title);
        }

        private static Task WriteProxyProblemAsync(HttpContext context, int statusCode, string reason, string title)
        {
            context.Response.Headers[ProxyReasons.Header] = reason;
            return Problem(title, statusCode).ExecuteAsync(context);
        }

        private static IResult PackageDocumentation(string name, PackageCatalog catalog) =>
            catalog.DocumentationPath(name) is { } path
                ? Results.File(path, "application/json")
                : Problem("No such package.", StatusCodes.Status404NotFound);

        private static IResult NoSuchRun() => Problem("No such run.", StatusCodes.Status404NotFound);

        private static async Task<IResult> PrepareDependenciesAsync(DependenciesRequestBody body, IToolchainPackages packages, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(body.Manifest) || string.IsNullOrEmpty(body.Lockfile))
                return BadRequest("A manifest and a lockfile are required.");

            return await packages.PrepareAsync(new DependencyRequest(body.Manifest, body.Lockfile), cancellationToken) switch
            {
                DependencyAnswer.Needed needed => Results.Json(
                    new DependenciesResponseBody(DependencyStates.Needed, Needed: needed.Archives.Select(LockedPackageBody.From).ToList()), ToolchainJson.Options),
                DependencyAnswer.Ready ready => Results.Json(
                    new DependenciesResponseBody(DependencyStates.Ready, Id: ready.Id, Packages: ready.Packages), ToolchainJson.Options),
                DependencyAnswer.Failed failed => Results.Json(
                    new DependenciesResponseBody(DependencyStates.Failed, Diagnostics: failed.Problems.Select(PackageProblemBody.From).ToList()), ToolchainJson.Options),
                DependencyAnswer.Busy => Problem("Too many sets of packages are being built. Try again shortly.", StatusCodes.Status429TooManyRequests),
                _ => Problem("The project's packages could not be prepared.", StatusCodes.Status503ServiceUnavailable),
            };
        }

        private static async Task<IResult> AddArchiveAsync(string sha256, string? name, string? version, HttpContext context, IToolchainPackages packages, IOptions<ToolchainOptions> toolchain)
        {
            var limit = toolchain.Value.MaxArchiveBytes;

            // An archive may be larger than any other request, so the server's own limit is
            // raised for this request alone, before its body is read. It is raised to one byte
            // past the limit: the reading below then sees a body that is too long and refuses
            // it in the service's own words, where at the limit itself the server would end the
            // request first, in its own.
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
                size.MaxRequestBodySize = limit + 1;

            if (await ReadBodyAsync(context.Request, limit, context.RequestAborted) is not { } body)
                return Refused(context, ArchiveOutcome.TooLarge);

            var package = new LockedPackage(name ?? string.Empty, version ?? string.Empty, sha256);
            var outcome = await packages.AddArchiveAsync(package, new MemoryStream(body), context.RequestAborted);

            return outcome == ArchiveOutcome.Stored ? Results.NoContent() : Refused(context, outcome);
        }

        private static IResult Refused(HttpContext context, ArchiveOutcome outcome)
        {
            var (statusCode, title) = outcome switch
            {
                ArchiveOutcome.WrongDigest => (StatusCodes.Status400BadRequest, "The archive is not the bytes its digest names."),
                ArchiveOutcome.Malformed => (StatusCodes.Status400BadRequest, "The package's name, version or digest is not well formed."),
                ArchiveOutcome.NotAllowed => (StatusCodes.Status400BadRequest, "The package is not of a scope this service builds."),
                ArchiveOutcome.TooLarge => (StatusCodes.Status413PayloadTooLarge, "The archive is too large."),
                _ => (StatusCodes.Status500InternalServerError, "The archive could not be kept."),
            };

            context.Response.Headers[ArchiveReasons.Header] = ArchiveReasons.Name(outcome) ?? ArchiveReasons.Name(ArchiveOutcome.Failed);
            return Problem(title, statusCode);
        }

        private static async Task<IResult> DependencyDocumentationAsync(string id, string name, IToolchainPackages packages, CancellationToken cancellationToken) =>
            await packages.GetDocumentationAsync(id, name, cancellationToken) is { } report
                ? Results.Text(report, "application/json", Encoding.UTF8)
                : Problem("No such package.", StatusCodes.Status404NotFound);

        private static IResult BadRequest(string title) => Problem(title, StatusCodes.Status400BadRequest);

        private static IResult Problem(string title, int statusCode) => Results.Problem(title: title, statusCode: statusCode);
    }
}
