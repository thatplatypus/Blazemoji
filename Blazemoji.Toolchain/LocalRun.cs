using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Blazemoji.Toolchain
{
    /// <summary>
    /// A running program. Output is read only as fast as the caller enumerates the events, so a
    /// slow reader slows the program down through its pipes instead of buffering without bound.
    /// </summary>
    internal sealed class LocalRun : IToolchainRun
    {
        private const int NoEndRequested = -1;

        // How long output may still arrive after the program itself has gone. Anything holding
        // the pipes open beyond this is a process the program left behind, not the program.
        private static readonly TimeSpan DrainGrace = TimeSpan.FromMilliseconds(250);

        private readonly Process? _process;
        private readonly string? _runDirectory;
        private readonly long _maxOutputBytes;
        private readonly bool _leadsProcessGroup;
        private readonly Action<LocalRun> _disposed;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly CancellationTokenSource _timeout = new();
        private readonly CancellationTokenRegistration _timeoutRegistration;
        private readonly SurrogateSafeReader? _stdout;
        private readonly SurrogateSafeReader? _stderr;
        private readonly Task _exited = Task.CompletedTask;

        private Task<string?>? _pendingStdout;
        private Task<string?>? _pendingStderr;
        private int _requestedEnd = NoEndRequested;
        private int _readerTaken;
        private int _disposeStarted;
        private int _readsAbandoned;
        private int _inputClosed;
        private volatile bool _processDisposed;

        private LocalRun(string runId, Process? process, string? runDirectory, TimeSpan timeout, long maxOutputBytes, bool leadsProcessGroup, Action<LocalRun> disposed)
        {
            RunId = runId;
            _process = process;
            _runDirectory = runDirectory;
            _maxOutputBytes = maxOutputBytes;
            _leadsProcessGroup = leadsProcessGroup;
            _disposed = disposed;

            if (process is null)
                return;

            _stdout = new SurrogateSafeReader(process.StandardOutput);
            _stderr = new SurrogateSafeReader(process.StandardError);
            _exited = process.WaitForExitAsync(CancellationToken.None);
            _timeoutRegistration = _timeout.Token.Register(() => End(RunEndReason.TimedOut));
            _timeout.CancelAfter(timeout);
        }

        public string RunId { get; }

        public static LocalRun Started(string runId, Process process, string runDirectory, TimeSpan timeout, long maxOutputBytes, bool leadsProcessGroup, Action<LocalRun> disposed) =>
            new(runId, process, runDirectory, timeout, maxOutputBytes, leadsProcessGroup, disposed);

        public static LocalRun FailedToStart(string runId, string? runDirectory, Action<LocalRun> disposed) =>
            new(runId, null, runDirectory, Timeout.InfiniteTimeSpan, 0, false, disposed);

        public async IAsyncEnumerable<RunEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _readerTaken, 1) == 1)
                throw new InvalidOperationException("A run supports a single reader.");

            if (_process is null || _stdout is null || _stderr is null)
            {
                yield return new ExitEvent(null, RunEndReason.FailedToStart, TimeSpan.Zero);
                yield break;
            }

            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var cancellation = cancellationToken.Register(() => cancelled.TrySetResult());

            // The pending reads are fields so that DisposeAsync can deal with them if the caller
            // stops enumerating early.
            _pendingStdout = _stdout.ReadChunkAsync(CancellationToken.None);
            _pendingStderr = _stderr.ReadChunkAsync(CancellationToken.None);
            long outputBytes = 0;
            Task? drained = null;

            while (_pendingStdout is not null || _pendingStderr is not null)
            {
                var completed = await Task.WhenAny(Pending(cancelled.Task, drained ?? _exited));
                cancellationToken.ThrowIfCancellationRequested();

                if (completed == _exited && drained is null)
                {
                    drained = Task.Delay(DrainGrace, CancellationToken.None);
                    continue;
                }

                if (completed == drained)
                {
                    // The program has gone but something it started still holds its pipes.
                    AbandonReads();
                    break;
                }

                RunEvent? output = null;
                if (completed == _pendingStdout)
                {
                    var text = await _pendingStdout;
                    _pendingStdout = text is null ? null : _stdout.ReadChunkAsync(CancellationToken.None);
                    output = text is null ? null : new StdoutEvent(text);
                }
                else if (completed == _pendingStderr)
                {
                    var text = await _pendingStderr;
                    _pendingStderr = text is null ? null : _stderr.ReadChunkAsync(CancellationToken.None);
                    output = text is null ? null : new StderrEvent(text);
                }

                // Once the run has been told to end, whatever is still in the pipes is discarded.
                if (output is null || EndRequested)
                    continue;

                outputBytes += Encoding.UTF8.GetByteCount(output is StdoutEvent stdout ? stdout.Text : ((StderrEvent)output).Text);
                yield return output;

                if (outputBytes > _maxOutputBytes)
                    End(RunEndReason.OutputLimit);
            }

            await _exited.WaitAsync(cancellationToken);
            _clock.Stop();
            EndStrays();

            var reason = EndRequested ? (RunEndReason)_requestedEnd : RunEndReason.Exited;
            yield return new ExitEvent(_process.ExitCode, reason, _clock.Elapsed);
        }

        public Task StopAsync()
        {
            End(RunEndReason.Stopped);
            return Task.CompletedTask;
        }

        public async Task WriteInputAsync(string text, bool endOfInput = false, CancellationToken cancellationToken = default)
        {
            if (_process is null || _processDisposed || _exited.IsCompleted || Volatile.Read(ref _inputClosed) == 1)
                throw new InvalidOperationException("The run is not accepting input.");

            try
            {
                if (text.Length > 0)
                {
                    await _process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken);
                    await _process.StandardInput.FlushAsync(cancellationToken);
                }

                if (endOfInput)
                    CloseInput();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                throw new InvalidOperationException("The run is not accepting input.", exception);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
                return;

            End(RunEndReason.Stopped);
            await _timeoutRegistration.DisposeAsync();
            _timeout.Dispose();

            if (_process is not null)
            {
                await _exited;
                EndStrays();
                CloseInput();
                AbandonReads();
                await ObserveAsync(_pendingStdout);
                await ObserveAsync(_pendingStderr);
                _processDisposed = true;
                _process.Dispose();
            }

            _disposed(this);
        }

        internal string? RunDirectory => _runDirectory;

        private bool EndRequested => Volatile.Read(ref _requestedEnd) != NoEndRequested;

        private void End(RunEndReason reason)
        {
            if (_process is null || _processDisposed || _exited.IsCompleted)
                return;

            if (Interlocked.CompareExchange(ref _requestedEnd, (int)reason, NoEndRequested) == NoEndRequested)
                ProcessRunner.Kill(_process, _leadsProcessGroup);
        }

        /// <summary>
        /// Ends anything the program started that has outlived it. Only possible when the
        /// program was given a process group of its own.
        /// </summary>
        private void EndStrays()
        {
            if (_process is not null && !_processDisposed && _leadsProcessGroup)
                ProcessRunner.Kill(_process, wholeGroup: true);
        }

        /// <summary>
        /// Closes this side of the pipes, which makes any read still waiting on them finish.
        /// </summary>
        private void AbandonReads()
        {
            if (_process is null || Interlocked.Exchange(ref _readsAbandoned, 1) == 1)
                return;

            _process.StandardOutput.Dispose();
            _process.StandardError.Dispose();
        }

        private void CloseInput()
        {
            if (_process is null || Interlocked.Exchange(ref _inputClosed, 1) == 1)
                return;

            try
            {
                _process.StandardInput.Dispose();
            }
            catch (IOException)
            {
                // The program closed its end first.
            }
        }

        private IEnumerable<Task> Pending(Task cancelled, Task exitedOrDrained)
        {
            if (_pendingStdout is not null)
                yield return _pendingStdout;
            if (_pendingStderr is not null)
                yield return _pendingStderr;
            yield return cancelled;
            yield return exitedOrDrained;
        }

        private static async Task ObserveAsync(Task<string?>? pendingRead)
        {
            if (pendingRead is null)
                return;

            try
            {
                await pendingRead;
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
                // The pipe was closed underneath a read that nobody is waiting for any more.
            }
        }
    }
}
