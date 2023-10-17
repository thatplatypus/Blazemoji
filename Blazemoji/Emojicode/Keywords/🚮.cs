namespace Blazemoji.Emojicode.Keywords
{
    public class Remainder : EmojicodeKeyword
    {
        public override string Emoji => Emojis.Remainder;
        public override string Description => "Modulus";

        public override string Keyword => "%";

        public override string Category => EmojicodeCategory.Operators;
    }
}
