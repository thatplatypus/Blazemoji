using System.Text.RegularExpressions;

namespace Blazemoji.Emojicode
{
    public static class EmojiStringGenerator
    {
        private static readonly string[] _emojis;

        static EmojiStringGenerator()
        {
            var emojiRange = @"\p{Cs}";
            var emojis = new List<string>();

            for (int i = 0x1F600; i <= 0x1F64F; i++)
            {
                var emojiCandidate = char.ConvertFromUtf32(i);
                if (Regex.IsMatch(emojiCandidate, emojiRange))
                {
                    emojis.Add(emojiCandidate);
                }
            }

            _emojis = [..emojis];
        }

        public static string GetRandomEmojis(int length)
        {
            var random = new Random();
            return string.Concat(Enumerable.Range(0, length).Select(_ => _emojis[random.Next(_emojis.Length)]));
        }
    }
}
