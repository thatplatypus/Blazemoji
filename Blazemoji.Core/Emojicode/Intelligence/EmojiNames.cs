namespace Blazemoji.Emojicode.Intelligence
{
    /// <param name="Alias">The name that matched, as in <c>:inbox_tray:</c>.</param>
    public sealed record NamedEmoji(string Emoji, string Alias, string Description);

    public interface IEmojiNames
    {
        /// <summary>
        /// Emoji whose name starts with <paramref name="query"/>, then those that contain it or
        /// are tagged with it. At most <paramref name="limit"/>.
        /// </summary>
        IReadOnlyList<NamedEmoji> Search(string query, int limit);

        /// <summary>What an emoji is called, with underscores as spaces. Null for one that has no name.</summary>
        string? NameOf(string emoji);

        /// <summary>Every name and tag of an emoji, lower case, with underscores between words.</summary>
        IReadOnlyList<string> NamesOf(string emoji);
    }

    /// <summary>
    /// Emoji names from the list GitHub uses for its <c>:shortcodes:</c>.
    /// </summary>
    public sealed class EmojiNames : IEmojiNames
    {
        private readonly List<(string Alias, GEmojiSharp.GEmoji Emoji, bool IsTag)> _names = [];
        private readonly Dictionary<string, GEmojiSharp.GEmoji> _byEmoji = [];

        public EmojiNames()
        {
            foreach (var emoji in GEmojiSharp.Emoji.All.Where(emoji => !emoji.IsCustom && !string.IsNullOrEmpty(emoji.Raw)))
            {
                _byEmoji.TryAdd(EmojiText.Bare(emoji.Raw), emoji);

                foreach (var alias in emoji.Aliases)
                    _names.Add((alias, emoji, false));

                foreach (var tag in emoji.Tags ?? [])
                    _names.Add((tag.Replace(' ', '_'), emoji, true));
            }
        }

        public IReadOnlyList<NamedEmoji> Search(string query, int limit)
        {
            var wanted = query.Trim().ToLowerInvariant().Replace(' ', '_');
            if (wanted.Length == 0)
                return [];

            return _names
                .Select(name => (name, Rank: Rank(name.Alias, name.IsTag, wanted)))
                .Where(match => match.Rank >= 0)
                .OrderBy(match => match.Rank)
                .ThenBy(match => match.name.Alias.Length)
                .ThenBy(match => match.name.Alias, StringComparer.Ordinal)
                .DistinctBy(match => match.name.Emoji.Raw)
                .Take(limit)
                .Select(match => new NamedEmoji(match.name.Emoji.Raw, match.name.Alias, match.name.Emoji.Description ?? match.name.Alias))
                .ToList();
        }

        public string? NameOf(string emoji) =>
            _byEmoji.TryGetValue(EmojiText.Bare(emoji), out var found) ? found.Aliases.FirstOrDefault()?.Replace('_', ' ') : null;

        public IReadOnlyList<string> NamesOf(string emoji) =>
            _byEmoji.TryGetValue(EmojiText.Bare(emoji), out var found)
                ? [.. found.Aliases, .. (found.Tags ?? []).Select(tag => tag.Replace(' ', '_'))]
                : [];

        /// <returns>Lower is better. Negative for no match.</returns>
        private static int Rank(string alias, bool isTag, string wanted)
        {
            if (alias == wanted)
                return isTag ? 2 : 0;

            if (alias.StartsWith(wanted, StringComparison.Ordinal))
                return isTag ? 3 : 1;

            return !isTag && alias.Contains("_" + wanted, StringComparison.Ordinal) ? 4 : -1;
        }
    }
}
