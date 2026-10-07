using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Service;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Blazemoji.Test.Service
{
    /// <summary>
    /// A finished run stays readable for a while. These pin that what is kept has a ceiling,
    /// whatever the rate at which runs finish.
    /// </summary>
    public class RunRegistryTests
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);

        private int _nextId;

        private static RunRegistry Create(int maxRetainedRuns = 100, long maxRetainedBytes = long.MaxValue) =>
            new(Options.Create(new ToolchainServiceOptions
            {
                MaxConcurrentRuns = 100,
                MaxRetainedRuns = maxRetainedRuns,
                MaxRetainedBytes = maxRetainedBytes,
            }));

        private RunSession Started(RunRegistry registry, int outputCharacters = 0)
        {
            var run = Substitute.For<IToolchainRun>();
            run.RunId.Returns($"run-{++_nextId}");
            registry.TryReserve().ShouldBeTrue();

            var session = registry.Add(run);
            if (outputCharacters > 0)
                session.Log.Append(new StdoutEvent(new string('x', outputCharacters)));

            return session;
        }

        private RunSession Ended(RunRegistry registry, int secondsAfterStart, int outputCharacters = 0)
        {
            var session = Started(registry, outputCharacters);
            registry.MarkEnded(session, Start.AddSeconds(secondsAfterStart));
            return session;
        }

        [Fact]
        public void Ended_runs_beyond_the_count_that_may_be_kept_are_dropped_oldest_first()
        {
            var registry = Create(maxRetainedRuns: 2);
            var first = Ended(registry, 1);
            var second = Ended(registry, 2);
            var third = Ended(registry, 3);

            var dropped = registry.RemoveOverBudget();

            dropped.ShouldBe([first]);
            registry.Find(first.RunId).ShouldBeNull();
            registry.Find(second.RunId).ShouldBe(second);
            registry.Find(third.RunId).ShouldBe(third);
        }

        [Fact]
        public void Ended_runs_beyond_the_memory_that_may_be_kept_are_dropped_oldest_first()
        {
            var registry = Create(maxRetainedBytes: 5000);
            var first = Ended(registry, 1, outputCharacters: 1000);
            var second = Ended(registry, 2, outputCharacters: 1000);
            var third = Ended(registry, 3, outputCharacters: 1000);

            var dropped = registry.RemoveOverBudget();

            // A character is two bytes in memory, so three runs hold over 6000.
            dropped.ShouldBe([first]);
            registry.Find(second.RunId).ShouldBe(second);
            registry.Find(third.RunId).ShouldBe(third);
        }

        [Fact]
        public void Nothing_is_dropped_while_what_is_kept_is_inside_the_budget()
        {
            var registry = Create(maxRetainedRuns: 3, maxRetainedBytes: 100_000);
            Ended(registry, 1, outputCharacters: 1000);
            Ended(registry, 2, outputCharacters: 1000);
            Ended(registry, 3, outputCharacters: 1000);

            registry.RemoveOverBudget().ShouldBeEmpty();
        }

        [Fact]
        public void A_run_that_is_still_going_is_never_dropped_to_make_room()
        {
            var registry = Create(maxRetainedRuns: 1, maxRetainedBytes: 100);
            var running = Started(registry, outputCharacters: 5000);
            var ended = Ended(registry, 1, outputCharacters: 10);
            var endedLater = Ended(registry, 2, outputCharacters: 10);

            var dropped = registry.RemoveOverBudget();

            dropped.ShouldBe([ended]);
            registry.Find(running.RunId).ShouldBe(running);
            registry.Find(endedLater.RunId).ShouldBe(endedLater);
        }

        [Fact]
        public void The_run_that_ended_last_is_kept_even_when_it_alone_is_over_the_budget()
        {
            var registry = Create(maxRetainedBytes: 100);
            var only = Ended(registry, 1, outputCharacters: 5000);

            registry.RemoveOverBudget().ShouldBeEmpty();
            registry.Find(only.RunId).ShouldBe(only);
        }

        [Fact]
        public void Many_small_events_count_for_more_than_their_text()
        {
            var log = new EventLog();
            for (var i = 0; i < 1000; i++)
                log.Append(new StdoutEvent("x"));

            // 1000 one-character events hold far more than 2000 bytes: each is an object.
            log.ApproximateBytes.ShouldBeGreaterThan(30_000);
        }
    }
}
