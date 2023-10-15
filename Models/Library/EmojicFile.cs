using Blazemoji.Emojicode;

namespace Blazemoji.Models.Library
{
    public class EmojicFile
    {
        public string Name { get; set; } = $".{Emojis.Grape}";

        public string Code { get; set; } = $"{Emojis.ChequeredFlag} {Emojis.Grape}{Environment.NewLine}{Emojis.Watermelon}";
    }
}
