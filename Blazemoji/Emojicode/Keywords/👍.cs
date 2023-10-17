namespace Blazemoji.Emojicode.Keywords
{
    public class ThumbsUp : EmojicodeKeyword
    {
        public override string Emoji => Emojis.ThumbsUp;

        public override string Description => "True value";

        public override string Keyword => "true";

        public override string Category => EmojicodeCategory.Literals;
    }
}
