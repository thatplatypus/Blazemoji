namespace Blazemoji.Emojicode.Keywords
{
    public class ThoughtBubble : EmojicodeKeyword
    {
        public override string Emoji => Emojis.ThoughtBubble;

        public override string Description => "Inline comment";

        public override string Keyword => "//";

        public override string Category => EmojicodeCategory.Basics;
    }
}
