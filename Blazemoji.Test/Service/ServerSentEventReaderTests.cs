using System.Text;
using Blazemoji.Toolchain.Http;

namespace Blazemoji.Test.Service
{
    public class ServerSentEventReaderTests
    {
        private static async Task<List<ServerSentEvent>> ReadAsync(string text, int chunkSize = 4096)
        {
            var events = new List<ServerSentEvent>();
            await using var stream = new ChunkedStream(Encoding.UTF8.GetBytes(text), chunkSize);
            await foreach (var serverEvent in ServerSentEventReader.ReadAsync(stream, TestContext.Current.CancellationToken))
                events.Add(serverEvent);

            return events;
        }

        [Fact]
        public async Task An_event_has_an_id_a_name_and_data()
        {
            var events = await ReadAsync("id: 1\nevent: stdout\ndata: {\"text\":\"one\\n\"}\n\n");

            events.ShouldBe([new ServerSentEvent("1", "stdout", "{\"text\":\"one\\n\"}")]);
        }

        [Fact]
        public async Task Events_are_separated_by_blank_lines()
        {
            var events = await ReadAsync("id: 1\nevent: stdout\ndata: a\n\nid: 2\nevent: exit\ndata: b\n\n");

            events.Select(e => (e.Id, e.Name, e.Data)).ShouldBe([("1", "stdout", "a"), ("2", "exit", "b")]);
        }

        [Fact]
        public async Task Carriage_return_line_feed_endings_are_accepted()
        {
            var events = await ReadAsync("id: 1\r\nevent: stdout\r\ndata: a\r\n\r\n");

            events.ShouldBe([new ServerSentEvent("1", "stdout", "a")]);
        }

        [Fact]
        public async Task Several_data_lines_are_joined_with_newlines()
        {
            var events = await ReadAsync("event: stdout\ndata: a\ndata: b\n\n");

            events.ShouldHaveSingleItem().Data.ShouldBe("a\nb");
        }

        [Fact]
        public async Task Comments_and_unknown_fields_are_ignored()
        {
            var events = await ReadAsync(": keep-alive\n\nretry: 500\nevent: stdout\ndata: a\n\n");

            events.ShouldBe([new ServerSentEvent(null, "stdout", "a")]);
        }

        [Fact]
        public async Task A_value_without_a_space_after_the_colon_is_read_whole()
        {
            var events = await ReadAsync("event:stdout\ndata:a\n\n");

            events.ShouldBe([new ServerSentEvent(null, "stdout", "a")]);
        }

        [Fact]
        public async Task An_event_cut_off_by_the_end_of_the_stream_is_not_delivered()
        {
            var events = await ReadAsync("event: stdout\ndata: a\n\nevent: stdout\ndata: unfinished");

            events.ShouldHaveSingleItem().Data.ShouldBe("a");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(7)]
        public async Task Events_survive_being_split_at_any_byte(int chunkSize)
        {
            const string text = "id: 1\nevent: stdout\ndata: {\"text\":\"😀 héllo\\n\"}\n\nid: 2\nevent: exit\ndata: {\"exitCode\":0}\n\n";

            var events = await ReadAsync(text, chunkSize);

            events.ShouldBe(
            [
                new ServerSentEvent("1", "stdout", "{\"text\":\"😀 héllo\\n\"}"),
                new ServerSentEvent("2", "exit", "{\"exitCode\":0}"),
            ]);
        }

        /// <summary>
        /// Hands out its bytes a few at a time, the way a network stream can.
        /// </summary>
        private sealed class ChunkedStream(byte[] bytes, int chunkSize) : MemoryStream(bytes)
        {
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);

            public override int Read(byte[] buffer, int offset, int count) =>
                base.Read(buffer, offset, Math.Min(count, chunkSize));
        }
    }
}
