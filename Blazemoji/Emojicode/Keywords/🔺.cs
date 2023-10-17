namespace Blazemoji.Emojicode.Keywords
{
    public class Exponent : EmojicodeKeyword
    {
        public override string Emoji => Emojis.Exponent;

        public override string Description => "Power of, exponent";

        public override string Keyword => "^";

        public override string Category => EmojicodeCategory.Operators;
    }
}
