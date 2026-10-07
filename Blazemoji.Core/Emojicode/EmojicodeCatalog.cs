namespace Blazemoji.Emojicode
{
    /// <summary>
    /// Every keyword in the catalog: one instance of each <see cref="EmojicodeKeyword"/> class.
    /// </summary>
    public static class EmojicodeCatalog
    {
        public static IReadOnlyList<EmojicodeKeyword> All() =>
            typeof(EmojicodeKeyword).Assembly.GetTypes()
                .Where(type => type.IsSubclassOf(typeof(EmojicodeKeyword)) && !type.IsAbstract)
                .Select(type => Activator.CreateInstance(type) as EmojicodeKeyword)
                .Where(keyword => keyword?.Emoji is not null)
                .Select(keyword => keyword!)
                .ToList();
    }
}
