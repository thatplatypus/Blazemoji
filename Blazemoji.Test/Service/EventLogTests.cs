using Blazemoji.Toolchain;
using Blazemoji.Toolchain.Service;

namespace Blazemoji.Test.Service
{
    public class EventLogTests
    {
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static async Task<List<NumberedEvent>> ReadAllAsync(EventLog log, long afterId = 0)
        {
            var events = new List<NumberedEvent>();
            await foreach (var numbered in log.ReadAsync(afterId, Cancellation))
                events.Add(numbered);

            return events;
        }

        [Fact]
        public async Task Events_are_numbered_from_one_in_the_order_they_were_added()
        {
            var log = new EventLog();
            log.Append(new StdoutEvent("a"));
            log.Append(new StderrEvent("b"));
            log.Append(new ExitEvent(0, RunEndReason.Exited, TimeSpan.Zero));

            var events = await ReadAllAsync(log);

            events.Select(e => e.Id).ShouldBe([1, 2, 3]);
            events.Select(e => e.Event).ShouldBe([new StdoutEvent("a"), new StderrEvent("b"), new ExitEvent(0, RunEndReason.Exited, TimeSpan.Zero)]);
        }

        [Fact]
        public async Task A_reader_can_start_after_an_event_it_has_already_seen()
        {
            var log = new EventLog();
            log.Append(new StdoutEvent("a"));
            log.Append(new StdoutEvent("b"));
            log.Append(new ExitEvent(0, RunEndReason.Exited, TimeSpan.Zero));

            var events = await ReadAllAsync(log, afterId: 2);

            events.ShouldHaveSingleItem().Id.ShouldBe(3);
        }

        [Fact]
        public async Task A_reader_waits_for_events_that_have_not_happened_yet()
        {
            var log = new EventLog();
            var reading = ReadAllAsync(log);
            reading.IsCompleted.ShouldBeFalse();

            log.Append(new StdoutEvent("late"));
            log.Append(new ExitEvent(0, RunEndReason.Exited, TimeSpan.Zero));

            (await reading).Count.ShouldBe(2);
        }

        [Fact]
        public async Task Two_readers_each_get_every_event()
        {
            var log = new EventLog();
            var first = ReadAllAsync(log);
            var second = ReadAllAsync(log);

            log.Append(new StdoutEvent("a"));
            log.Append(new ExitEvent(0, RunEndReason.Exited, TimeSpan.Zero));

            (await first).Count.ShouldBe(2);
            (await second).Count.ShouldBe(2);
        }

        [Fact]
        public void The_log_knows_when_the_run_has_ended()
        {
            var log = new EventLog();
            log.Append(new StdoutEvent("a"));
            log.IsComplete.ShouldBeFalse();

            log.Append(new ExitEvent(0, RunEndReason.Exited, TimeSpan.Zero));

            log.IsComplete.ShouldBeTrue();
        }

        [Fact]
        public async Task A_reader_that_is_cancelled_stops_waiting()
        {
            var log = new EventLog();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
            var reading = Task.Run(async () =>
            {
                await foreach (var _ in log.ReadAsync(0, cancellation.Token))
                {
                }
            }, Cancellation);

            await cancellation.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(() => reading);
        }
    }
}
