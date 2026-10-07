using Blazemoji.Emojicode.Intelligence;

namespace Blazemoji.Test.Intelligence
{
    public class SourceReaderTests
    {
        private static string Describe(string source) =>
            string.Join(" ", SourceReader.Read(source).Select(token => token.Kind switch
            {
                TokenKind.Word => $"w:{token.Text}",
                TokenKind.Number => $"n:{token.Text}",
                TokenKind.Text => "text",
                _ => token.Text,
            }));

        [Fact]
        public void A_call_is_a_method_a_receiver_its_arguments_and_a_mood()
        {
            Describe("📥 app 🔤/hello🔤 handler❗️").ShouldBe("📥 w:app text w:handler ❗️");
        }

        [Fact]
        public void Emoji_written_without_spaces_are_separate_tokens()
        {
            Describe("🆕🍷❗️ ➡️ app").ShouldBe("🆕 🍷 ❗️ ➡️ w:app");
        }

        [Fact]
        public void An_emoji_with_a_variation_selector_a_skin_tone_or_a_joiner_is_one_token()
        {
            Describe("↩️ 👂🏼 🙅‍♀️ 1️⃣").ShouldBe("↩️ 👂🏼 🙅‍♀️ 1️⃣");
        }

        [Fact]
        public void Numbers_and_names_are_told_apart()
        {
            Describe("8080 ➡️ 🖍🆕port2 3.5 _x").ShouldBe("n:8080 ➡️ 🖍 🆕 w:port2 n:3.5 w:_x");
        }

        [Fact]
        public void A_string_is_one_token_whatever_is_inside_it()
        {
            Describe("😀 🔤Hello, 🧲name🧲! 💭 not a comment ❌🔤 still inside🔤❗️").ShouldBe("😀 text ❗️");
        }

        [Fact]
        public void Line_comments_block_comments_and_documentation_are_left_out()
        {
            const string source = """
                💭 a comment with 📥 app in it
                💭🔜 a block
                with 🆕🍷❗️ ➡️ nothing 🔚💭
                📗 documentation mentioning 🍷 📗
                📘
                  package documentation
                📘
                🏁 🍇 🍉
                """;

            Describe(source).ShouldBe("🏁 🍇 🍉");
        }

        [Fact]
        public void An_unfinished_string_or_comment_runs_to_the_end_without_failing()
        {
            Describe("😀 🔤never closed").ShouldBe("😀 text");
            Describe("😀 💭🔜 never closed").ShouldBe("😀");
        }

        [Fact]
        public void Every_token_knows_where_it_is()
        {
            var tokens = SourceReader.Read("🏁 🍇\n  📥 app");

            var app = tokens.Single(token => token.Text == "app");
            app.Start.ShouldBe("🏁 🍇\n  📥 ".Length);
            app.End.ShouldBe("🏁 🍇\n  📥 app".Length);
            app.Line.ShouldBe(2);
            tokens[0].Line.ShouldBe(1);
            tokens[0].Start.ShouldBe(0);
            tokens[0].End.ShouldBe(2);
        }

        [Fact]
        public void A_dot_is_a_token_of_its_own_so_that_a_receiver_can_be_followed_by_one()
        {
            Describe("app.").ShouldBe("w:app .");
            Describe("app.inb").ShouldBe("w:app . w:inb");
        }

        [Fact]
        public void A_colon_is_a_token_of_its_own()
        {
            Describe(":inbox").ShouldBe(": w:inbox");
        }

        [Fact]
        public void Nothing_is_no_tokens()
        {
            SourceReader.Read(string.Empty).ShouldBeEmpty();
            SourceReader.Read("  \n\t ").ShouldBeEmpty();
        }
    }
}
