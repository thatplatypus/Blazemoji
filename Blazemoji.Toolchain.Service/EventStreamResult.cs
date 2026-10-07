using System.Text;
using System.Text.Json;
using Blazemoji.Toolchain.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Blazemoji.Toolchain.Service
{
    /// <summary>
    /// Writes a run's events as a <c>text/event-stream</c> response, starting after the event
    /// the client says it saw last, and ending after the exit event.
    /// </summary>
    internal sealed class EventStreamResult(EventLog log, long afterId) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no";
            httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            try
            {
                await response.StartAsync(httpContext.RequestAborted);
                await foreach (var numbered in log.ReadAsync(afterId, httpContext.RequestAborted))
                {
                    var (name, data) = Describe(numbered.Event);
                    var frame = $"id: {numbered.Id}\nevent: {name}\ndata: {data}\n\n";
                    await response.Body.WriteAsync(Encoding.UTF8.GetBytes(frame), httpContext.RequestAborted);
                    await response.Body.FlushAsync(httpContext.RequestAborted);
                }
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                // The client went away. The run and its log carry on without it.
            }
        }

        private static (string Name, string Data) Describe(RunEvent runEvent) => runEvent switch
        {
            StdoutEvent output => (EventNames.Stdout, Serialize(new OutputEventData(output.Text))),
            StderrEvent error => (EventNames.Stderr, Serialize(new OutputEventData(error.Text))),
            ExitEvent exit => (EventNames.Exit, Serialize(new ExitEventData(exit.ExitCode, RunEndReasons.Name(exit.Reason), (long)exit.Duration.TotalMilliseconds))),
            _ => throw new InvalidOperationException($"Unknown run event {runEvent.GetType().Name}."),
        };

        private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, ToolchainJson.EventOptions);
    }
}
