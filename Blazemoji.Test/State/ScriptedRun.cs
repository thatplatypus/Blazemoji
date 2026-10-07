using System.Threading.Channels;
using Blazemoji.Toolchain;

namespace Blazemoji.Test.State
{
    /// <summary>
    /// A run whose events are supplied by the test.
    /// </summary>
    internal sealed class ScriptedRun : IToolchainRun
    {
        private readonly Channel<RunEvent> _events = Channel.CreateUnbounded<RunEvent>();

        public string RunId => "scripted";

        public int StopCalls { get; private set; }

        public bool Disposed { get; private set; }

        public List<(string Text, bool EndOfInput)> Input { get; } = [];

        public bool Ended { get; private set; }

        /// <summary>Makes <see cref="StopAsync"/> fail, as it does when the toolchain cannot be reached.</summary>
        public Exception? StopFailure { get; set; }

        public List<ProgramRequest> Requests { get; } = [];

        /// <summary>How the "program" answers. Without it the run is not a server.</summary>
        public Func<ProgramRequest, ProgramResponse>? Respond { get; set; }

        public void Emit(RunEvent runEvent) => _events.Writer.TryWrite(runEvent).ShouldBeTrue();

        public void Exit(int? exitCode = 0, RunEndReason reason = RunEndReason.Exited)
        {
            Emit(new ExitEvent(exitCode, reason, TimeSpan.FromMilliseconds(120)));
            _events.Writer.Complete();
            Ended = true;
        }

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(CancellationToken cancellationToken = default) =>
            _events.Reader.ReadAllAsync(cancellationToken);

        public Task WriteInputAsync(string text, bool endOfInput = false, CancellationToken cancellationToken = default)
        {
            if (Ended)
                throw new InvalidOperationException("The run has ended.");

            Input.Add((text, endOfInput));
            return Task.CompletedTask;
        }

        public Task<ProgramResponse> SendHttpAsync(ProgramRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Respond?.Invoke(request) ?? ProgramResponse.Without(ProgramResponseOutcome.NotAServer));
        }

        public Task StopAsync()
        {
            StopCalls++;
            if (StopFailure is not null)
                return Task.FromException(StopFailure);

            if (!Ended)
                Exit(137, RunEndReason.Stopped);

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
