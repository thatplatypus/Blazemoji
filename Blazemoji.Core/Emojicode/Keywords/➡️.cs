namespace Blazemoji.Emojicode.Keywords
{
    public class Assignment : EmojicodeKeyword
    {
        public override string Emoji => Emojis.ArrowBlock;

        public override string Description => "Assigns a value to a variable";

        public override string Keyword => "=";

        public override string Category => EmojicodeCategory.Basics;

        public override string? Example => "1 ➡️ x";
    }
}
