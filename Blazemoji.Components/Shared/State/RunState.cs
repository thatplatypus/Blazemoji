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

        /// <summary>
        /// The longest line that can be sent to a program. The box it is typed in holds no
        /// more, but a page can be made to send anything, and what is sent is kept here.
        /// </summary>
        public const int MaxInputLength = 2000;

        /// <summary>
        /// How many lines may be waiting for a program to read them. One that never reads
        /// stops taking them once its pipe is full, and the rest would otherwise pile up here.
        /// </summary>
        public const int MaxPendingInputs = 50;

        private const string EntryFile = "main.🍇";

        // Output can arrive thousands of times a second. Subscribers are told at most this often.
        private static readonly TimeSpan NotifyInterval = TimeSpan.FromMilliseconds(50);

        private readonly List<OutputLine> _lines = [];
        private readonly OutputAssembler _stdout = new(MaxLineLength);
        private readonly OutputAssembler _stderr = new(MaxLineLength);
        private readonly CancellationTokenSource _disposal = new();

        private IToolchainRun? _run;
        private bool _inputOpen;
        private bool _inputEnded;
        private InputQueue _inputQueue = new();
        private CancellationTokenSource? _compileCancellation;
        private bool _stopRequested;
        private bool _server;
        private int _checks;
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
        /// True when <see cref="Diagnostics"/> are what Run found, false when they are from a
        /// check made while typing. A failed Run is worth interrupting someone for; a mistake
        /// half typed is not.
        /// </summary>
        public bool DiagnosticsAreFromARun { get; private set; }

        /// <summary>
        /// A server program is running and can be sent requests.
        /// </summary>
        public bool ServerRunning => _server && Status == RunStatus.Running;

        /// <summary>True while a program is running that has been told, or has said, that its input is over.</summary>
        public bool InputHasEnded => _inputEnded && Status == RunStatus.Running;

        /// <summary>True while a program is running whose input has not been ended: a line can be sent to it.</summary>
        public bool AcceptsInput => _inputOpen && _run is not null && Status == RunStatus.Running;

        /// <summary>
        /// What the program has printed since its last newline, or null when that is nothing.
        /// A program that asks a question and waits for the answer often ends the question
        /// with a space and not a newline, and it has to be seen before it can be answered.
        /// </summary>
        public string? UnfinishedOutput => _stdout.Pending;

        /// <summary>The same for what the program has written to standard error.</summary>
        public string? UnfinishedError => _stderr.Pending;

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

            // Whatever a check of the typing finds from here on is older than this build.
            _checks++;

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
                DiagnosticsAreFromARun = true;
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

                // Its input is left open for lines to be sent to it. A program that reads and
                // is sent none waits until it is stopped or reaches its time limit. The lines
                // wait in a queue of this run's own: one still on its way to an earlier
                // program must not hold these up.
                _inputQueue = new InputQueue();
                _inputEnded = false;
                _inputOpen = true;
                NotifyStateChanged();

                await ConsumeAsync(_run, _disposal.Token);
            }
            catch (OperationCanceledException) when (_disposal.IsCancellationRequested)
            {
                // The circuit is going away; there is nobody left to tell.
            }
            catch (OperationCanceledException) when (_stopRequested)
            {
                FlushPartialLines();
                AddLine(OutputStream.System, "Stopped.");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The run failed");
                FlushPartialLines();
                AddLine(OutputStream.System, "The run could not be completed. The details are in the server log.");
            }
            finally
            {
                _compileCancellation = null;
                _inputOpen = false;
                _inputEnded = false;
                FlushPartialLines();
                await EndRunAsync(buildId);
                _server = false;
                Status = RunStatus.Idle;
                NotifyStateChanged();
            }
        }

        /// <summary>
        /// Compiles for the problems alone, as someone types: nothing is run and the output is
        /// left as it is. It stands aside while a program is being built, whose own problems
        /// are about to arrive, and carries on while one runs: a server is edited while it is
        /// up. A later check replaces an earlier one, and a check that never reached the
        /// compiler changes nothing.
        /// </summary>
        public async Task CheckAsync(RunTarget target)
        {
            if (Status == RunStatus.Compiling || _disposal.IsCancellationRequested)
                return;

            var check = ++_checks;
            var files = new Dictionary<string, string>(target.Files);

            CompileResult result;
            try
            {
                result = await toolchain.CompileAsync(new CompileRequest(files, target.Entry, CheckOnly: true), _disposal.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "A check of the code being typed failed");
                return;
            }

            if (check != _checks || Status == RunStatus.Compiling || !result.ReachedCompiler)
                return;

            var changed = !result.Diagnostics.SequenceEqual(Diagnostics);
            Diagnostics = result.Diagnostics;
            DiagnosticsEntry = target.Entry;
            DiagnosticsAreFromARun = false;
            _sources = files;
            if (changed)
                DiagnosticsChanged?.Invoke();
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
        /// Sends a line to the running program's input, with the newline that ends it, and
        /// shows it as typed. Does nothing when no program is taking input.
        /// </summary>
        public Task SendInputAsync(string text)
        {
            if (!AcceptsInput || _run is not { } run)
                return Task.CompletedTask;

            if (text.Length > MaxInputLength)
                return Refuse(FormattableString.Invariant($"A line of input can be at most {MaxInputLength:N0} characters."));

            if (_inputQueue.Waiting >= MaxPendingInputs)
                return Refuse("The program has not read the lines it was already sent.");

            // Whatever the program has printed on this line so far is its question, and this
            // is the answer: shown on one line as a terminal shows them, and before the write,
            // so that the program's reply cannot arrive above it. A question is usually asked
            // on standard output. Unfinished text on the other stream becomes a line above.
            var question = _stdout.Flush();
            var aside = _stderr.Flush();
            if (question is null && aside is not null)
            {
                AddLine(OutputStream.Stderr, aside, text);
            }
            else
            {
                if (aside is not null)
                    AddLine(OutputStream.Stderr, aside);

                AddLine(OutputStream.Stdout, question ?? string.Empty, text);
            }

            NotifyStateChanged();

            return InTurn(_inputQueue, queue => WriteInputAsync(run, queue, text + "\n", endOfInput: false));
        }

        /// <summary>Tells the running program that it has had all of its input. Does nothing when no program is taking input.</summary>
        public Task EndInputAsync()
        {
            if (!AcceptsInput || _run is not { } run)
                return Task.CompletedTask;

            _inputOpen = false;
            _inputEnded = true;
            NotifyStateChanged();

            return InTurn(_inputQueue, queue => WriteInputAsync(run, queue, string.Empty, endOfInput: true));
        }

        private Task Refuse(string why)
        {
            AddLine(OutputStream.System, why);
            NotifyStateChanged();
            return Task.CompletedTask;
        }

        /// <summary>One write at a time, in the order they were asked for: two lines sent quickly must not overtake each other.</summary>
        private static Task InTurn(InputQueue queue, Func<InputQueue, Task> write)
        {
            queue.Waiting++;
            var before = queue.Last;
            return queue.Last = AfterAsync(before, queue, write);
        }

        private static async Task AfterAsync(Task before, InputQueue queue, Func<InputQueue, Task> write)
        {
            try
            {
                await before;
                await write(queue);
            }
            finally
            {
                queue.Waiting--;
            }
        }

        private async Task WriteInputAsync(IToolchainRun run, InputQueue queue, string text, bool endOfInput)
        {
            // Whether this is still the program on the page. A line can still be on its way
            // to one that has ended, and what becomes of it is no news for the next.
            bool Current() => ReferenceEquals(run, _run) && ReferenceEquals(queue, _inputQueue);

            try
            {
                await run.WriteInputAsync(text, endOfInput, _disposal.Token);
            }
            catch (InvalidOperationException)
            {
                // The program has ended, or its input has; its exit event says which.
                if (Current() && !_inputEnded)
                {
                    _inputOpen = false;
                    _inputEnded = true;
                    NotifyStateChanged();
                }
            }
            catch (OperationCanceledException) when (_disposal.IsCancellationRequested)
            {
                // The circuit is going away; there is nobody left to tell.
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Input could not be sent to the program");
                if (!Current())
                    return;

                // The program was not told its input is over, and is still waiting to be.
                if (endOfInput)
                {
                    _inputOpen = true;
                    _inputEnded = false;
                }

                AddLine(OutputStream.System, "The input could not be sent.");
                NotifyStateChanged();
            }
        }

        /// <summary>The lines on their way to one program, which go one at a time.</summary>
        private sealed class InputQueue
        {
            public Task Last { get; set; } = Task.CompletedTask;

            public int Waiting { get; set; }
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
                // Text with no newline yet makes no line, and is still something to show.
                case StdoutEvent output:
                    AddLines(OutputStream.Stdout, _stdout.Append(output.Text));
                    return output.Text.Length > 0;

                case StderrEvent error:
                    AddLines(OutputStream.Stderr, _stderr.Append(error.Text));
                    return error.Text.Length > 0;

                case ExitEvent exit:
                    _inputOpen = false;
                    _inputEnded = false;
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

        private void AddLine(OutputStream stream, string text, string? typed = null)
        {
            TotalLines++;
            _lines.Add(new OutputLine(TotalLines, stream, text, typed));

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
