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
        private bool _server;
        private IReadOnlyDictionary<string, string> _sources = new Dictionary<string, string>();

        public RunStatus Status { get; private set; } = RunStatus.Idle;

        public IReadOnlyList<OutputLine> Lines => _lines;

        /// <summary>
        /// Lines produced by the current run, including those no longer kept in <see cref="Lines"/>.
        /// </summary>
        public long TotalLines { get; private set; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; private set; } = [];

        /// <summary>
        /// The entry file of the build that <see cref="Diagnostics"/> came from. A problem the
        /// compiler gave no file for belongs to it.
        /// </summary>
        public string DiagnosticsEntry { get; private set; } = string.Empty;

        /// <summary>
        /// The entry file's text as it was compiled.
        /// </summary>
        public string DiagnosticsSource => _sources.GetValueOrDefault(DiagnosticsEntry, string.Empty);

        /// <summary>
        /// A server program is running and can be sent requests.
        /// </summary>
        public bool ServerRunning => _server && Status == RunStatus.Running;

        public RunSummary? LastRun { get; private set; }

        public event Action? StateChanged;

        public event Action? DiagnosticsChanged;

        /// <summary>
        /// The text of the file a problem is in, as it was compiled. Positions in a problem are
        /// only right against that text, not against what has been typed since.
        /// </summary>
        public string SourceOf(Diagnostic diagnostic) =>
            _sources.GetValueOrDefault(diagnostic.File) ?? DiagnosticsSource;

        public Task RunAsync(string code) =>
            RunAsync(new RunTarget(new Dictionary<string, string> { [EntryFile] = code }, EntryFile, Server: false));

        public async Task RunAsync(RunTarget target)
        {
            if (Status != RunStatus.Idle || _disposal.IsCancellationRequested)
                return;

            // A copy: the caller goes on editing its files while this build is looked at.
            var files = new Dictionary<string, string>(target.Files);

            ResetOutput();
            ClearDiagnostics();
            Status = RunStatus.Compiling;
            NotifyStateChanged();

            string? buildId = null;
            using var compileCancellation = CancellationTokenSource.CreateLinkedTokenSource(_disposal.Token);
            _compileCancellation = compileCancellation;

            try
            {
                var build = await toolchain.CompileAsync(new CompileRequest(files, target.Entry), compileCancellation.Token);

                Diagnostics = build.Diagnostics;
                DiagnosticsEntry = target.Entry;
                _sources = files;
                DiagnosticsChanged?.Invoke();

                if (!build.Ok || build.BuildId is null)
                    return;

                buildId = build.BuildId;
                _server = target.Server;
                Status = RunStatus.Running;
                NotifyStateChanged();

                _run = await toolchain.StartRunAsync(new RunRequest(buildId, Server: target.Server), _disposal.Token);

                // Stop may have been pressed while the program was being started.
                if (_stopRequested)
                    await _run.StopAsync();

                await CloseInputAsync(_run, _disposal.Token);

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
                _server = false;
                Status = RunStatus.Idle;
                NotifyStateChanged();
            }
        }

        /// <summary>
        /// Forgets the last build's diagnostics, for when the code they refer to is replaced.
        /// </summary>
        public void ClearDiagnostics()
        {
            if (Diagnostics.Count == 0 && _sources.Count == 0)
                return;

            Diagnostics = [];
            DiagnosticsEntry = string.Empty;
            _sources = new Dictionary<string, string>();
            DiagnosticsChanged?.Invoke();
        }

        /// <summary>
        /// Sends an HTTP request to the server program that is running.
        /// </summary>
        public async Task<ProgramResponse> SendHttpAsync(ProgramRequest request, CancellationToken cancellationToken = default)
        {
            if (_run is not { } run)
                return ProgramResponse.Without(ProgramResponseOutcome.Ended);

            return _server
                ? await run.SendHttpAsync(request, cancellationToken)
                : ProgramResponse.Without(ProgramResponseOutcome.NotAServer);
        }

        public async Task StopAsync()
        {
            _stopRequested = true;

            try
            {
                if (_run is { } run)
                    await run.StopAsync();
                else if (_compileCancellation is { } compiling)
                    await compiling.CancelAsync();
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
            {
                // Stop is a button. A toolchain that cannot be reached must not take the page down.
                logger.LogError(exception, "The run could not be asked to stop");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposal.IsCancellationRequested)
                return;

            await _disposal.CancelAsync();

            if (_run is { } run)
                await run.DisposeAsync();
        }

        /// <summary>
        /// The page has no way to type input yet, so a program that reads it must see the end
        /// of its input instead of waiting for the time limit.
        /// </summary>
        private static async Task CloseInputAsync(IToolchainRun run, CancellationToken cancellationToken)
        {
            try
            {
                await run.WriteInputAsync(string.Empty, endOfInput: true, cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // The program has already ended, or never started; its exit event says which.
            }
        }

        private async Task ConsumeAsync(IToolchainRun run, CancellationToken cancellationToken)
        {
            using var flushCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var events = run.ReadEventsAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

            Task<bool>? next = null;
            Task? flushDue = null;

            try
            {
                next = events.MoveNextAsync().AsTask();
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

                // An enumerator must not be disposed while a move is still in flight, which is
                // where things stand when the session closes between two events.
                await ObserveCancelledAsync(next);
                await events.DisposeAsync();
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
            RunEndReason.Idle => "Stopped after going too long without a request.",
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
