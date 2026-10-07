using System.Globalization;
using System.Text;

namespace Blazemoji.Emojicode.Intelligence
{
    public enum TokenKind
    {
        /// <summary>An emoji or any other symbol: one visible character.</summary>
        Symbol,

        /// <summary>A name made of letters, digits and underscores.</summary>
        Word,

        Number,

        /// <summary>A whole string literal, from its opening 🔤 to its closing one.</summary>
        Text,
    }

    /// <param name="Start">Offset of the first UTF-16 unit in the source.</param>
    /// <param name="End">Offset just past the last one.</param>
    /// <param name="Line">1-based.</param>
    public sealed record SourceToken(TokenKind Kind, string Text, int Start, int End, int Line);

    /// <summary>
    /// Splits Emojicode source into tokens: enough to see calls, assignments and declarations,
    /// which is all the editor's help is built on. It is not the compiler's lexer and never
    /// fails; text it does not understand becomes symbols.
    /// </summary>
    public static class SourceReader
    {
        private const string StringQuote = "🔤";
        private const string Escape = "❌";
        private const string LineComment = "💭";
        private const string BlockCommentOpen = "💭🔜";
        private const string BlockCommentClose = "🔚💭";
        private const string Documentation = "📗";
        private const string PackageDocumentation = "📘";

        public static IReadOnlyList<SourceToken> Read(string source)
        {
            var tokens = new List<SourceToken>();
            var line = 1;
            var position = 0;

            while (position < source.Length)
            {
                var c = source[position];
                if (c == '\n')
                {
                    line++;
                    position++;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    position++;
                    continue;
                }

                if (StartsWith(source, position, BlockCommentOpen))
                {
                    position = SkipPast(source, position + BlockCommentOpen.Length, BlockCommentClose, ref line);
                    continue;
                }

                if (StartsWith(source, position, LineComment))
                {
                    var end = source.IndexOf('\n', position);
                    position = end < 0 ? source.Length : end;
                    continue;
                }

                if (StartsWith(source, position, Documentation))
                {
                    position = SkipPast(source, position + Documentation.Length, Documentation, ref line);
                    continue;
                }

                if (StartsWith(source, position, PackageDocumentation))
                {
                    position = SkipPast(source, position + PackageDocumentation.Length, PackageDocumentation, ref line);
                    continue;
                }

                if (StartsWith(source, position, StringQuote))
                {
                    var startLine = line;
                    var end = SkipString(source, position + StringQuote.Length, ref line);
                    tokens.Add(new SourceToken(TokenKind.Text, source[position..end], position, end, startLine));
                    position = end;
                    continue;
                }

                if (IsWordCharacter(source, position))
                {
                    var end = position;
                    while (end < source.Length && (IsWordCharacter(source, end) || IsDecimalPoint(source, position, end)))
                        end++;

                    var text = source[position..end];
                    tokens.Add(new SourceToken(char.IsAsciiDigit(text[0]) ? TokenKind.Number : TokenKind.Word, text, position, end, line));
                    position = end;
                    continue;
                }

                var length = StringInfo.GetNextTextElementLength(source, position);
                tokens.Add(new SourceToken(TokenKind.Symbol, source.Substring(position, length), position, position + length, line));
                position += length;
            }

            return tokens;
        }

        /// <summary>
        /// A letter, digit or underscore standing on its own. A digit that starts a keycap emoji
        /// (1️⃣) is part of that emoji, not of a number.
        /// </summary>
        private static bool IsWordCharacter(string source, int position)
        {
            var c = source[position];
            if (c != '_' && !char.IsLetterOrDigit(c))
                return false;

            return StringInfo.GetNextTextElementLength(source, position) == 1;
        }

        private static bool IsDecimalPoint(string source, int start, int position) =>
            source[position] == '.'
            && char.IsAsciiDigit(source[start])
            && position + 1 < source.Length
            && char.IsAsciiDigit(source[position + 1]);

        private static bool StartsWith(string source, int position, string text) =>
            string.CompareOrdinal(source, position, text, 0, text.Length) == 0;

        /// <returns>The offset just past the closing marker, or the end of the source.</returns>
        private static int SkipPast(string source, int position, string closing, ref int line)
        {
            var end = source.IndexOf(closing, position, StringComparison.Ordinal);
            var stop = end < 0 ? source.Length : end + closing.Length;
            line += source.AsSpan(position, stop - position).Count('\n');
            return stop;
        }

        private static int SkipString(string source, int position, ref int line)
        {
            while (position < source.Length)
            {
                if (StartsWith(source, position, Escape))
                {
                    position += Escape.Length;
                    if (position < source.Length)
                        position += StringInfo.GetNextTextElementLength(source, position);

                    continue;
                }

                if (StartsWith(source, position, StringQuote))
                    return position + StringQuote.Length;

                if (source[position] == '\n')
                    line++;

                position++;
            }

            return source.Length;
        }
    }
}
