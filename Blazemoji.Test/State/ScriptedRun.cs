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

        public void Emit(RunEvent runEvent) => _events.Writer.TryWrite(runEvent).ShouldBeTrue();

        public void Exit(int? exitCode = 0, RunEndReason reason = RunEndReason.Exited)
        {
            Emit(new ExitEvent(exitCode, reason, TimeSpan.FromMilliseconds(120)));
            _events.Writer.Complete();
        }

        public IAsyncEnumerable<RunEvent> ReadEventsAsync(CancellationToken cancellationToken = default) =>
            _events.Reader.ReadAllAsync(cancellationToken);

        public Task StopAsync()
        {
            StopCalls++;
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
