namespace Blazemoji.Emojicode.Keywords
{
    public class Not : EmojicodeKeyword
    {
        public override string Emoji => Emojis.CrossMarkButton;

        public override string Description => "Negation";

        public override string Keyword => "!bool";

        public override string Category => EmojicodeCategory.Operators;
    }
}
