using Blazemoji.Emojicode.Intelligence;

namespace Blazemoji.Emojicode.Editing
{
    /// <param name="Before">Everything from the start of the file to the start of the selection.</param>
    /// <param name="Selected">The selected text. Empty when there is only a cursor.</param>
    /// <param name="After">From the end of the selection to the end of that line.</param>
    public sealed record TextAroundCursor(string Before, string Selected, string After);

    /// <summary>
    /// One change to the text for something typed. The text from <paramref name="RemoveBefore"/>
    /// UTF-16 units before the selection to <paramref name="RemoveAfter"/> units after it is
    /// replaced with <paramref name="Text"/>, and the selection then runs from
    /// <paramref name="SelectionStart"/> to <paramref name="SelectionEnd"/>, both counted from
    /// the start of what was put in. The two are equal when only a cursor is left.
    /// </summary>
    public sealed record TypingEdit(int RemoveBefore, int RemoveAfter, string Text, int SelectionStart, int SelectionEnd)
    {
        /// <summary>The text goes where the selection was and the cursor after it.</summary>
        public static TypingEdit Plain(string text) => new(0, 0, text, text.Length, text.Length);
    }

    /// <summary>
    /// What typing one half of a pair does in code: an opener brings its closer, a closer that
    /// is already there is stepped over, and the 🍉 that ends a block lines up with the 🍇 that
    /// began it. Monaco does all this for brackets of one UTF-16 unit and none of it for emoji,
    /// which are two, so it is worked out here from the text around the cursor.
    /// </summary>
    public static class Typing
    {
        // After one of these an opener's closer would not be in anything's way.
        private static readonly string[] _endsOfACall = ["❗", "❓"];

        private static readonly HashSet<string> _halves =
            [.. EmojicodePairs.Completed.SelectMany(pair => new[] { pair.Open, pair.Close })];

        /// <summary>True for text that <see cref="For(string, TextAroundCursor)"/> may type differently from place to place.</summary>
        public static bool DependsOnWhatIsAround(string typed) => _halves.Contains(typed);

        /// <summary>
        /// What is typed where nothing around the cursor matters: the text, with the cursor
        /// after it. The one exception is the key that types both marks of a block comment,
        /// which leaves the cursor after the first, where the comment is written.
        /// </summary>
        public static TypingEdit For(string typed)
        {
            var open = EmojicodePairs.BlockComment.Open;
            var close = EmojicodePairs.BlockComment.Close;
            var bothMarks = typed.Length > open.Length + close.Length
                && typed.StartsWith(open, StringComparison.Ordinal)
                && typed.EndsWith(close, StringComparison.Ordinal);

            return bothMarks ? new TypingEdit(0, 0, typed, open.Length, open.Length) : TypingEdit.Plain(typed);
        }

        public static TypingEdit For(string typed, TextAroundCursor around) =>
            For(typed, around, SourceReader.ContextAtEnd(around.Before));

        /// <param name="context">What <see cref="TextAroundCursor.Before"/> ends inside of, for a caller that has already worked it out.</param>
        public static TypingEdit For(string typed, TextAroundCursor around, TextContext context)
        {
            var nothingSelected = around.Selected.Length == 0;

            if (context == TextContext.String)
            {
                var endsTheString = typed == EmojicodePairs.String.Close && !EndsEscaping(around.Before);
                return endsTheString && nothingSelected && StartsWith(around.After, typed) ? StepOver(typed) : For(typed);
            }

            if (context == TextContext.Comment)
                return For(typed);

            var opened = EmojicodePairs.Completed.FirstOrDefault(pair => pair.Open == typed);
            if (!nothingSelected)
            {
                return opened is null
                    ? For(typed)
                    : new TypingEdit(0, 0, opened.Open + around.Selected + opened.Close, opened.Open.Length, opened.Open.Length + around.Selected.Length);
            }

            var closes = EmojicodePairs.Completed.Any(pair => pair.Close == typed && pair.Open != typed);
            if (closes && StartsWith(around.After, typed))
                return StepOver(typed);

            if (opened is not null)
            {
                return CouldFollowACloser(around.After)
                    ? new TypingEdit(0, 0, opened.Open + opened.Close, opened.Open.Length, opened.Open.Length)
                    : For(typed);
            }

            if (typed == EmojicodePairs.Block.Close && LinedUpWithItsOpener(around.Before) is { } linedUp)
                return linedUp;

            return For(typed);
        }

        // Replacing the closer with itself moves the cursor past it and leaves the text as it is.
        private static TypingEdit StepOver(string closer) => new(0, closer.Length, closer, closer.Length, closer.Length);

        private static bool StartsWith(string text, string start) => text.StartsWith(start, StringComparison.Ordinal);

        /// <summary>
        /// True when the text ends with an odd number of ❌: the character typed next is escaped.
        /// Two of them are one ❌ written out, and escape nothing.
        /// </summary>
        private static bool EndsEscaping(string text)
        {
            var escape = EmojicodePairs.Escape;
            var marks = 0;
            for (var at = text.Length - escape.Length; at >= 0 && string.CompareOrdinal(text, at, escape, 0, escape.Length) == 0; at -= escape.Length)
                marks++;

            return marks % 2 == 1;
        }

        private static bool CouldFollowACloser(string after) =>
            after.Length == 0
            || char.IsWhiteSpace(after[0])
            || EmojicodePairs.Completed.Any(pair => pair.Open != pair.Close && StartsWith(after, pair.Close))
            || _endsOfACall.Any(end => StartsWith(after, end));

        /// <summary>
        /// A 🍉 typed on a line that has nothing on it yet, given the indentation of the line
        /// that holds the 🍇 it closes. Null when the line has something on it or no block is open.
        /// </summary>
        private static TypingEdit? LinedUpWithItsOpener(string before)
        {
            var lineStart = before.LastIndexOf('\n') + 1;
            var indentation = before[lineStart..];
            if (!indentation.All(IsIndentation))
                return null;

            var open = new Stack<int>();
            foreach (var token in SourceReader.Read(before))
            {
                if (token.Kind != TokenKind.Symbol)
                    continue;

                if (token.Text == EmojicodePairs.Block.Open)
                    open.Push(token.Start);
                else if (token.Text == EmojicodePairs.Block.Close && open.Count > 0)
                    open.Pop();
            }

            if (open.Count == 0)
                return null;

            var opener = open.Peek();
            var openerLineStart = opener == 0 ? 0 : before.LastIndexOf('\n', opener - 1) + 1;
            var openerIndentationEnd = openerLineStart;
            while (openerIndentationEnd < before.Length && IsIndentation(before[openerIndentationEnd]))
                openerIndentationEnd++;

            var text = before[openerLineStart..openerIndentationEnd] + EmojicodePairs.Block.Close;
            return new TypingEdit(indentation.Length, 0, text, text.Length, text.Length);
        }

        private static bool IsIndentation(char c) => c is ' ' or '\t';
    }
}
