using Blazemoji.Toolchain;

namespace Blazemoji.Shared.State
{
    /// <summary>
    /// Owns everything about the current compile and run: status, output, diagnostics and the
    /// outcome. Components render from it and ask it to run or stop; nothing else talks to the toolchain.
    /// </summary>
    public sealed class RunState(IToolchain toolchain, ILogger<RunState> logger, TimeProvider timeProvider) : IAsyncDisposable
    {
        public const int MaxLines = 5000;
        public const int MaxLineLength = 4000;

        private const string EntryFile = "main.🍇";

        // Output can arrive thousands of times a second. Subscribers are told at most this often.
        private static readonly TimeSpan NotifyInterval = TimeSpan.FromMilliseconds(50);

        private readonly List<OutputLine> _lines = [];
        private readonly OutputAssembler _stdout = new(MaxLineLength);
        private readonly OutputAssembler _stderr = new(MaxLineLength);
        private readonly CancellationTokenSource _disposal = new();

        private IToolchainRun? _run;
        private CancellationTokenSource? _compileCancellation;
        private bool _stopRequested;

        public RunStatus Status { get; private set; } = RunStatus.Idle;

        public IReadOnlyList<OutputLine> Lines => _lines;

        /// <summary>
        /// Lines produced by the current run, including those no longer kept in <see cref="Lines"/>.
        /// </summary>
        public long TotalLines { get; private set; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; private set; } = [];

        /// <summary>
        /// The code that <see cref="Diagnostics"/> refers to.
        /// </summary>
        public string DiagnosticsSource { get; private set; } = string.Empty;

        public RunSummary? LastRun { get; private set; }

        public event Action? StateChanged;

        public event Action? DiagnosticsChanged;

        public async Task RunAsync(string code)
        {
            if (Status != RunStatus.Idle || _disposal.IsCancellationRequested)
                return;

            ResetOutput();
            Status = RunStatus.Compiling;
            NotifyStateChanged();

            string? buildId = null;
            using var compileCancellation = CancellationTokenSource.CreateLinkedTokenSource(_disposal.Token);
            _compileCancellation = compileCancellation;

            try
            {
                var request = new CompileRequest(new Dictionary<string, string> { [EntryFile] = code }, EntryFile);
                var build = await toolchain.CompileAsync(request, compileCancellation.Token);

                Diagnostics = build.Diagnostics;
                DiagnosticsSource = code;
                DiagnosticsChanged?.Invoke();

                if (!build.Ok || build.BuildId is null)
                    return;

                buildId = build.BuildId;
                Status = RunStatus.Running;
                NotifyStateChanged();

                _run = await toolchain.StartRunAsync(new RunRequest(buildId), _disposal.Token);
                await ConsumeAsync(_run, _disposal.Token);
            }
            catch (OperationCanceledException) when (_disposal.IsCancellationRequested)
            {
                // The circuit is going away; there is nobody left to tell.
            }
            catch (OperationCanceledException) when (_stopRequested)
            {
                AddLine(OutputStream.System, "Stopped.");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The run failed");
                AddLine(OutputStream.System, "The run could not be completed. The details are in the server log.");
            }
            finally
            {
                _compileCancellation = null;
                await EndRunAsync(buildId);
                Status = RunStatus.Idle;
                NotifyStateChanged();
            }
        }

        public async Task StopAsync()
        {
            _stopRequested = true;

            if (_run is { } run)
                await run.StopAsync();
            else if (_compileCancellation is { } compiling)
                await compiling.CancelAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposal.IsCancellationRequested)
                return;

            await _disposal.CancelAsync();

            if (_run is { } run)
                await run.DisposeAsync();
        }

        private async Task ConsumeAsync(IToolchainRun run, CancellationToken cancellationToken)
        {
            using var flushCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            await using var events = run.ReadEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

            var next = events.MoveNextAsync().AsTask();
            Task? flushDue = null;

            try
            {
                while (true)
                {
                    if (flushDue is not null && await Task.WhenAny(next, flushDue) == flushDue)
                    {
                        await flushDue;
                        flushDue = null;
                        NotifyStateChanged();
                        continue;
                    }

                    if (!await next)
                        break;

                    if (Apply(events.Current))
                        flushDue ??= Task.Delay(NotifyInterval, timeProvider, flushCancellation.Token);

                    next = events.MoveNextAsync().AsTask();
                }
            }
            finally
            {
                await flushCancellation.CancelAsync();
                await ObserveCancelledAsync(flushDue);
            }
        }

        /// <returns>True when the output changed and subscribers need telling.</returns>
        private bool Apply(RunEvent runEvent)
        {
            switch (runEvent)
            {
                case StdoutEvent output:
                    return AddLines(OutputStream.Stdout, _stdout.Append(output.Text));

                case StderrEvent error:
                    return AddLines(OutputStream.Stderr, _stderr.Append(error.Text));

                case ExitEvent exit:
                    FlushPartialLines();
                    LastRun = new RunSummary(exit.ExitCode, exit.Reason, exit.Duration);
                    if (EndMessage(exit.Reason) is { } message)
                        AddLine(OutputStream.System, message);
                    return true;

                default:
                    return false;
            }
        }

        private static string? EndMessage(RunEndReason reason) => reason switch
        {
            RunEndReason.Stopped => "Stopped.",
            RunEndReason.TimedOut => "Stopped after reaching the time limit.",
            RunEndReason.OutputLimit => "Stopped after reaching the output limit.",
            RunEndReason.FailedToStart => "The run could not be started.",
            _ => null,
        };

        private void FlushPartialLines()
        {
            if (_stdout.Flush() is { } stdout)
                AddLine(OutputStream.Stdout, stdout);
            if (_stderr.Flush() is { } stderr)
                AddLine(OutputStream.Stderr, stderr);
        }

        private bool AddLines(OutputStream stream, IReadOnlyList<string> lines)
        {
            foreach (var line in lines)
                AddLine(stream, line);

            return lines.Count > 0;
        }

        private void AddLine(OutputStream stream, string text)
        {
            TotalLines++;
            _lines.Add(new OutputLine(TotalLines, stream, text));

            if (_lines.Count > MaxLines)
                _lines.RemoveRange(0, _lines.Count - MaxLines);
        }

        private void ResetOutput()
        {
            _lines.Clear();
            _stdout.Flush();
            _stderr.Flush();
            TotalLines = 0;
            LastRun = null;
            _stopRequested = false;
        }

        private async Task EndRunAsync(string? buildId)
        {
            try
            {
                if (_run is { } run)
                {
                    _run = null;
                    await run.DisposeAsync();
                }

                if (buildId is not null)
                    await toolchain.ReleaseBuildAsync(buildId);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not clean up after the run");
            }
        }

        private static async Task ObserveCancelledAsync(Task? task)
        {
            if (task is null)
                return;

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // The pending notification was no longer needed.
            }
        }

        private void NotifyStateChanged() => StateChanged?.Invoke();
    }
}
