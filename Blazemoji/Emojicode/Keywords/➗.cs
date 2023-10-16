namespace Blazemoji.Emojicode.Keywords
{
    public class Divide : EmojicodeKeyword
    {
        ///<inheritdoc />
        public override string Emoji => Emojis.Division;

        ///<inheritdoc />
        public override string Description => "Division operator";

        ///<inheritdoc />
        public override string Keyword => "/";

        ///<inheritdoc />
        public override string Category => EmojicodeCategory.Operators;
    }
}
