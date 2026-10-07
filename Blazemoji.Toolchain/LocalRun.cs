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

        private readonly Process? _process;
        private readonly string? _runDirectory;
        private readonly long _maxOutputBytes;
        private readonly Action<LocalRun> _disposed;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly CancellationTokenSource _timeout = new();
        private readonly CancellationTokenRegistration _timeoutRegistration;
        private readonly SurrogateSafeReader? _stdout;
        private readonly SurrogateSafeReader? _stderr;

        private Task<string?>? _pendingStdout;
        private Task<string?>? _pendingStderr;
        private int _requestedEnd = NoEndRequested;
        private int _readerTaken;
        private int _disposeStarted;

        private LocalRun(string runId, Process? process, string? runDirectory, TimeSpan timeout, long maxOutputBytes, Action<LocalRun> disposed)
        {
            RunId = runId;
            _process = process;
            _runDirectory = runDirectory;
            _maxOutputBytes = maxOutputBytes;
            _disposed = disposed;

            if (process is null)
                return;

            _stdout = new SurrogateSafeReader(process.StandardOutput);
            _stderr = new SurrogateSafeReader(process.StandardError);
            _timeoutRegistration = _timeout.Token.Register(() => End(RunEndReason.TimedOut));
            _timeout.CancelAfter(timeout);
        }

        public string RunId { get; }

        public static LocalRun Started(string runId, Process process, string runDirectory, TimeSpan timeout, long maxOutputBytes, Action<LocalRun> disposed) =>
            new(runId, process, runDirectory, timeout, maxOutputBytes, disposed);

        public static LocalRun FailedToStart(string runId, string? runDirectory, Action<LocalRun> disposed) =>
            new(runId, null, runDirectory, Timeout.InfiniteTimeSpan, 0, disposed);

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

            // The pending reads are fields so that DisposeAsync can wait for them if the caller
            // stops enumerating early.
            _pendingStdout = _stdout.ReadChunkAsync(CancellationToken.None);
            _pendingStderr = _stderr.ReadChunkAsync(CancellationToken.None);
            long outputBytes = 0;

            while (_pendingStdout is not null || _pendingStderr is not null)
            {
                var completed = await Task.WhenAny(Pending(cancelled.Task));
                cancellationToken.ThrowIfCancellationRequested();

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

            await _process.WaitForExitAsync(cancellationToken);
            _clock.Stop();

            var reason = EndRequested ? (RunEndReason)_requestedEnd : RunEndReason.Exited;
            yield return new ExitEvent(_process.ExitCode, reason, _clock.Elapsed);
        }

        public Task StopAsync()
        {
            End(RunEndReason.Stopped);
            return Task.CompletedTask;
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
                await ObserveAsync(_pendingStdout);
                await ObserveAsync(_pendingStderr);
                await _process.WaitForExitAsync(CancellationToken.None);
                _process.Dispose();
            }

            _disposed(this);
        }

        internal string? RunDirectory => _runDirectory;

        private bool EndRequested => Volatile.Read(ref _requestedEnd) != NoEndRequested;

        private void End(RunEndReason reason)
        {
            if (_process is null || _process.HasExited)
                return;

            if (Interlocked.CompareExchange(ref _requestedEnd, (int)reason, NoEndRequested) == NoEndRequested)
                ProcessRunner.KillTree(_process);
        }

        private IEnumerable<Task> Pending(Task cancelled)
        {
            if (_pendingStdout is not null)
                yield return _pendingStdout;
            if (_pendingStderr is not null)
                yield return _pendingStderr;
            yield return cancelled;
        }

        private static async Task ObserveAsync(Task<string?>? pendingRead)
        {
            if (pendingRead is null)
                return;

            try
            {
                await pendingRead;
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The pipe closed underneath a read that nobody is waiting for any more.
            }
        }
    }
}
