namespace Blazemoji.Emojicode.Keywords
{
    public class GreaterThan : EmojicodeKeyword
    {
        public override string Emoji => Emojis.GreaterThan;

        public override string Description => "Greater than operator";

        public override string Keyword => ">";

        public override string Category => EmojicodeCategory.Operators;
    }
}
