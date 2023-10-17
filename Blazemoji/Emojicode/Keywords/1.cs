namespace Blazemoji.Emojicode.Keywords
{
    public class NumberLiteral : EmojicodeKeyword
    {
        public override string Emoji => Emojis.NumberLiteral;

        public override string Description => "Number";

        public override string Keyword => "123";

        public override string Category => EmojicodeCategory.ClassesAndValueTypes;
    }
}
