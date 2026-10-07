using Microsoft.Extensions.Options;

namespace Blazemoji.Toolchain.Service
{
    /// <summary>
    /// Reads every run's events into its log for as long as the run lasts, whether or not a
    /// client is listening, and removes runs once they have been finished for long enough.
    /// </summary>
    public sealed class RunPumpService(
        RunRegistry registry,
        IOptions<ToolchainServiceOptions> options,
        TimeProvider timeProvider,
        ILogger<RunPumpService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var parallel = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(options.Value.MaxConcurrentRuns, 1),
                CancellationToken = stoppingToken,
            };

            try
            {
                await Task.WhenAll(
                    Parallel.ForEachAsync(registry.Started.ReadAllAsync(stoppingToken), parallel, PumpAsync),
                    SweepAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The host is shutting down.
            }
            finally
            {
                foreach (var session in registry.RemoveAll())
                    await session.Run.DisposeAsync();
            }
        }

        private async ValueTask PumpAsync(RunSession session, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var runEvent in session.Run.ReadEventsAsync(cancellationToken))
                    session.Log.Append(runEvent);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Reading the events of run {RunId} failed", session.RunId);
            }
            finally
            {
                // A reader must always be able to reach the end of the log.
                if (!session.Log.IsComplete)
                    session.Log.Append(new ExitEvent(null, RunEndReason.Stopped, TimeSpan.Zero));

                registry.MarkEnded(session, timeProvider.GetUtcNow());

                foreach (var dropped in registry.RemoveOverBudget())
                    await dropped.Run.DisposeAsync();
            }
        }

        private async Task SweepAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(options.Value.SweepInterval, timeProvider);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                foreach (var session in registry.RemoveExpired(timeProvider.GetUtcNow(), options.Value.RunRetention))
                    await session.Run.DisposeAsync();
            }
        }
    }
}
