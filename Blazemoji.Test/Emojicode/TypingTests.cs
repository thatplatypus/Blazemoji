using Blazemoji.Emojicode.Editing;

namespace Blazemoji.Test.Emojicode
{
    public class TypingTests
    {
        /// <summary>
        /// Types into a text and shows what is left. In both texts <c>|</c> is the cursor and
        /// <c>[</c> to <c>]</c> a selection.
        /// </summary>
        private static string Type(string typed, string marked)
        {
            var selectionStart = marked.IndexOfAny(['|', '[']);
            var selectionEnd = marked[selectionStart] == '|' ? selectionStart : marked.IndexOf(']') - 1;
            var text = marked.Replace("|", string.Empty).Replace("[", string.Empty).Replace("]", string.Empty);
            var endOfLine = text.IndexOf('\n', selectionEnd);
            var around = new TextAroundCursor(
                text[..selectionStart],
                text[selectionStart..selectionEnd],
                text[selectionEnd..(endOfLine < 0 ? text.Length : endOfLine)]);

            var edit = Typing.For(typed, around);

            var from = selectionStart - edit.RemoveBefore;
            var changed = text[..from] + edit.Text + text[(selectionEnd + edit.RemoveAfter)..];
            return edit.SelectionStart == edit.SelectionEnd
                ? changed.Insert(from + edit.SelectionStart, "|")
                : changed.Insert(from + edit.SelectionEnd, "]").Insert(from + edit.SelectionStart, "[");
        }

        [Theory]
        [InlineData("🍇", "🏁 |", "🏁 🍇|🍉")]
        [InlineData("🤜", "↪️ |", "↪️ 🤜|🤛")]
        [InlineData("🔤", "😀 |", "😀 🔤|🔤")]
        [InlineData("🍿", "➡️ |", "➡️ 🍿|🍆")]
        [InlineData("🐚", "🍨|", "🍨🐚|🍆")]
        public void An_opener_brings_its_closer_and_leaves_the_cursor_between_them(string typed, string before, string after)
        {
            Type(typed, before).ShouldBe(after);
        }

        [Theory]
        [InlineData("🍇", "🏁 | 😀", "🏁 🍇|🍉 😀", "a space follows")]
        [InlineData("🤜", "🤜|🤛", "🤜🤜|🤛🤛", "a closer follows")]
        [InlineData("🤜", "🍇 |🍉", "🍇 🤜|🤛🍉", "another kind of closer follows")]
        [InlineData("🔤", "😀 |❗️", "😀 🔤|🔤❗️", "the end of a call follows")]
        [InlineData("🤜", "🍿 |🍆", "🍿 🤜|🤛🍆", "the end of a list follows")]
        public void An_opener_is_closed_when_what_follows_could_come_after_the_closer(string typed, string before, string after, string because)
        {
            Type(typed, before).ShouldBe(after, because);
        }

        [Theory]
        [InlineData("🍇", "🏁 |x", "🏁 🍇|x")]
        [InlineData("🤜", "|a ➕ b", "🤜|a ➕ b")]
        [InlineData("🔤", "😀 |🔤Hello🔤", "😀 🔤|🔤Hello🔤")]
        public void An_opener_typed_in_front_of_something_is_left_alone(string typed, string before, string after)
        {
            Type(typed, before).ShouldBe(after);
        }

        [Theory]
        [InlineData("🍉", "🏁 🍇|🍉", "🏁 🍇🍉|")]
        [InlineData("🤛", "🤜a ➕ b|🤛", "🤜a ➕ b🤛|")]
        [InlineData("🍆", "🍿 1 2|🍆", "🍿 1 2🍆|")]
        [InlineData("🍆", "🍨🐚🔡|🍆", "🍨🐚🔡🍆|")]
        public void A_closer_that_is_already_there_is_stepped_over(string typed, string before, string after)
        {
            Type(typed, before).ShouldBe(after);
        }

        [Theory]
        [InlineData("🤛", "🤜a ➕ b|", "🤜a ➕ b🤛|")]
        [InlineData("🤛", "🤜a|🍉", "🤜a🤛|🍉")]
        [InlineData("🍉", "🍇 x |", "🍇 x 🍉|")]
        [InlineData("🍆", "🍿 1 2|", "🍿 1 2🍆|")]
        public void A_closer_with_no_twin_after_it_is_typed(string typed, string before, string after)
        {
            Type(typed, before).ShouldBe(after);
        }

        [Fact]
        public void The_quote_that_ends_a_string_is_stepped_over_when_it_is_already_there()
        {
            Type("🔤", "😀 🔤Hello|🔤❗️").ShouldBe("😀 🔤Hello🔤|❗️");
        }

        [Fact]
        public void The_quote_that_ends_a_string_is_typed_when_the_string_is_still_open()
        {
            Type("🔤", "😀 🔤Hello|").ShouldBe("😀 🔤Hello🔤|");
        }

        [Fact]
        public void A_quote_after_an_escaped_escape_ends_the_string_as_any_other_does()
        {
            // ❌❌ is one ❌ written out, so the quote after it is not escaped.
            Type("🔤", "😀 🔤a❌❌|🔤").ShouldBe("😀 🔤a❌❌🔤|");
            Type("🔤", "😀 🔤a❌❌❌|🔤").ShouldBe("😀 🔤a❌❌❌🔤|🔤");
        }

        [Fact]
        public void The_marks_of_a_block_comment_are_typed_together_and_the_cursor_is_left_inside()
        {
            const string both = "💭🔜\r\n🔚💭";

            Type(both, "a\n|\nb").ShouldBe("a\n💭🔜|\r\n🔚💭\nb");
            Typing.For(both).ShouldBe(new TypingEdit(0, 0, both, 4, 4));
            Typing.DependsOnWhatIsAround(both).ShouldBeFalse();
        }

        [Fact]
        public void Text_typed_without_looking_around_goes_in_with_the_cursor_after_it()
        {
            Typing.For("😀").ShouldBe(new TypingEdit(0, 0, "😀", 2, 2));
        }

        [Fact]
        public void An_escaped_quote_is_typed_even_in_front_of_the_closing_one()
        {
            Type("🔤", "😀 🔤She said ❌|🔤").ShouldBe("😀 🔤She said ❌🔤|🔤");
        }

        [Theory]
        [InlineData("🍇", "😀 🔤a|", "😀 🔤a🍇|")]
        [InlineData("🤜", "😀 🔤a|🔤", "😀 🔤a🤜|🔤")]
        [InlineData("🍇", "💭 a|", "💭 a🍇|")]
        [InlineData("🔤", "💭 a|", "💭 a🔤|")]
        [InlineData("🍇", "💭🔜 a|\n🔚💭", "💭🔜 a🍇|\n🔚💭")]
        public void Nothing_is_paired_inside_a_string_or_a_comment(string typed, string before, string after)
        {
            Type(typed, before).ShouldBe(after);
        }

        [Theory]
        [InlineData("🍉", "😀 🔤a|🍉🔤", "😀 🔤a🍉|🍉🔤")]
        [InlineData("🤛", "💭 a|🤛", "💭 a🤛|🤛")]
        public void Nothing_is_stepped_over_inside_a_string_or_a_comment(string typed, string before, string after)
        {
            Type(typed, before).ShouldBe(after);
        }

        [Theory]
        [InlineData("🤜", "a ➕ [b ➕ c]", "a ➕ 🤜[b ➕ c]🤛")]
        [InlineData("🔤", "😀 [Hello]❗️", "😀 🔤[Hello]🔤❗️")]
        [InlineData("🍇", "[😀 a❗️\n😀 b❗️]\n", "🍇[😀 a❗️\n😀 b❗️]🍉\n")]
        public void An_opener_typed_over_a_selection_wraps_it_and_keeps_it_selected(string typed, string before, string after)
        {
            Type(typed, before).ShouldBe(after);
        }

        [Theory]
        [InlineData("x", "a [b] c", "a x| c")]
        [InlineData("🍉", "a [b] c", "a 🍉| c")]
        [InlineData("🍇", "😀 🔤a [b] c🔤", "😀 🔤a 🍇| c🔤")]
        public void Anything_else_typed_over_a_selection_takes_its_place(string typed, string before, string after)
        {
            Type(typed, before).ShouldBe(after);
        }

        [Theory]
        [InlineData("🏁 🍇\n  😀 🔤hi🔤❗️\n  |", "🏁 🍇\n  😀 🔤hi🔤❗️\n🍉|", "back to the margin")]
        [InlineData("🏁 🍇\n  ↪️ a 🍇\n    😀 a❗️\n    |", "🏁 🍇\n  ↪️ a 🍇\n    😀 a❗️\n  🍉|", "back to the inner block's start")]
        [InlineData("🏁 🍇\n  ↪️ a 🍇\n  🍉\n  |", "🏁 🍇\n  ↪️ a 🍇\n  🍉\n🍉|", "a block that is closed is not the one")]
        [InlineData("🏁 🍇\n\t↪️ a 🍇\n\t\t|", "🏁 🍇\n\t↪️ a 🍇\n\t🍉|", "tabs")]
        [InlineData("  ↪️ a 🍇\n|", "  ↪️ a 🍇\n  🍉|", "in, when the line is not far enough")]
        [InlineData("🏁 🍇\r\n  😀 a❗️\r\n  |", "🏁 🍇\r\n  😀 a❗️\r\n🍉|", "Windows line ends")]
        [InlineData("🏁 🍇\n  |", "🏁 🍇\n🍉|", "the first line of the block")]
        public void A_closing_watermelon_on_an_empty_line_lines_up_with_the_grapes_it_closes(string before, string after, string what)
        {
            Type("🍉", before).ShouldBe(after, what);
        }

        [Theory]
        [InlineData("🏁 🍇\n  😀 a❗️ |", "🏁 🍇\n  😀 a❗️ 🍉|", "something is on the line already")]
        [InlineData("  |", "  🍉|", "nothing is open")]
        [InlineData("😀 🔤🍇🔤❗️\n  |", "😀 🔤🍇🔤❗️\n  🍉|", "the only grapes are in a string")]
        [InlineData("💭 🍇\n  |", "💭 🍇\n  🍉|", "the only grapes are in a comment")]
        [InlineData("🏁 🍇 🍉\n  |", "🏁 🍇 🍉\n  🍉|", "every block is closed")]
        public void A_closing_watermelon_stays_where_it_was_typed_when(string before, string after, string what)
        {
            Type("🍉", before).ShouldBe(after, what);
        }

        [Fact]
        public void Only_the_rest_of_the_cursors_line_decides_whether_an_opener_is_closed()
        {
            Type("🍇", "🏁 |\nx").ShouldBe("🏁 🍇|🍉\nx");
        }

        [Theory]
        [InlineData("😀")]
        [InlineData("x")]
        [InlineData("❗")]
        public void Anything_that_is_not_half_of_a_pair_is_just_typed(string typed)
        {
            Type(typed, "🍇 |🍉").ShouldBe("🍇 " + typed + "|🍉");
            Typing.DependsOnWhatIsAround(typed).ShouldBeFalse();
        }

        [Theory]
        [InlineData("🍇")]
        [InlineData("🍉")]
        [InlineData("🤜")]
        [InlineData("🤛")]
        [InlineData("🍿")]
        [InlineData("🐚")]
        [InlineData("🍆")]
        [InlineData("🔤")]
        public void Each_half_of_a_pair_needs_to_know_what_is_around_the_cursor(string typed)
        {
            Typing.DependsOnWhatIsAround(typed).ShouldBeTrue();
        }

        [Fact]
        public void A_very_deep_file_is_no_trouble()
        {
            var deep = string.Concat(Enumerable.Repeat("🍇\n", 60_000));

            var edit = Typing.For("🍉", new TextAroundCursor(deep + "    ", string.Empty, string.Empty));

            edit.Text.ShouldBe("🍉");
            edit.RemoveBefore.ShouldBe(4);
        }

        [Fact]
        public void The_editor_matches_blocks_groups_literals_and_generic_arguments()
        {
            // 🍨 and 🍯 are not here. They are the names of the list and dictionary types
            // (🍨🐚🔡🍆), not openers: a literal of either starts with 🍿.
            EmojicodePairs.Matched.Select(pair => pair.Open + pair.Close).ShouldBe(["🍇🍉", "🤜🤛", "🍿🍆", "🐚🍆"]);
        }
    }
}
