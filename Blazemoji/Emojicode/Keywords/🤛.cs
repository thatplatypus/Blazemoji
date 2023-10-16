namespace Blazemoji.Emojicode.Keywords
{
    public class FistLeft : EmojicodeKeyword
    {
        public override string Emoji => throw new NotImplementedException();

        public override string Description => "Math left grouping operator";

        public override string Keyword => "(";

        public override string Category => EmojicodeCategory.Operators;
    }
}
