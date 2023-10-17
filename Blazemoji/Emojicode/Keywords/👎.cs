namespace Blazemoji.Emojicode.Keywords
{
    public class ThumbsDown : EmojicodeKeyword
    {
        public override string Emoji => Emojis.ThumbsDown;

        public override string Description => "False value";

        public override string Keyword => "false";

        public override string Category => EmojicodeCategory.Literals;
    }
}
