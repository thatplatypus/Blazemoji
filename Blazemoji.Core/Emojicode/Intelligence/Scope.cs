namespace Blazemoji.Emojicode.Intelligence
{
    /// <param name="TypeName">The emoji of the variable's type. It may be a type no package documents.</param>
    public sealed record ScopeVariable(string Name, string TypeName, int Position);

    /// <summary>
    /// The variables of a source file and the types they can be seen to have. This is not type
    /// checking. It reads the handful of shapes that say what a variable is outright: an
    /// assignment from an initializer, a literal or a documented call, and a declaration with
    /// its type written out. Anything else stays unknown: a wrong type would be stated as
    /// fact in a hover and would write wrong code from a suggestion, so nothing is guessed.
    /// </summary>
    public sealed class Scope
    {
        private static readonly HashSet<string> Modifiers = ["🔓", "🔒", "🔐", "🐇", "✒", "🖍", "🎍", "🥡", "🔏", "📻", "🌍", "⚠"];
        private static readonly HashSet<string> Moods = ["❗", "❓"];
        private static readonly HashSet<string> TypeDeclarations = ["🐇", "🕊", "🔘", "🦃", "🐊"];

        private readonly List<ScopeVariable> _variables = [];
        private readonly HashSet<string> _typesOfThisFile = [];
        private readonly List<(int First, int Next)> _declarations = [];
        private readonly IReadOnlyList<SourceToken> _tokens;
        private readonly TypeIndex _types;
        private readonly ExpressionReader _expressions;

        private Scope(IReadOnlyList<SourceToken> tokens, TypeIndex types)
        {
            _tokens = tokens;
            _types = types;
            _expressions = new ExpressionReader(tokens);
        }

        public IReadOnlyList<ScopeVariable> Variables => _variables;

        public static Scope Read(IReadOnlyList<SourceToken> tokens, TypeIndex types)
        {
            var scope = new Scope(tokens, types);
            for (var index = 0; index < tokens.Count; index++)
                scope.ReadTypeDeclarationAt(index);

            for (var index = 0; index < tokens.Count; index++)
                scope.ReadAt(index);

            return scope;
        }

        /// <summary>
        /// True for a token that is part of a declaration: a method's or initializer's
        /// header, a variable declared with its type, a closure's parameters. Nothing there
        /// is a call, however much <c>🔢 aTitle</c> looks like one.
        /// </summary>
        public bool IsDeclaration(int tokenIndex) => _declarations.Any(range => range.First <= tokenIndex && tokenIndex < range.Next);

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
        /// The type of the expression that is exactly the tokens from <paramref name="start"/>
        /// up to <paramref name="end"/>. Null when they are not one whole expression, or its
        /// type cannot be seen.
        /// </summary>
        public string? TypeOfExpression(int start, int end) =>
            _expressions.ReadExpression(start, end) is { Complete: true } expression && expression.Next == end ? TypeOf(expression) : null;

        /// <summary>The type a value of this expression has, when the documentation and the declarations above say.</summary>
        public string? TypeOf(Expression? expression)
        {
            switch (expression?.Kind)
            {
                case ExpressionKind.Text:
                    return "🔡";

                case ExpressionKind.Number:
                    return _tokens[expression.First].Text.Contains('.') ? "💯" : "🔢";

                case ExpressionKind.Boolean:
                    return "👌";

                case ExpressionKind.Variable:
                    return TypeOf(_tokens[expression.First].Text, _tokens[expression.First].Start);

                case ExpressionKind.Initialization:
                case ExpressionKind.Cast when expression.NameToken >= 0:
                    return _tokens[expression.NameToken].Text;

                case ExpressionKind.Wrapped:
                    return TypeOf(expression.Inner);

                case ExpressionKind.Call:
                    return MethodOf(expression)?.ReturnType?.TypeName;

                case ExpressionKind.Binary:
                    // Read left to right. Where that is not how the operators group, the
                    // left side has no such operator and the type stays unknown.
                    var type = TypeOf(expression.Operands[0]);
                    foreach (var (_, text) in expression.Operators)
                        type = _types.FindMethod(type, text)?.ReturnType?.TypeName;

                    return type;

                default:
                    return null;
            }
        }

        /// <summary>The documented method a call is to, going by its receiver. Null for a call on the object itself.</summary>
        public MethodDocumentation? MethodOf(Expression call)
        {
            if (call.Kind != ExpressionKind.Call || call.Receiver is not { } receiver)
                return null;

            var name = _tokens[call.NameToken].Text;
            return receiver is { Kind: ExpressionKind.TypeValue, NameToken: >= 0 }
                ? _types.FindTypeMethod(_tokens[receiver.NameToken].Text, name)
                : _types.FindMethod(TypeOf(receiver), name);
        }

        private void ReadAt(int index)
        {
            if (Is(index, "➡"))
                ReadAssignment(index);
            else if (Is(index, "🆗"))
                ReadErrorCheck(index);
            else if (Is(index, "🍇"))
                ReadParameters(SkipAll(index + 1, "🎍", "🥡"), onlyKnownTypes: true);
            else if (IsFirstOnLine(index) && Moods.Contains(Bare(index)))
                ReadMethodHeader(index);
            else if (IsFirstOnLine(index) && Is(index, "🆕"))
                ReadInitializerOrInstanceVariable(index);
        }

        /// <summary><c>🐇 Name</c> and the like at the start of a line declare a type of this file.</summary>
        private void ReadTypeDeclarationAt(int index)
        {
            if (_tokens[index].Kind == TokenKind.Symbol
                && TypeDeclarations.Contains(Bare(index))
                && IsFirstOnLine(index)
                && SameLineSymbol(index, index + 1)
                && !Moods.Contains(Bare(index + 1)))
            {
                _typesOfThisFile.Add(Bare(index + 1));
            }
        }

        /// <summary><c>❗️ name parameters ➡️ Type 🍇</c>.</summary>
        private void ReadMethodHeader(int mood)
        {
            if (!SameLineSymbol(mood, mood + 1) || Is(mood + 1, "➡"))
                return;

            DeclaresToEndOfLine(mood);
            ReadParameters(mood + 2, onlyKnownTypes: false);
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
            // A named initializer: 🆕 ▶️🐴 capacity 🔢.
            var next = index + 1;
            if (Is(next, "▶") && SameLineSymbol(next, next + 1))
                next += 2;

            var name = SkipAll(next, "🍼");
            if (name >= _tokens.Count || _tokens[name].Kind != TokenKind.Word || _tokens[name].Line != _tokens[index].Line)
                return;

            DeclaresToEndOfLine(index);
            ReadParameters(next, onlyKnownTypes: false);
        }

        /// <summary>
        /// Reads <c>name Type name Type ...</c> for as long as that is what is there on the line.
        /// </summary>
        /// <param name="onlyKnownTypes">
        /// After a 🍇 the same shape is also how a statement can begin (<c>total ➕ 1</c>), so
        /// there a name is only a parameter when what follows it is a type of an imported
        /// package or of this file.
        /// </param>
        private void ReadParameters(int index, bool onlyKnownTypes)
        {
            if (index >= _tokens.Count)
                return;

            var line = _tokens[index].Line;
            var end = index;
            while (end < _tokens.Count && _tokens[end].Line == line)
                end++;

            while (true)
            {
                index = SkipAll(index, "🍼");
                if (index + 1 >= end || _tokens[index].Kind != TokenKind.Word)
                    return;

                var (typeToken, next) = _expressions.ReadType(index + 1, end);
                if (next == index + 1)
                    return;

                // A closure's type has no name to give the variable, and is still a type.
                if (typeToken >= 0)
                {
                    var type = _tokens[typeToken].Text;
                    if (onlyKnownTypes && _types.Find(type) is null && !_typesOfThisFile.Contains(EmojiText.Bare(type)))
                        return;

                    Declare(_tokens[index], type);
                }

                _declarations.Add((index, next));
                index = next;
            }
        }

        private void DeclaresToEndOfLine(int index)
        {
            var first = index;
            while (first > 0 && _tokens[first - 1].Line == _tokens[index].Line)
                first--;

            var next = index;
            while (next < _tokens.Count && _tokens[next].Line == _tokens[index].Line)
                next++;

            _declarations.Add((first, next));
        }

        private bool SameLineSymbol(int index, int other) =>
            other < _tokens.Count && _tokens[other].Kind == TokenKind.Symbol && _tokens[other].Line == _tokens[index].Line;

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
