namespace Blazemoji.Emojicode.Editing
{
    public sealed record Pair(string Open, string Close);

    /// <summary>
    /// What Emojicode writes in pairs, and how it writes comments, as an editor needs to know it.
    /// </summary>
    public static class EmojicodePairs
    {
        /// <summary>A block of code, and a callable's type.</summary>
        public static Pair Block { get; } = new("🍇", "🍉");

        /// <summary>The brackets around part of an expression.</summary>
        public static Pair Group { get; } = new("🤜", "🤛");

        /// <summary>A list or dictionary literal.</summary>
        public static Pair Listing { get; } = new("🍿", "🍆");

        public static Pair GenericArguments { get; } = new("🐚", "🍆");

        public static Pair String { get; } = new("🔤", "🔤");

        public const string LineComment = "💭";

        public static Pair BlockComment { get; } = new("💭🔜", "🔚💭");

        /// <summary>What the compiler reads as "the next character is not special" inside a string.</summary>
        public const string Escape = "❌";

        /// <summary>
        /// The pairs an editor shows as belonging together. 🍨 and 🍯 are not among them: they
        /// are the names of the list and dictionary types, and a literal of either starts with 🍿.
        /// </summary>
        public static IReadOnlyList<Pair> Matched { get; } = [Block, Group, Listing, GenericArguments];

        /// <summary>The pairs whose closer is typed for the writer when the opener is.</summary>
        public static IReadOnlyList<Pair> Completed { get; } = [Block, Group, Listing, GenericArguments, String];
    }
}
