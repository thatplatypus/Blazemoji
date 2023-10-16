namespace Blazemoji.Emojicode.Keywords
{
    public class Plus : EmojicodeKeyword
    {
        ///<inheritdoc />
        public override string Emoji => Emojis.Plus;

        ///<inheritdoc />
        public override string Description => "Addition operator";

        ///<inheritdoc />
        public override string Keyword => "+";

        ///<inheritdoc />
        public override string Category => EmojicodeCategory.Operators;
    }
}
