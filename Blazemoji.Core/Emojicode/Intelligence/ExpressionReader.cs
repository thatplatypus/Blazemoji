namespace Blazemoji.Emojicode.Intelligence
{
    public enum ExpressionKind
    {
        Text,
        Number,
        Boolean,
        Variable,

        /// <summary><c>method receiver arguments❗️</c>, or <c>method❗️</c> on the object itself.</summary>
        Call,

        /// <summary><c>🆕Type arguments❗️</c>, or <c>🆕Type▶️name arguments❗️</c>.</summary>
        Initialization,

        /// <summary><c>🐇Type</c>: a type standing where a value does, as the receiver of a type method.</summary>
        TypeValue,

        /// <summary>Operands joined by operators: <c>a ➕ b ▶️ c</c>.</summary>
        Binary,

        /// <summary><c>🤜 expression 🤛</c>, <c>🍺 expression</c>: the same value, written differently.</summary>
        Wrapped,

        /// <summary><c>🔲 expression Type</c>.</summary>
        Cast,

        /// <summary>A closure, a collection literal, or anything else that is one value of no type this reader can name.</summary>
        Other,
    }

    /// <summary>
    /// One expression as the tokens spell it out. It says where the expression starts and
    /// ends and what it is made of, and nothing about types.
    /// </summary>
    public sealed class Expression(ExpressionKind kind, int first)
    {
        public ExpressionKind Kind => kind;

        /// <summary>Index of its first token.</summary>
        public int First => first;

        /// <summary>Index just past the last token read as part of it.</summary>
        public int Next { get; set; } = first + 1;

        /// <summary>False when it had not ended where the reading stopped.</summary>
        public bool Complete { get; set; } = true;

        /// <summary>
        /// It is incomplete because the tokens ran out, which is what an expression still
        /// being typed looks like. False when something that cannot belong to it was met.
        /// </summary>
        public bool RanOut { get; set; }

        /// <summary>The method's token for a call; the type's token for an initialization, a type value and a cast. -1 otherwise.</summary>
        public int NameToken { get; set; } = -1;

        /// <summary>For an initialization: the name after ▶️, or empty.</summary>
        public string InitializerName { get; set; } = string.Empty;

        /// <summary>For a call: what the method is called on. Null for a call on the object itself, and before it is typed.</summary>
        public Expression? Receiver { get; set; }

        public List<Expression> Arguments { get; } = [];

        /// <summary>For <see cref="ExpressionKind.Binary"/>: the operands in order.</summary>
        public List<Expression> Operands { get; } = [];

        /// <summary>For <see cref="ExpressionKind.Binary"/>: each operator's first token and its text, so <c>◀️🙌</c> is one.</summary>
        public List<(int Token, string Text)> Operators { get; } = [];

        /// <summary>For <see cref="ExpressionKind.Wrapped"/> and <see cref="ExpressionKind.Cast"/>.</summary>
        public Expression? Inner { get; set; }
    }

    /// <summary>
    /// Reads expressions the way the compiler's parser does, as far as the editor needs: a
    /// call is a method, what it is called on, and arguments up to its ❗️ or ❓, and each of
    /// those is itself an expression. Every call gets read this way whether or not anything
    /// is known about its method, which is what keeps one call's ❗️ from being taken for
    /// another's.
    /// </summary>
    public sealed class ExpressionReader(IReadOnlyList<SourceToken> tokens)
    {
        private static readonly HashSet<string> Moods = ["❗", "❓"];

        private static readonly HashSet<string> Operators =
            ["➕", "➖", "➗", "✖", "👐", "🤝", "⭕", "💢", "❌", "👈", "👉", "🚮", "🙌", "😜", "◀", "▶"];

        // What a statement starts or is joined with. None of these can begin a value.
        private static readonly HashSet<string> NeverAValue =
            ["➡", "⬅", "🍉", "🤛", "🍆", "🐚", "↩", "🔁", "🔂", "🚨", "↪", "🙅", "🆗", "☣", "🖍", "🎍", "🥡", "🍬"];

        private static readonly HashSet<string> TypesAsValues = ["🐇", "🕊", "🔘", "🐊"];

        // Each value inside another is a step down the stack, and pasted text can nest
        // without end (a line of nothing but emoji is a call in a call in a call). Past
        // this depth the text is not code anyone wrote, and reading stops.
        private const int DeepestNesting = 100;

        private int _depth;

        public static bool IsOperator(SourceToken token) => token.Kind == TokenKind.Symbol && Operators.Contains(EmojiText.Bare(token.Text));

        /// <summary>
        /// Every expression of one statement, in order. Keywords, assignment targets and block
        /// openers are stepped over.
        /// </summary>
        /// <param name="limit">Index just past the last token to read.</param>
        public IReadOnlyList<Expression> ReadStatement(int index, int limit)
        {
            var expressions = new List<Expression>();
            while (index < limit)
            {
                // At the start of a statement 🍇 opens a block. It is a closure only as a value inside a call.
                if (Is(index, "🍇") || ReadExpression(index, limit) is not { } expression)
                {
                    index++;
                    continue;
                }

                expressions.Add(expression);
                if (!expression.Complete && expression.RanOut)
                    break;

                index = Math.Max(expression.Next, index + 1);
            }

            return expressions;
        }

        /// <returns>Null when no expression starts at <paramref name="index"/>.</returns>
        public Expression? ReadExpression(int index, int limit)
        {
            if (ReadOperand(index, limit) is not { } left)
                return null;

            Expression? binary = null;
            var last = left;
            while (last.Complete && OperatorAt(last.Next, limit) is { } found)
            {
                if (binary is null)
                {
                    binary = new Expression(ExpressionKind.Binary, left.First);
                    binary.Operands.Add(left);
                }

                binary.Operators.Add((last.Next, found.Text));
                if (ReadOperand(found.Next, limit) is not { } right)
                {
                    binary.Complete = false;
                    binary.RanOut = found.Next >= limit;
                    binary.Next = found.Next;
                    return binary;
                }

                binary.Operands.Add(right);
                binary.Next = right.Next;
                binary.Complete = right.Complete;
                binary.RanOut = right.RanOut;
                last = right;
            }

            return binary ?? left;
        }

        private (string Text, int Next)? OperatorAt(int index, int limit)
        {
            if (index >= limit || !IsOperator(tokens[index]))
                return null;

            // ◀️🙌 and ▶️🙌 are one operator each.
            var bare = Bare(index);
            if (bare is "◀" or "▶" && index + 1 < limit && Is(index + 1, "🙌") && tokens[index].End == tokens[index + 1].Start)
                return (bare + "🙌", index + 2);

            return (bare, index + 1);
        }

        private Expression? ReadOperand(int index, int limit)
        {
            if (index >= limit || _depth >= DeepestNesting)
                return null;

            _depth++;
            try
            {
                return ReadOperandAt(index, limit);
            }
            finally
            {
                _depth--;
            }
        }

        private Expression? ReadOperandAt(int index, int limit)
        {
            var token = tokens[index];
            switch (token.Kind)
            {
                case TokenKind.Text:
                    return new Expression(ExpressionKind.Text, index);
                case TokenKind.Number:
                    return new Expression(ExpressionKind.Number, index);
                case TokenKind.Word:
                    return new Expression(ExpressionKind.Variable, index);
            }

            var bare = Bare(index);
            if (char.IsAscii(bare[0]) || Moods.Contains(bare) || NeverAValue.Contains(bare) || Operators.Contains(bare))
                return null;

            if (bare is "👍" or "👎")
                return new Expression(ExpressionKind.Boolean, index);

            // This object, and "no value" (🤷 with whatever is joined to it).
            if (bare == "👇" || bare.StartsWith("🤷", StringComparison.Ordinal))
                return new Expression(ExpressionKind.Other, index);

            if (bare is "🍺" or "🔺")
                return Wrapping(index, ReadOperand(index + 1, limit), limit);

            if (bare == "🤜")
                return ReadGroup(index, limit);

            if (bare == "🍇")
                return ReadBalanced(index, limit, "🍇", "🍉");

            if (bare == "🍿")
                return ReadCollection(index, limit);

            if (bare == "🆕")
                return ReadInitialization(index, limit);

            if (TypesAsValues.Contains(bare))
                return ReadTypeValue(index, limit);

            if (bare is "🔲" or "📣")
                return ReadCast(index, limit);

            // The size of a type, and "is this the only reference to this variable".
            if (bare is "⚖" or "🏮")
                return ReadKeywordAndOneMore(index, limit);

            return ReadCall(index, limit);
        }

        private Expression ReadCall(int index, int limit)
        {
            var call = new Expression(ExpressionKind.Call, index) { NameToken = index };
            var next = SkipGenericArguments(index + 1, limit);

            // ⤴️ calls the superclass's initializer, whose name follows. ⁉️ calls a closure.
            if (Is(index, "⤴") && next < limit)
                next++;

            if (next >= limit)
                return Unfinished(call, next, ranOut: true);

            if (IsMood(next))
            {
                call.Next = next + 1;
                return call;
            }

            if (ReadExpression(next, limit) is not { } receiver)
                return Unfinished(call, next, ranOut: false);

            if (Is(index, "⤴"))
                call.Arguments.Add(receiver);
            else
                call.Receiver = receiver;

            if (!receiver.Complete)
                return Unfinished(call, receiver.Next, receiver.RanOut);

            return ReadArguments(call, receiver.Next, limit);
        }

        private Expression? ReadInitialization(int index, int limit)
        {
            var (typeToken, next) = ReadType(index + 1, limit);
            if (typeToken < 0)
                return null;

            var created = new Expression(ExpressionKind.Initialization, index) { NameToken = typeToken };
            if (next + 1 < limit && Is(next, "▶") && tokens[next + 1].Kind == TokenKind.Symbol && tokens[next].End == tokens[next + 1].Start)
            {
                created.InitializerName = Bare(next + 1);
                next += 2;
            }

            return ReadArguments(created, SkipGenericArguments(next, limit), limit);
        }

        private Expression ReadArguments(Expression call, int index, int limit)
        {
            while (true)
            {
                if (index >= limit)
                    return Unfinished(call, index, ranOut: true);

                if (IsMood(index))
                {
                    call.Next = index + 1;
                    return call;
                }

                if (ReadExpression(index, limit) is not { } argument)
                    return Unfinished(call, index, ranOut: false);

                call.Arguments.Add(argument);
                if (!argument.Complete)
                    return Unfinished(call, argument.Next, argument.RanOut);

                index = argument.Next;
            }
        }

        private Expression? ReadTypeValue(int index, int limit)
        {
            var (typeToken, next) = ReadType(index + 1, limit);
            if (typeToken < 0)
                return index + 1 >= limit ? Unfinished(new Expression(ExpressionKind.TypeValue, index), index + 1, ranOut: true) : null;

            return new Expression(ExpressionKind.TypeValue, index) { NameToken = typeToken, Next = next };
        }

        private Expression ReadCast(int index, int limit)
        {
            var cast = new Expression(ExpressionKind.Cast, index);
            if (ReadOperand(index + 1, limit) is not { } inner)
                return Unfinished(cast, index + 1, index + 1 >= limit);

            cast.Inner = inner;
            if (!inner.Complete)
                return Unfinished(cast, inner.Next, inner.RanOut);

            var (typeToken, next) = ReadType(inner.Next, limit);
            if (typeToken < 0 && next == inner.Next)
                return Unfinished(cast, next, next >= limit);

            cast.NameToken = typeToken;
            cast.Next = next;
            return cast;
        }

        private Expression ReadKeywordAndOneMore(int index, int limit)
        {
            var expression = new Expression(ExpressionKind.Other, index);
            if (index + 1 >= limit)
                return Unfinished(expression, index + 1, ranOut: true);

            expression.Next = Is(index, "⚖") ? ReadType(index + 1, limit).Next : index + 2;
            return expression;
        }

        private Expression ReadGroup(int index, int limit)
        {
            var group = new Expression(ExpressionKind.Wrapped, index);
            if (ReadExpression(index + 1, limit) is not { } inner)
                return Unfinished(group, index + 1, index + 1 >= limit);

            group.Inner = inner;
            if (!inner.Complete)
                return Unfinished(group, inner.Next, inner.RanOut);

            if (!Is(inner.Next, "🤛") || inner.Next >= limit)
                return Unfinished(group, inner.Next, inner.Next >= limit);

            group.Next = inner.Next + 1;
            return group;
        }

        private Expression ReadCollection(int index, int limit)
        {
            var collection = new Expression(ExpressionKind.Other, index);
            var next = index + 1;
            while (true)
            {
                if (next >= limit)
                    return Unfinished(collection, next, ranOut: true);

                if (Is(next, "🍆"))
                {
                    collection.Next = next + 1;
                    return collection;
                }

                if (ReadExpression(next, limit) is not { } element)
                    return Unfinished(collection, next, ranOut: false);

                collection.Arguments.Add(element);
                if (!element.Complete)
                    return Unfinished(collection, element.Next, element.RanOut);

                next = element.Next;
            }
        }

        /// <summary>A closure is one value from its 🍇 to the 🍉 that closes it. What is inside is not read.</summary>
        private Expression ReadBalanced(int index, int limit, string open, string close)
        {
            var expression = new Expression(ExpressionKind.Other, index);
            var depth = 0;
            for (var at = index; at < limit; at++)
            {
                if (Is(at, open))
                    depth++;
                else if (Is(at, close) && --depth == 0)
                {
                    expression.Next = at + 1;
                    return expression;
                }
            }

            return Unfinished(expression, limit, ranOut: true);
        }

        private Expression Wrapping(int index, Expression? inner, int limit)
        {
            var wrapped = new Expression(ExpressionKind.Wrapped, index) { Inner = inner };
            if (inner is null)
                return Unfinished(wrapped, index + 1, index + 1 >= limit);

            wrapped.Next = inner.Next;
            wrapped.Complete = inner.Complete;
            wrapped.RanOut = inner.RanOut;
            return wrapped;
        }

        /// <summary>A type as it is written: its emoji, with 🍬 before it or 🐚…🍆 after it, or a closure's type.</summary>
        /// <returns>The emoji's token, or -1 for a type without a name or no type at all, and the index after the type.</returns>
        public (int TypeToken, int Next) ReadType(int index, int limit)
        {
            while (index < limit && Is(index, "🍬"))
                index++;

            if (index >= limit || tokens[index].Kind != TokenKind.Symbol)
                return (-1, index);

            if (Is(index, "🍇"))
                return (-1, ReadBalanced(index, limit, "🍇", "🍉").Next);

            var bare = Bare(index);
            if (char.IsAscii(bare[0]) || Moods.Contains(bare) || NeverAValue.Contains(bare) || Operators.Contains(bare))
                return (-1, index);

            return (index, SkipGenericArguments(index + 1, limit));
        }

        private int SkipGenericArguments(int index, int limit) =>
            index < limit && Is(index, "🐚") ? ReadBalanced(index, limit, "🐚", "🍆").Next : index;

        private static Expression Unfinished(Expression expression, int next, bool ranOut)
        {
            expression.Complete = false;
            expression.RanOut = ranOut;
            expression.Next = next;
            return expression;
        }

        private bool IsMood(int index) => tokens[index].Kind == TokenKind.Symbol && Moods.Contains(Bare(index));

        private string Bare(int index) => EmojiText.Bare(tokens[index].Text);

        private bool Is(int index, string symbol) =>
            index >= 0 && index < tokens.Count && tokens[index].Kind == TokenKind.Symbol && Bare(index) == symbol;
    }
}
