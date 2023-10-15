namespace Blazemoji.Emojicode.Keywords
{
    public class Exclamation : EmojicodeKeyword
    {
        public override string Emoji => Emojis.Exclamation;

        public override string Description => "The end of arguments";

        public override string Keyword => ":";

        public override string ShortCode => "!";

        public override string Category => EmojicodeCategory.ClassesAndValueTypes;
    }
}
