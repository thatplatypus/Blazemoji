namespace Blazemoji.Emojicode.Keywords
{
    public class Minus : EmojicodeKeyword
    {
        ///<inheritdoc />
        public override string Emoji => Emojis.Minus;

        ///<inheritdoc />
        public override string Description => "Subtraction operator";

        ///<inheritdoc />
        public override string Keyword => "-";

        ///<inheritdoc />
        public override string Category => EmojicodeCategory.Operators;
    }
}
