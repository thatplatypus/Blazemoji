namespace Blazemoji.Emojicode.Intelligence
{
    /// <param name="TypeName">The emoji of the variable's type. It may be a type no package documents.</param>
    public sealed record ScopeVariable(string Name, string TypeName, int Position);

    /// <summary>
    /// The variables of a source file and the types they can be seen to have. This is not type
    /// checking. It reads the handful of shapes that say what a variable is outright: an
    /// assignment from an initializer, a literal or a documented call, and a declaration with
    /// its type written out. Anything else stays unknown.
    /// </summary>
    public sealed class Scope
    {
        private static readonly HashSet<string> Modifiers = ["🔓", "🔒", "🔐", "🐇", "✒", "🖍", "🎍", "🥡", "🔏", "📻", "🌍", "⚠"];
        private static readonly HashSet<string> Moods = ["❗", "❓"];

        private readonly List<ScopeVariable> _variables = [];
        private readonly IReadOnlyList<SourceToken> _tokens;
        private readonly TypeIndex _types;

        private Scope(IReadOnlyList<SourceToken> tokens, TypeIndex types)
        {
            _tokens = tokens;
            _types = types;
        }

        public IReadOnlyList<ScopeVariable> Variables => _variables;

        public static Scope Read(IReadOnlyList<SourceToken> tokens, TypeIndex types)
        {
            var scope = new Scope(tokens, types);
            for (var index = 0; index < tokens.Count; index++)
                scope.ReadAt(index);

            return scope;
        }

        /// <summary>
        /// The type of the nearest declaration of <paramref name="name"/> above
        /// <paramref name="offset"/>, or failing that the nearest below, which is how an
        /// instance variable declared further down is found.
        /// </summary>
        public string? TypeOf(string name, int offset)
        {
            var declared = _variables.Where(variable => variable.Name == name).ToList();
            return (declared.LastOrDefault(variable => variable.Position <= offset) ?? declared.FirstOrDefault())?.TypeName;
        }

        /// <summary>Each variable once, the ones declared nearest above <paramref name="offset"/> first.</summary>
        public IReadOnlyList<ScopeVariable> Near(int offset) =>
            _variables
                .OrderBy(variable => variable.Position <= offset ? offset - variable.Position : int.MaxValue / 2 + variable.Position)
                .DistinctBy(variable => variable.Name)
                .ToList();

        /// <summary>
        /// The type an expression can be seen to have, from its first tokens. Null when it cannot.
        /// </summary>
        public string? TypeOfExpression(int start, int end)
        {
            while (start < end && Is(start, "🍺"))
                start++;

            if (start >= end)
                return null;

            var first = _tokens[start];
            switch (first.Kind)
            {
                case TokenKind.Text:
                    return "🔡";

                case TokenKind.Number:
                    return first.Text.Contains('.') ? "💯" : "🔢";

                case TokenKind.Word:
                    return TypeOf(first.Text, first.Start);
            }

            if (Is(start, "👍") || Is(start, "👎"))
                return "👌";

            if (start + 1 >= end)
                return null;

            var second = _tokens[start + 1];
            if (Is(start, "🆕"))
                return second.Kind == TokenKind.Symbol ? second.Text : null;

            if (Is(start + 1, "🐇") && start + 2 < end)
                return _types.FindTypeMethod(_tokens[start + 2].Text, first.Text)?.ReturnType?.TypeName;

            if (second.Kind == TokenKind.Word)
                return _types.FindMethod(TypeOf(second.Text, second.Start), first.Text)?.ReturnType?.TypeName;

            return null;
        }

        private void ReadAt(int index)
        {
            if (Is(index, "➡"))
                ReadAssignment(index);
            else if (Is(index, "🆗"))
                ReadErrorCheck(index);
            else if (Is(index, "🍇"))
                ReadParameters(SkipAll(index + 1, "🎍", "🥡"));
            else if (IsFirstOnLine(index) && Moods.Contains(Bare(index)) && index + 1 < _tokens.Count)
                ReadParameters(index + 2);
            else if (IsFirstOnLine(index) && Is(index, "🆕"))
                ReadInitializerOrInstanceVariable(index);
        }

        /// <summary><c>expression ➡️ name</c>, <c>expression ➡️ 🖍🆕 name</c>.</summary>
        private void ReadAssignment(int arrow)
        {
            var target = SkipAll(arrow + 1, "🖍", "🆕");
            if (target >= _tokens.Count || _tokens[target].Kind != TokenKind.Word || _tokens[target].Line != _tokens[arrow].Line)
                return;

            var start = arrow;
            while (start > 0 && _tokens[start - 1].Line == _tokens[arrow].Line && !Is(start - 1, "🍇"))
                start--;

            // "if this has a value, call it name": the condition keyword is not part of the expression.
            if (start < arrow && Is(start, "↪"))
                start++;

            if (TypeOfExpression(start, arrow) is { } type)
                Declare(_tokens[target], type);
        }

        /// <summary><c>🆗 name expression 🍇</c>: the expression's value when it did not fail.</summary>
        private void ReadErrorCheck(int index)
        {
            if (index + 2 >= _tokens.Count || _tokens[index + 1].Kind != TokenKind.Word)
                return;

            var end = index + 2;
            while (end < _tokens.Count && _tokens[end].Line == _tokens[index].Line && !Is(end, "🍇"))
                end++;

            if (TypeOfExpression(index + 2, end) is { } type)
                Declare(_tokens[index + 1], type);
        }

        /// <summary>
        /// <c>🆕 name Type 🍇</c> declares an initializer's parameters, and <c>🖍🆕 name Type</c>
        /// an instance variable. <c>🆕Type❗️</c> at the start of a line is neither.
        /// </summary>
        private void ReadInitializerOrInstanceVariable(int index)
        {
            var next = SkipAll(index + 1, "🍼");
            if (next < _tokens.Count && _tokens[next].Kind == TokenKind.Word)
                ReadParameters(index + 1);
        }

        /// <summary>Reads <c>name Type name Type ...</c> for as long as that is what is there.</summary>
        private void ReadParameters(int index)
        {
            while (true)
            {
                index = SkipAll(index, "🍼");
                if (index + 1 >= _tokens.Count || _tokens[index].Kind != TokenKind.Word || _tokens[index + 1].Kind != TokenKind.Symbol)
                    return;

                var (type, next) = ReadType(index + 1);
                if (next == index + 1)
                    return;

                if (type is not null)
                    Declare(_tokens[index], type);

                index = next;
            }
        }

        /// <returns>The type's name, or null for a type without one such as a closure, and the index after it.</returns>
        private (string? TypeName, int Next) ReadType(int index)
        {
            index = SkipAll(index, "🍬");
            if (index >= _tokens.Count || _tokens[index].Kind != TokenKind.Symbol)
                return (null, index);

            if (Is(index, "🍇"))
                return (null, SkipBalanced(index, "🍇", "🍉"));

            var name = _tokens[index].Text;
            if (Moods.Contains(Bare(index)) || Is(index, "➡") || Is(index, "🍉"))
                return (null, index);

            var next = index + 1;
            if (next < _tokens.Count && Is(next, "🐚"))
                next = SkipBalanced(next, "🐚", "🍆");

            return (name, next);
        }

        private int SkipBalanced(int index, string open, string close)
        {
            var depth = 0;
            for (; index < _tokens.Count; index++)
            {
                if (Is(index, open))
                    depth++;
                else if (Is(index, close) && --depth == 0)
                    return index + 1;
            }

            return index;
        }

        private int SkipAll(int index, params string[] symbols)
        {
            while (index < _tokens.Count && symbols.Any(symbol => Is(index, symbol)))
                index++;

            return index;
        }

        /// <summary>True when only modifiers stand between the token and the start of its line.</summary>
        private bool IsFirstOnLine(int index)
        {
            for (var before = index - 1; before >= 0 && _tokens[before].Line == _tokens[index].Line; before--)
            {
                if (_tokens[before].Kind != TokenKind.Symbol || !Modifiers.Contains(Bare(before)))
                    return false;
            }

            return true;
        }

        private void Declare(SourceToken name, string type) => _variables.Add(new ScopeVariable(name.Text, type, name.Start));

        private string Bare(int index) => EmojiText.Bare(_tokens[index].Text);

        private bool Is(int index, string symbol) =>
            index >= 0 && index < _tokens.Count && _tokens[index].Kind == TokenKind.Symbol && Bare(index) == EmojiText.Bare(symbol);
    }
}
