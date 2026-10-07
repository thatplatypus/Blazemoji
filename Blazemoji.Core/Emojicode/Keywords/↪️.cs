namespace Blazemoji.Emojicode.Keywords
{
    public class If : EmojicodeKeyword
    {
        public override string Emoji => Emojis.IfButton;

        public override string Description => "If conditional";

        public override string Keyword => "if";

        public override string Category => EmojicodeCategory.Logic;
    }
}
