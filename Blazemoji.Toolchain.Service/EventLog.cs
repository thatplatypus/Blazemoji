using System.Runtime.CompilerServices;

namespace Blazemoji.Toolchain.Service
{
    public readonly record struct NumberedEvent(long Id, RunEvent Event);

    /// <summary>
    /// Everything one run has emitted, numbered from 1. A reader can start after any event and
    /// waits for the ones still to come, which is what lets a client connect late or reconnect.
    /// The log is bounded by the toolchain's output cap.
    /// </summary>
    public sealed class EventLog
    {
        private readonly Lock _gate = new();
        private readonly List<RunEvent> _events = [];
        private TaskCompletionSource _changed = NewSignal();

        public bool IsComplete { get; private set; }

        public void Append(RunEvent runEvent)
        {
            TaskCompletionSource changed;
            lock (_gate)
            {
                _events.Add(runEvent);
                if (runEvent is ExitEvent)
                    IsComplete = true;

                changed = _changed;
                _changed = NewSignal();
            }

            changed.TrySetResult();
        }

        /// <summary>
        /// Events with an id greater than <paramref name="afterId"/>, ending after the exit event.
        /// </summary>
        public async IAsyncEnumerable<NumberedEvent> ReadAsync(long afterId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var next = (int)Math.Max(afterId, 0);

            while (true)
            {
                RunEvent? runEvent = null;
                Task changed;
                bool complete;
                lock (_gate)
                {
                    if (next < _events.Count)
                        runEvent = _events[next];

                    changed = _changed.Task;
                    complete = IsComplete;
                }

                if (runEvent is not null)
                {
                    next++;
                    yield return new NumberedEvent(next, runEvent);
                    continue;
                }

                if (complete)
                    yield break;

                await changed.WaitAsync(cancellationToken);
            }
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
