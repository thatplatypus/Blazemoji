namespace Blazemoji.Emojicode.Intelligence
{
    /// <summary>
    /// What the compiler's documentation report says about one package.
    /// </summary>
    public sealed record PackageDocumentation(string Name, string Documentation, IReadOnlyList<TypeDocumentation> Types)
    {
        public TypeDocumentation? Find(string typeName) => Types.FirstOrDefault(type => type.Name == typeName);
    }

    public enum TypeKind
    {
        Class,
        ValueType,
        Protocol,
        Enumeration,
        Other,
    }

    /// <param name="Initializers">Called as <c>🆕Type name args❗️</c>. The unnamed one has an empty name.</param>
    /// <param name="TypeMethods">Called on the type itself: <c>method🐇Type args❗️</c>.</param>
    public sealed record TypeDocumentation(
        string Package,
        string Name,
        TypeKind Kind,
        string Documentation,
        IReadOnlyList<MethodDocumentation> Methods,
        IReadOnlyList<MethodDocumentation> TypeMethods,
        IReadOnlyList<MethodDocumentation> Initializers);

    /// <param name="Mood"><c>❗️</c> for a method that does something, <c>❓</c> for one that asks.</param>
    /// <param name="ReturnType">Null when the method returns nothing.</param>
    public sealed record MethodDocumentation(
        string Name,
        string Mood,
        string Documentation,
        IReadOnlyList<ParameterDocumentation> Parameters,
        TypeReference? ReturnType);

    public sealed record ParameterDocumentation(string Name, TypeReference Type);

    /// <summary>
    /// A type as it appears in a signature.
    /// </summary>
    /// <param name="Display">How it is written in Emojicode, for example <c>🍬🔡</c> or <c>🍇📨➡️📬🍉</c>.</param>
    /// <param name="TypeName">
    /// The type whose methods a value of this type has, when that is a named type: <c>🔡</c> for
    /// <c>🔡</c>, and also for <c>🍬🔡</c>, since the optional is unwrapped before use. Null for
    /// callables, generic parameters and anything else without methods of its own.
    /// </param>
    public sealed record TypeReference(string Display, string? TypeName);
}
