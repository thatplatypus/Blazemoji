namespace Blazemoji.Emojicode.Keywords
{
    public class Flag : EmojicodeKeyword
    {
        public override string Emoji => Emojis.ChequeredFlag;

        public override string Description => "Program entry point";

        public override string Keyword => "main()";

        public override string Category => EmojicodeCategory.Basics;
    }
}
