namespace Blazemoji.Emojicode.Keywords
{
    public class New : EmojicodeKeyword
    {
        public override string Emoji => Emojis.NewButton;

        public override string Description => "Initialize a new object";

        public override string Keyword => "new";

        public override string Category => EmojicodeCategory.ClassesAndValueTypes;
    }
}
