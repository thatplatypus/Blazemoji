using System.Globalization;
using System.Text;
using Blazemoji.Toolchain;

namespace Blazemoji.Emojicode
{
    /// <summary>
    /// The compiler reports a 1-based line and a 1-based character counted in Unicode code
    /// points, and only where a problem starts. The editor wants UTF-16 columns and a range.
    /// </summary>
    public static class DiagnosticPositions
    {
        private const char VariationSelector16 = '️';

        /// <returns>Null when the diagnostic has no location.</returns>
        public static EditorRange? ToEditorRange(Diagnostic diagnostic, string source)
        {
            if (diagnostic.Line < 1)
                return null;

            var lines = source.Split('\n');
            var lineNumber = Math.Min(diagnostic.Line, lines.Length);
            var line = lines[lineNumber - 1].TrimEnd('\r');

            // A line past the end of the file has no meaningful character position.
            var character = diagnostic.Line > lines.Length ? 1 : Math.Max(diagnostic.Character, 1);

            if (line.Length == 0)
                return new EditorRange(lineNumber, 1, lineNumber, 1);

            // Character 0 means the compiler gave only a line; point at the first thing on it.
            var start = diagnostic.Character < 1 || diagnostic.Line > lines.Length
                ? FirstNonWhitespace(line)
                : Math.Min(Utf16OffsetOfCharacter(line, character), line.Length - 1);
            var end = TokenEnd(line, start);

            return new EditorRange(lineNumber, start + 1, lineNumber, end + 1);
        }

        private static int FirstNonWhitespace(string line)
        {
            for (var offset = 0; offset < line.Length; offset++)
            {
                if (!char.IsWhiteSpace(line[offset]))
                    return offset;
            }

            return 0;
        }

        private static int Utf16OffsetOfCharacter(string line, int character)
        {
            var offset = 0;
            for (var seen = 1; seen < character && offset < line.Length; seen++)
                offset += RuneAt(line, offset).Utf16SequenceLength;

            return offset;
        }

        /// <summary>
        /// An emoji is a token by itself. Anything else runs until whitespace or the next emoji.
        /// </summary>
        private static int TokenEnd(string line, int start)
        {
            var first = RuneAt(line, start);
            var end = start + first.Utf16SequenceLength;

            if (IsEmoji(first))
                return end < line.Length && line[end] == VariationSelector16 ? end + 1 : end;

            if (Rune.IsWhiteSpace(first))
                return end;

            while (end < line.Length)
            {
                var next = RuneAt(line, end);
                if (Rune.IsWhiteSpace(next) || IsEmoji(next))
                    break;

                end += next.Utf16SequenceLength;
            }

            return end;
        }

        /// <summary>
        /// Editor text can hold half a surrogate pair. It is treated as one replacement character
        /// that occupies one column.
        /// </summary>
        private static Rune RuneAt(string line, int offset) =>
            Rune.TryGetRuneAt(line, offset, out var rune) ? rune : Rune.ReplacementChar;

        private static bool IsEmoji(Rune rune) => Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherSymbol;
    }
}
