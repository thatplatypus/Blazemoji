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

    /// <summary>What kind of text a position is in.</summary>
    public enum TextContext
    {
        Code,

        /// <summary>Between a 🔤 and the 🔤 that closes it.</summary>
        String,

        Comment,
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
        /// What the end of <paramref name="source"/> is inside of. Given the text before the
        /// cursor, this says whether the next character typed is code, part of a string, or
        /// part of a comment.
        /// </summary>
        public static TextContext ContextAtEnd(string source)
        {
            var position = 0;
            while (position < source.Length)
            {
                if (StartsWith(source, position, BlockCommentOpen))
                {
                    if (!Closes(source, position + BlockCommentOpen.Length, BlockCommentClose, out position))
                        return TextContext.Comment;
                }
                else if (StartsWith(source, position, LineComment))
                {
                    if (!Closes(source, position, "\n", out position))
                        return TextContext.Comment;
                }
                else if (StartsWith(source, position, Documentation))
                {
                    if (!Closes(source, position + Documentation.Length, Documentation, out position))
                        return TextContext.Comment;
                }
                else if (StartsWith(source, position, PackageDocumentation))
                {
                    if (!Closes(source, position + PackageDocumentation.Length, PackageDocumentation, out position))
                        return TextContext.Comment;
                }
                else if (StartsWith(source, position, StringQuote))
                {
                    var line = 0;
                    var closed = SkipString(source, position + StringQuote.Length, ref line, out position);
                    if (!closed)
                        return TextContext.String;
                }
                else
                {
                    position += StringInfo.GetNextTextElementLength(source, position);
                }
            }

            return TextContext.Code;
        }

        private static bool Closes(string source, int from, string closing, out int after)
        {
            var end = source.IndexOf(closing, from, StringComparison.Ordinal);
            after = end < 0 ? source.Length : end + closing.Length;
            return end >= 0;
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
            SkipString(source, position, ref line, out var end);
            return end;
        }

        /// <returns>False when the source ends before the string does.</returns>
        private static bool SkipString(string source, int position, ref int line, out int end)
        {
            end = source.Length;
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
                {
                    end = position + StringQuote.Length;
                    return true;
                }

                if (source[position] == '\n')
                    line++;

                position++;
            }

            return false;
        }
    }
}
