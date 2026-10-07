namespace Blazemoji.Emojicode.Intelligence
{
    public enum CompletionKind
    {
        Method,
        Variable,
        Keyword,
        Emoji,
    }

    /// <summary>One suggestion.</summary>
    /// <param name="Insert">What accepting it puts in the text.</param>
    /// <param name="ReplaceStart">Offset where the text it replaces starts: what was typed to find it.</param>
    /// <param name="ReplaceEnd">Offset just past the text it replaces.</param>
    /// <param name="Detail">One line beside the label: what the suggestion belongs to.</param>
    /// <param name="Documentation">Markdown.</param>
    /// <param name="Order">Position in the list. Suggestions are already in the order to show them.</param>
    public sealed record CompletionEntry(
        string Label,
        CompletionKind Kind,
        string Insert,
        int ReplaceStart,
        int ReplaceEnd,
        string Detail,
        string Documentation,
        int Order);

    /// <param name="Start">Offset of the first character the hover is about.</param>
    /// <param name="Markdown">What to show.</param>
    public sealed record HoverInfo(int Start, int End, string Markdown);

    /// <param name="Label">The parameter as it appears in <see cref="SignatureInfo.Label"/>.</param>
    public sealed record SignatureParameter(string Label);

    /// <param name="Label">The whole call written out, for example <c>📥 app path 🔡 handler 🍇📨➡️📬🍉❗️</c>.</param>
    /// <param name="ActiveParameter">Index of the parameter being typed. Equal to the count when all are given.</param>
    public sealed record SignatureInfo(string Label, string Documentation, IReadOnlyList<SignatureParameter> Parameters, int ActiveParameter);

    /// <summary>
    /// What the editor can say about Emojicode text at a position: suggestions, a description,
    /// and the parameters of the call being typed. It is given the text and the documentation
    /// of the packages in play, and knows nothing about the editor that asks.
    /// </summary>
    public interface ICodeIntelligence
    {
        /// <param name="offset">UTF-16 offset of the cursor in <paramref name="text"/>.</param>
        IReadOnlyList<CompletionEntry> Complete(string text, int offset, IReadOnlyList<PackageDocumentation> packages);

        HoverInfo? Hover(string text, int offset, IReadOnlyList<PackageDocumentation> packages);

        SignatureInfo? Signature(string text, int offset, IReadOnlyList<PackageDocumentation> packages);

        /// <summary>The packages a file imports with <c>📦 name 🏠</c>, and the standard package, which every file has.</summary>
        IReadOnlyList<string> Imports(string text);
    }
}
