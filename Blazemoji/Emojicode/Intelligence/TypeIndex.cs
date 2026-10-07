namespace Blazemoji.Emojicode.Intelligence
{
    /// <summary>
    /// Every type of the packages a project imports, by name.
    /// </summary>
    public sealed class TypeIndex
    {
        private readonly Dictionary<string, TypeDocumentation> _types = [];

        public TypeIndex(IEnumerable<PackageDocumentation> packages)
        {
            foreach (var type in packages.SelectMany(package => package.Types))
                _types.TryAdd(EmojiText.Bare(type.Name), type);
        }

        public IEnumerable<TypeDocumentation> All => _types.Values;

        public TypeDocumentation? Find(string? name) =>
            name is not null && _types.TryGetValue(EmojiText.Bare(name), out var type) ? type : null;

        public MethodDocumentation? FindMethod(string? typeName, string methodName) =>
            Find(typeName)?.Methods.FirstOrDefault(method => EmojiText.Same(method.Name, methodName));

        public MethodDocumentation? FindTypeMethod(string? typeName, string methodName) =>
            Find(typeName)?.TypeMethods.FirstOrDefault(method => EmojiText.Same(method.Name, methodName));
    }
}
