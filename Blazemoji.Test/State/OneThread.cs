using System.Collections.Concurrent;
using System.Diagnostics;

namespace Blazemoji.Test.State
{
    /// <summary>
    /// One thread that takes turns, as a circuit's or a window's does. What is posted
    /// waits its turn, and what waited on a task that ends on this thread goes on inside
    /// the call that ended it.
    /// </summary>
    internal sealed class OneThread : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Work, object? State)> _turns = [];
        private readonly int _owner = Environment.CurrentManagedThreadId;
        private readonly bool _postsAtOnce;

        private OneThread(bool postsAtOnce) => _postsAtOnce = postsAtOnce;

        // The desktop host's window thread runs what is posted from itself there and then
        // (Hermes 1.2.0, HermesSynchronizationContext.Post, when it is not busy). A circuit's
        // never does.
        public override void Post(SendOrPostCallback d, object? state)
        {
            if (_postsAtOnce && Environment.CurrentManagedThreadId == _owner)
                d(state);
            else
                _turns.Add((d, state));
        }

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        public override SynchronizationContext CreateCopy() => this;

        public static void Run(Func<Task> work, bool postsAtOnce = false)
        {
            var before = Current;
            var context = new OneThread(postsAtOnce);
            SetSynchronizationContext(context);
            try
            {
                var task = work();
                var waited = Stopwatch.StartNew();
                while (!task.IsCompleted)
                {
                    if (waited.Elapsed > TimeSpan.FromSeconds(60))
                        throw new TimeoutException("the work on the one thread never ended");

                    if (context._turns.TryTake(out var turn, TimeSpan.FromMilliseconds(20)))
                        turn.Work(turn.State);
                }

                task.GetAwaiter().GetResult();
            }
            finally
            {
                SetSynchronizationContext(before);
            }
        }
    }
}
