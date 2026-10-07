using Blazemoji.Emojicode.Intelligence;

namespace Blazemoji.Test.Intelligence
{
    public class EmojiNamesTests
    {
        private static readonly EmojiNames Names = new();

        [Theory]
        [InlineData("grapes", "🍇")]
        [InlineData("inbox", "📥")]
        [InlineData("watermelon", "🍉")]
        [InlineData("wine", "🍷")]
        [InlineData("bricks", "🧱")]
        [InlineData("postbox", "📮")]
        [InlineData("pencil2", "✏️")]
        [InlineData("wastebasket", "🗑️")]
        public void An_emoji_is_found_by_the_start_of_its_name(string query, string emoji)
        {
            Names.Search(query, 10).Select(found => EmojiText.Bare(found.Emoji)).ShouldContain(EmojiText.Bare(emoji));
        }

        [Fact]
        public void An_exact_name_comes_first()
        {
            Names.Search("grapes", 10)[0].ShouldBe(new NamedEmoji("🍇", "grapes", "grapes"));
        }

        [Fact]
        public void A_search_is_not_case_sensitive_and_reads_spaces_as_underscores()
        {
            Names.Search("Inbox Tray", 5)[0].Emoji.ShouldBe("📥");
        }

        [Fact]
        public void An_emoji_can_be_found_by_a_later_word_of_its_name()
        {
            Names.Search("tray", 20).Select(found => found.Emoji).ShouldContain("📥");
        }

        [Fact]
        public void No_more_than_the_limit_come_back_and_each_emoji_once()
        {
            var found = Names.Search("a", 7);

            found.Count.ShouldBe(7);
            found.Select(emoji => emoji.Emoji).ShouldBeUnique();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("zzzzqqqq")]
        public void A_search_that_matches_nothing_is_empty(string query)
        {
            Names.Search(query, 10).ShouldBeEmpty();
        }

        [Theory]
        [InlineData("🍇", "grapes")]
        [InlineData("📥", "inbox tray")]
        [InlineData("✏", "pencil2")]
        [InlineData("✏️", "pencil2")]
        public void An_emoji_knows_its_name_with_or_without_a_variation_selector(string emoji, string name)
        {
            Names.NameOf(emoji).ShouldBe(name);
        }

        [Fact]
        public void Text_that_is_not_an_emoji_has_no_name()
        {
            Names.NameOf("app").ShouldBeNull();
        }
    }
}
