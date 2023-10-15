namespace Blazemoji.Emojicode.Keywords
{
    public class GrinningFace : EmojicodeKeyword
    {
        public override string Emoji => Emojis.GrinningFace;

        public override string Description => "Prints output";

        public override string Keyword => "print";

        public override string ShortCode => ":grinning:";

        public override string Category => EmojicodeCategory.Basics;
    }
}
