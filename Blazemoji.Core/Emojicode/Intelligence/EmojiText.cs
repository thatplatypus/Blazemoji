namespace Blazemoji.Emojicode.Intelligence
{
    /// <summary>
    /// The same emoji can be written with or without a variation selector: ✏ and ✏️ are one
    /// method name to the compiler, and its documentation report uses the bare form while
    /// people type the other. Names are compared with the selectors taken out.
    /// </summary>
    public static class EmojiText
    {
        public static string Bare(string emoji) =>
            emoji.Contains('️') || emoji.Contains('︎')
                ? emoji.Replace("️", string.Empty).Replace("︎", string.Empty)
                : emoji;

        public static bool Same(string a, string b) => string.Equals(Bare(a), Bare(b), StringComparison.Ordinal);
    }
}
