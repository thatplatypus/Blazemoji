namespace Blazemoji.Emojicode.Keywords
{
    public class Escape : EmojicodeKeyword
    {
        public override string Emoji => Emojis.CrossMark;

        public override string Description => "Escapes the next emoji";

        public override string Keyword => "\\";

        public override string Category => EmojicodeCategory.Basics;
    }
}
