namespace Blazemoji.Emojicode.Keywords
{
    public class LessThan : EmojicodeKeyword
    {
        public override string Emoji => Emojis.LessThan;

        public override string Description => "Less than";

        public override string Keyword => "<";

        public override string Category => EmojicodeCategory.Operators;
    }
}
