namespace Blazemoji.Emojicode.Keywords
{
    public class Crayon : EmojicodeKeyword
    {
        public override string Emoji => "🖍";

        public override string Description => "Editable variable";

        public override string Keyword => "mutable";

        public override string Category => EmojicodeCategory.ClassesAndValueTypes;

        public override string? Example => "1 ➡️ 🖍🆕 x\r\nx➕1 ➡️ 🖍x";
    }
}
