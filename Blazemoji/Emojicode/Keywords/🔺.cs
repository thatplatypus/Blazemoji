namespace Blazemoji.Emojicode.Keywords
{
    public class Exponent : EmojicodeKeyword
    {
        public override string Emoji => throw new NotImplementedException();

        public override string Description => "Math power of, exponent";

        public override string Keyword => "^";

        public override string Category => EmojicodeCategory.Operators;
    }
}
