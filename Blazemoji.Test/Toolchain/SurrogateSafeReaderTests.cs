using System.Text;
using Blazemoji.Toolchain;

namespace Blazemoji.Test.Toolchain
{
    public class SurrogateSafeReaderTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(5)]
        public async Task Chunks_never_end_in_half_an_emoji(int bufferSize)
        {
            const string text = "a😀😀b😀😀😀cd😀";
            var reader = new SurrogateSafeReader(new StringReader(text), bufferSize);

            var chunks = await ReadAllAsync(reader);

            string.Concat(chunks).ShouldBe(text);
            chunks.ShouldAllBe(chunk => chunk.Length > 0 && !char.IsHighSurrogate(chunk[chunk.Length - 1]));
        }

        [Fact]
        public async Task An_empty_stream_yields_nothing()
        {
            var reader = new SurrogateSafeReader(new StringReader(string.Empty), 16);

            (await reader.ReadChunkAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
        }

        [Fact]
        public async Task A_stream_that_ends_on_a_lone_high_surrogate_still_delivers_it()
        {
            var reader = new SurrogateSafeReader(new StringReader("ab\uD83D"), 16);

            var chunks = await ReadAllAsync(reader);

            string.Concat(chunks).ShouldBe("ab\uD83D");
        }

        private static async Task<List<string>> ReadAllAsync(SurrogateSafeReader reader)
        {
            var chunks = new List<string>();
            while (await reader.ReadChunkAsync(TestContext.Current.CancellationToken) is { } chunk)
                chunks.Add(chunk);

            return chunks;
        }
    }
}
