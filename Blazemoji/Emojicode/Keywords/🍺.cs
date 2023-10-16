namespace Blazemoji.Emojicode.Keywords
{
    public class Beer : EmojicodeKeyword
    {
        public override string Emoji => Emojis.Beer;

        public override string Description => "Accesses an optional property";

        public override string Keyword => "!";

        public override string Category => EmojicodeCategory.Operators;
    }
}
