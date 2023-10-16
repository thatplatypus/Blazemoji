namespace Blazemoji.Emojicode.Keywords
{
    public class Remainder : EmojicodeKeyword
    {
        public override string Emoji => throw new NotImplementedException();

        public override string Description => "Modulus operator, remainder";

        public override string Keyword => "%";

        public override string Category => EmojicodeCategory.Operators;
    }
}
