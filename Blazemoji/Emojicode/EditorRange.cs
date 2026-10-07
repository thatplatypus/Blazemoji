namespace Blazemoji.Emojicode
{
    /// <summary>
    /// A range in the editor. Lines and columns are 1-based and columns count UTF-16 code
    /// units, which is how Monaco addresses text. The end column is exclusive.
    /// </summary>
    public readonly record struct EditorRange(int StartLine, int StartColumn, int EndLine, int EndColumn);
}
