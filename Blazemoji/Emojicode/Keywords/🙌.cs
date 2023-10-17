namespace Blazemoji.Emojicode.Keywords
{
    public class Equals : EmojicodeKeyword
    {
        public override string Emoji => Emojis.EqualTo;

        public override string Description => "Equals";

        public override string Keyword => "==";

        public override string Category => EmojicodeCategory.Operators;
    }
}
