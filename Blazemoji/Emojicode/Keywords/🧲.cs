namespace Blazemoji.Emojicode.Keywords
{
    public class Magnet : EmojicodeKeyword
    {
        ///<inheritdoc />
        public override string Emoji => Emojis.Magnet;

        ///<inheritdoc />
        public override string Description => "Escaping variables";

        ///<inheritdoc />
        public override string Keyword => "@";

        ///<inheritdoc />
        public override string Category => EmojicodeCategory.Operators;
    }
}