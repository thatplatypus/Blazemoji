using System.Text;
using Blazemoji.Toolchain.Http;

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
            app.MapGet(ToolchainRoutes.Packages, (PackageCatalog catalog) => Results.Json(catalog.Names(), ToolchainJson.Options));
            app.MapGet("/packages/{name}/documentation.json", PackageDocumentation);

            return app;
        }

        private static async Task<IResult> CompileAsync(CompileRequestBody body, IToolchain toolchain, PackageCatalog catalog, CancellationToken cancellationToken)
        {
            if (body.Files is null || body.Files.Count == 0)
                return BadRequest("At least one file is required.");

            if (string.IsNullOrEmpty(body.Entry) || !body.Files.ContainsKey(body.Entry))
                return BadRequest("The entry must name one of the files.");

            if (body.Files.Keys.Any(name => !SourceFileNames.IsSafe(name)))
                return BadRequest("File names must be relative and must not contain '..' segments.");

            if (body.Packages is { Length: > 0 } requested && requested.Except(catalog.Names(), StringComparer.Ordinal).Any())
                return BadRequest("An unknown package was requested.");

            var result = await toolchain.CompileAsync(new CompileRequest(body.Files, body.Entry), cancellationToken);
            var response = new CompileResponseBody(result.Ok, result.Diagnostics.Select(DiagnosticBody.From).ToList(), result.BuildId);

            return Results.Json(response, ToolchainJson.Options);
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
                var run = await toolchain.StartRunAsync(new RunRequest(body.BuildId, body.Env), cancellationToken);
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

        private static IResult PackageDocumentation(string name, PackageCatalog catalog) =>
            catalog.DocumentationPath(name) is { } path
                ? Results.File(path, "application/json")
                : Problem("No such package.", StatusCodes.Status404NotFound);

        private static IResult NoSuchRun() => Problem("No such run.", StatusCodes.Status404NotFound);

        private static IResult BadRequest(string title) => Problem(title, StatusCodes.Status400BadRequest);

        private static IResult Problem(string title, int statusCode) => Results.Problem(title: title, statusCode: statusCode);
    }
}
