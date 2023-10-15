namespace Blazemoji.Emojicode.Keywords
{
    public class Grape : EmojicodeKeyword
    {
        public override string Emoji => Emojis.Grape;

        public override string Keyword => "{";

        public override string Description => "Opens a code block";

        public override string Category => EmojicodeCategory.ControlFlow;
    }
}
