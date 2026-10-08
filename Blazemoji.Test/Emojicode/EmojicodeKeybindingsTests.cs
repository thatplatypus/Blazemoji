using Blazemoji.Emojicode;
using Blazemoji.Emojicode.Intelligence;
using BlazorMonaco;

namespace Blazemoji.Test.Emojicode
{
    public class EmojicodeKeybindingsTests
    {
        private const int Exclamation = (int)KeyMod.Shift | (int)KeyCode.Digit1;
        private const int Quote = (int)KeyMod.Shift | (int)KeyCode.Quote;
        private const int Comment = (int)KeyMod.CtrlCmd | (int)KeyCode.Slash;

        [Fact]
        public void The_equals_key_types_the_arrow_that_assigns_in_code_and_a_plain_sign_in_a_string_or_comment()
        {
            const int equals = (int)KeyCode.Equal;

            EmojicodeKeybindings.TextFor(equals, TextContext.Code).ShouldBe("➡️");
            EmojicodeKeybindings.TextFor(equals, TextContext.String).ShouldBe("=");
            EmojicodeKeybindings.TextFor(equals, TextContext.Comment).ShouldBe("=");
            EmojicodeKeybindings.CanDependOnContext(equals).ShouldBeTrue();
        }

        [Fact]
        public void In_code_a_key_types_its_emoji()
        {
            EmojicodeKeybindings.TextFor(Exclamation, TextContext.Code).ShouldBe("❗");
            EmojicodeKeybindings.TextFor(Quote, TextContext.Code).ShouldBe("🔤");
        }

        [Theory]
        [InlineData(KeyCode.Digit1, "!")]
        [InlineData(KeyCode.BracketLeft, "{")]
        [InlineData(KeyCode.BracketRight, "}")]
        [InlineData(KeyCode.Digit5, "%")]
        [InlineData(KeyCode.Digit6, "^")]
        [InlineData(KeyCode.Digit8, "*")]
        [InlineData(KeyCode.Digit9, "(")]
        [InlineData(KeyCode.Digit0, ")")]
        [InlineData(KeyCode.Equal, "+")]
        [InlineData(KeyCode.Period, ">")]
        [InlineData(KeyCode.Comma, "<")]
        public void Inside_a_string_or_a_comment_a_shifted_key_types_what_is_printed_on_it(KeyCode key, string printed)
        {
            var keybinding = (int)KeyMod.Shift | (int)key;

            EmojicodeKeybindings.TextFor(keybinding, TextContext.String).ShouldBe(printed);
            EmojicodeKeybindings.TextFor(keybinding, TextContext.Comment).ShouldBe(printed);
        }

        [Fact]
        public void The_quote_key_still_closes_a_string_and_is_a_plain_quote_in_a_comment()
        {
            EmojicodeKeybindings.TextFor(Quote, TextContext.String).ShouldBe("🔤");
            EmojicodeKeybindings.TextFor(Quote, TextContext.Comment).ShouldBe("\"");
        }

        [Fact]
        public void A_shortcut_that_is_not_a_typed_character_does_the_same_everywhere()
        {
            EmojicodeKeybindings.TextFor(Comment, TextContext.String).ShouldBe("💭");
            EmojicodeKeybindings.CanDependOnContext(Comment).ShouldBeFalse();
            EmojicodeKeybindings.CanDependOnContext(Exclamation).ShouldBeTrue();
        }

        [Fact]
        public void Every_shifted_key_that_becomes_an_emoji_has_a_plain_character_to_fall_back_to()
        {
            var shiftedOnly = EmojicodeKeybindings.Keybindings.Keys
                .Where(keybinding => (keybinding & (int)KeyMod.Shift) != 0 && (keybinding & (int)KeyMod.CtrlCmd) == 0);

            shiftedOnly.ShouldAllBe(keybinding => EmojicodeKeybindings.CanDependOnContext(keybinding));
        }
    }
}
