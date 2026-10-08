using Blazemoji.Shared;

namespace Blazemoji.Test.Shared
{
    public class OnePassAtATimeTests
    {
        /// <summary>A pass that waits until it is told to finish, and counts how often it was started.</summary>
        private sealed class Passes
        {
            private readonly Queue<TaskCompletionSource> _waiting = new();

            public int Started { get; private set; }

            public Task PassAsync()
            {
                Started++;
                var pass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting.Enqueue(pass);
                return pass.Task;
            }

            public void FinishOne() => _waiting.Dequeue().SetResult();
        }

        [Fact]
        public async Task A_call_with_nothing_running_makes_one_pass()
        {
            var passes = 0;
            var work = new OnePassAtATime(() => { passes++; return Task.CompletedTask; });

            await work.RunAsync();

            passes.ShouldBe(1);
        }

        [Fact]
        public async Task A_call_during_a_pass_does_not_wait_and_one_more_pass_follows()
        {
            var passes = new Passes();
            var work = new OnePassAtATime(passes.PassAsync);
            var first = work.RunAsync();

            var second = work.RunAsync();

            second.IsCompletedSuccessfully.ShouldBeTrue("nothing waits behind a pass");
            passes.Started.ShouldBe(1, "and nothing runs beside it");

            passes.FinishOne();
            await WaitUntilAsync(() => passes.Started == 2);
            first.IsCompleted.ShouldBeFalse("the first caller is done when everything asked for is done");

            passes.FinishOne();
            await first;
            passes.Started.ShouldBe(2);
        }

        [Fact]
        public async Task However_many_calls_arrive_during_a_pass_one_more_pass_follows()
        {
            var passes = new Passes();
            var work = new OnePassAtATime(passes.PassAsync);
            var first = work.RunAsync();

            for (var call = 0; call < 10; call++)
                work.RunAsync().IsCompletedSuccessfully.ShouldBeTrue();

            passes.FinishOne();
            await WaitUntilAsync(() => passes.Started == 2);
            passes.FinishOne();
            await first;

            passes.Started.ShouldBe(2);
        }

        [Fact]
        public async Task A_call_during_the_extra_pass_asks_for_another()
        {
            var passes = new Passes();
            var work = new OnePassAtATime(passes.PassAsync);
            var first = work.RunAsync();
            work.RunAsync().IsCompletedSuccessfully.ShouldBeTrue();
            passes.FinishOne();
            await WaitUntilAsync(() => passes.Started == 2);

            work.RunAsync().IsCompletedSuccessfully.ShouldBeTrue();
            passes.FinishOne();
            await WaitUntilAsync(() => passes.Started == 3);
            passes.FinishOne();
            await first;

            passes.Started.ShouldBe(3);
        }

        [Fact]
        public async Task A_pass_that_fails_tells_its_caller_and_leaves_the_next_call_free_to_run()
        {
            var passes = 0;
            var work = new OnePassAtATime(() => ++passes == 1 ? throw new InvalidOperationException("no editor") : Task.CompletedTask);

            await Should.ThrowAsync<InvalidOperationException>(work.RunAsync);
            await work.RunAsync();

            passes.ShouldBe(2);
        }

        [Fact]
        public async Task It_is_not_left_stuck_where_work_that_is_posted_runs_at_once()
        {
            // What a Hermes window does: a continuation posted from the window's own thread is
            // run on the spot, inside whatever posted it. A SemaphoreSlim with a waiter is left
            // taken for good by that when the waiter has nothing to wait for. This has no waiters.
            var before = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new RunsPostedWorkAtOnce());
            try
            {
                var passes = new Passes();
                var finished = 0;
                var work = new OnePassAtATime(async () =>
                {
                    // Only the first pass has anything to wait for.
                    if (passes.Started == 0)
                        await passes.PassAsync();

                    finished++;
                });
                var first = work.RunAsync();
                var second = work.RunAsync();

                passes.FinishOne();

                first.IsCompletedSuccessfully.ShouldBeTrue();
                second.IsCompletedSuccessfully.ShouldBeTrue();
                finished.ShouldBe(2);

                await work.RunAsync();
                finished.ShouldBe(3, "a later call still gets its pass");
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(before);
            }
        }

        private sealed class RunsPostedWorkAtOnce : SynchronizationContext
        {
            public override void Post(SendOrPostCallback d, object? state)
            {
                var before = Current;
                SetSynchronizationContext(this);
                try
                {
                    d(state);
                }
                finally
                {
                    SetSynchronizationContext(before);
                }
            }
        }

        private static async Task WaitUntilAsync(Func<bool> reached)
        {
            for (var attempt = 0; attempt < 200 && !reached(); attempt++)
                await Task.Delay(10, TestContext.Current.CancellationToken);

            reached().ShouldBeTrue();
        }
    }
}
