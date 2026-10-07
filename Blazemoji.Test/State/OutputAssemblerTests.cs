using Blazemoji.Shared.State;

namespace Blazemoji.Test.State
{
    public class OutputAssemblerTests
    {
        [Fact]
        public void A_chunk_with_two_lines_gives_two_lines()
        {
            var assembler = new OutputAssembler(100);

            assembler.Append("one\ntwo\n").ShouldBe(["one", "two"]);
            assembler.Flush().ShouldBeNull();
        }

        [Fact]
        public void A_line_split_across_chunks_is_joined()
        {
            var assembler = new OutputAssembler(100);

            assembler.Append("hel").ShouldBeEmpty();
            assembler.Append("lo\nwor").ShouldBe(["hello"]);
            assembler.Append("ld\n").ShouldBe(["world"]);
        }

        [Fact]
        public void A_carriage_return_before_the_newline_is_dropped()
        {
            var assembler = new OutputAssembler(100);

            assembler.Append("one\r\ntwo\r").ShouldBe(["one"]);
            assembler.Append("\n").ShouldBe(["two"]);
        }

        [Fact]
        public void Text_without_a_final_newline_waits_for_flush()
        {
            var assembler = new OutputAssembler(100);

            assembler.Append("prompt: ").ShouldBeEmpty();

            assembler.Flush().ShouldBe("prompt: ");
            assembler.Flush().ShouldBeNull();
        }

        [Fact]
        public void An_empty_line_is_a_line()
        {
            var assembler = new OutputAssembler(100);

            assembler.Append("a\n\nb\n").ShouldBe(["a", "", "b"]);
        }

        [Fact]
        public void A_line_longer_than_the_limit_is_cut_into_pieces()
        {
            var assembler = new OutputAssembler(4);

            assembler.Append("abcdefghij").ShouldBe(["abcd", "efgh"]);
            assembler.Append("k\n").ShouldBe(["ijk"]);
        }

        [Fact]
        public void A_newline_right_after_a_cut_does_not_add_an_empty_line()
        {
            var assembler = new OutputAssembler(4);

            assembler.Append("abcdefgh\nnext\n").ShouldBe(["abcd", "efgh", "next"]);
        }

        [Fact]
        public void A_cut_never_splits_an_emoji()
        {
            var assembler = new OutputAssembler(3);

            var pieces = assembler.Append("a😀😀😀\n").ToList();

            string.Concat(pieces).ShouldBe("a😀😀😀");
            pieces.ShouldAllBe(piece => !char.IsHighSurrogate(piece[piece.Length - 1]));
        }

        [Fact]
        public void An_empty_chunk_gives_nothing()
        {
            var assembler = new OutputAssembler(100);

            assembler.Append(string.Empty).ShouldBeEmpty();
        }
    }
}
