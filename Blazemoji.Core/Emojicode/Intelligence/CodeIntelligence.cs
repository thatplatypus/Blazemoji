using System.Text;

namespace Blazemoji.Emojicode.Intelligence
{
    public sealed class CodeIntelligence(IEnumerable<EmojicodeKeyword> keywords, IEmojiNames emojiNames) : ICodeIntelligence
    {
        public const string StandardPackage = "s";

        private const int MostEmoji = 25;

        // A word this short matches too much to be worth searching descriptions for.
        private const int ShortestDescriptionSearch = 3;

        // The type the standard package uses for raw memory. An initializer that takes it
        // is for the package's own use and is the last one to suggest.
        private const string RawMemory = "🧠";

        private readonly IReadOnlyList<CatalogKeyword> _keywords = keywords.Select(CatalogKeyword.From).ToList();

        public IReadOnlyList<string> Imports(string text)
        {
            var tokens = SourceReader.Read(text);
            var packages = new List<string> { StandardPackage };

            for (var index = 0; index + 1 < tokens.Count; index++)
            {
                if (tokens[index].Text == "📦" && tokens[index + 1].Kind == TokenKind.Word && !packages.Contains(tokens[index + 1].Text))
                    packages.Add(tokens[index + 1].Text);
            }

            return packages;
        }

        public IReadOnlyList<CompletionEntry> Complete(string text, int offset, IReadOnlyList<PackageDocumentation> packages)
        {
            offset = Math.Clamp(offset, 0, text.Length);
            var before = SourceReader.Read(text[..offset]);
            if (before.Count == 0 || before[^1].End != offset || before[^1].Kind == TokenKind.Text)
                return [];

            var types = new TypeIndex(packages);
            var scope = Scope.Read(SourceReader.Read(text), types);

            if (ReceiverBeforeDot(before) is { } dotted)
                return MethodsOf(dotted.Receiver, dotted.Filter, offset, scope, types);

            var last = before[^1];
            if (last.Kind != TokenKind.Word)
                return [];

            var afterColon = before.Count >= 2 && before[^2].Text == ":" && before[^2].End == last.Start;
            if (!afterColon && last.Text.Length < 2)
                return [];

            return Named(last.Text, afterColon ? before[^2].Start : last.Start, offset, includeVariables: !afterColon, scope, types);
        }

        public HoverInfo? Hover(string text, int offset, IReadOnlyList<PackageDocumentation> packages)
        {
            var tokens = SourceReader.Read(text);
            var index = IndexAt(tokens, offset);
            if (index < 0)
                return null;

            var token = tokens[index];
            var types = new TypeIndex(packages);
            var scope = Scope.Read(tokens, types);

            if (token.Kind == TokenKind.Word)
            {
                return scope.TypeOf(token.Text, token.Start) is { } variableType
                    ? new HoverInfo(token.Start, token.End, DescribeVariable(token.Text, variableType, types))
                    : null;
            }

            if (token.Kind != TokenKind.Symbol)
                return null;

            // What is being called here comes first. When the keyword catalog teaches the same
            // emoji, its lesson and example follow.
            var called = DescribeCallAt(tokens, index, scope, types) ?? DescribeOperatorAt(tokens, index, scope, types);
            var markdown = called is not null && DescribeKeyword(token.Text) is { } lesson
                ? $"{called}\n\n---\n\n{lesson}"
                : called
                    ?? DescribeType(tokens, index, types)
                    ?? DescribeKeyword(token.Text)
                    ?? DescribeAnyMethod(token.Text, types)
                    ?? DescribeEmoji(token.Text);

            return markdown is null ? null : new HoverInfo(token.Start, token.End, markdown);
        }

        public SignatureInfo? Signature(string text, int offset, IReadOnlyList<PackageDocumentation> packages)
        {
            offset = Math.Clamp(offset, 0, text.Length);
            var tokens = SourceReader.Read(text);
            var types = new TypeIndex(packages);
            var scope = Scope.Read(tokens, types);

            // Only the line the cursor is on is read. A call whose arguments run over several
            // lines is nearly always one with a closure for a body, and inside that body the
            // parameters of the outer call are no longer what the writer needs.
            var lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
            var first = 0;
            while (first < tokens.Count && tokens[first].Start < lineStart)
                first++;

            var limit = first;
            while (limit < tokens.Count && tokens[limit].End <= offset)
                limit++;

            // A name or number the cursor is still touching is the argument being typed, not one already given.
            if (limit > first && tokens[limit - 1].End == offset && tokens[limit - 1].Kind is TokenKind.Word or TokenKind.Number)
                limit--;

            if (limit == first || scope.IsDeclaration(limit - 1))
                return null;

            var statement = new ExpressionReader(tokens).ReadStatement(first, limit);
            return statement.Count > 0 && statement[^1] is { Complete: false, RanOut: true } open
                ? SignatureWithin(open, tokens, scope, types)
                : null;
        }

        /// <summary>
        /// The call the cursor is in: the innermost one whose method is known. A call nothing
        /// is known about is, to the call around it, an argument still being typed.
        /// </summary>
        private static SignatureInfo? SignatureWithin(Expression open, IReadOnlyList<SourceToken> tokens, Scope scope, TypeIndex types)
        {
            var inner = open.Kind switch
            {
                ExpressionKind.Call or ExpressionKind.Initialization => open.Arguments.Count > 0 ? open.Arguments[^1] : open.Receiver,
                ExpressionKind.Binary => open.Operands[^1],
                ExpressionKind.Wrapped or ExpressionKind.Cast => open.Inner,
                _ => null,
            };

            // An unfinished closure or list means the cursor is inside it, where the call around it has nothing to say.
            if (open.Kind == ExpressionKind.Other)
                return null;

            if (inner is { Complete: false, RanOut: true })
            {
                if (inner.Kind == ExpressionKind.Other)
                    return null;

                if (SignatureWithin(inner, tokens, scope, types) is { } found)
                    return found;
            }

            if (open.Kind is not (ExpressionKind.Call or ExpressionKind.Initialization) || Resolve(open, tokens, scope, types) is not { } call)
                return null;

            var typing = open.Arguments.Count > 0 && !open.Arguments[^1].Complete ? open.Arguments.Count - 1 : open.Arguments.Count;
            return Describe(call.Method, call.Subject, call.Form, Math.Min(typing, call.Method.Parameters.Count));
        }

        /// <summary>
        /// The documented method or initializer a call is to. Where there are several by the
        /// same name, the one that fits the arguments given so far.
        /// </summary>
        private static (MethodDocumentation Method, string Subject, CallForm Form)? Resolve(Expression call, IReadOnlyList<SourceToken> tokens, Scope scope, TypeIndex types)
        {
            if (call.Kind == ExpressionKind.Initialization)
            {
                if (types.Find(tokens[call.NameToken].Text) is not { } created)
                    return null;

                var forms = created.Initializers.Where(initializer => EmojiText.Same(initializer.Name, call.InitializerName)).ToList();
                return forms.Count == 0 ? null : (BestFit(forms, call, scope), created.Name, CallForm.Initializer);
            }

            if (call.Kind != ExpressionKind.Call || call.Receiver is not { } receiver)
                return null;

            var name = tokens[call.NameToken].Text;
            if (receiver is { Kind: ExpressionKind.TypeValue, NameToken: >= 0 })
            {
                var typeName = tokens[receiver.NameToken].Text;
                var typeMethods = types.Find(typeName)?.TypeMethods.Where(method => EmojiText.Same(method.Name, name)).ToList() ?? [];
                return typeMethods.Count == 0 ? null : (BestFit(typeMethods, call, scope), typeName, CallForm.TypeMethod);
            }

            // After ➡️ the method is one written as an assignment, when the type has such a one.
            var assigned = call.First > 0 && EmojiText.Bare(tokens[call.First - 1].Text) == "➡";
            var methods = types.Find(scope.TypeOf(receiver))?.Methods
                .Where(method => EmojiText.Same(method.Name, name) && !method.IsOperator)
                .OrderBy(method => method.IsAssignment == assigned ? 0 : 1)
                .ToList() ?? [];
            if (methods.Count == 0)
                return null;

            // A receiver without a name is shown as its type: 😀 🔡❗️.
            var subject = receiver.Kind == ExpressionKind.Variable ? tokens[receiver.First].Text : scope.TypeOf(receiver) ?? "…";
            return (BestFit(methods.Where(method => method.IsAssignment == methods[0].IsAssignment).ToList(), call, scope), subject, CallForm.Method);
        }

        /// <summary>
        /// Of several forms with one name: one whose parameters match the types of the
        /// arguments given, with room for the argument being typed, and not one for the
        /// standard package's own use. Ties go to the shorter, then to the first documented.
        /// </summary>
        private static MethodDocumentation BestFit(IReadOnlyList<MethodDocumentation> forms, Expression call, Scope scope)
        {
            if (forms.Count == 1)
                return forms[0];

            var given = call.Arguments.Select(scope.TypeOf).ToList();
            var complete = call.Arguments.Count(argument => argument.Complete);

            int Mismatches(MethodDocumentation form) => given
                .Zip(form.Parameters, (argument, parameter) => argument is not null && parameter.Type.TypeName is { } wanted && !EmojiText.Same(argument, wanted))
                .Count(mismatch => mismatch);

            int Room(MethodDocumentation form) => form.Parameters.Count > complete ? 0 : form.Parameters.Count == complete ? 1 : 2;

            return forms
                .Select((form, order) => (form, order))
                .OrderBy(candidate => Mismatches(candidate.form))
                .ThenBy(candidate => Room(candidate.form))
                .ThenBy(candidate => candidate.form.Parameters.Any(parameter => parameter.Type.TypeName == RawMemory) ? 1 : 0)
                .ThenBy(candidate => candidate.form.Parameters.Count)
                .ThenBy(candidate => candidate.order)
                .First().form;
        }

        private static (string Receiver, string Filter)? ReceiverBeforeDot(IReadOnlyList<SourceToken> before)
        {
            var count = before.Count;
            if (count >= 2 && before[^1].Text == "." && before[^2].Kind == TokenKind.Word && before[^2].End == before[^1].Start)
                return (before[^2].Text, string.Empty);

            if (count >= 3
                && before[^1].Kind == TokenKind.Word
                && before[^2].Text == "."
                && before[^3].Kind == TokenKind.Word
                && before[^3].End == before[^2].Start
                && before[^2].End == before[^1].Start)
            {
                return (before[^3].Text, before[^1].Text);
            }

            return null;
        }

        /// <summary>
        /// The receiver-first shortcut: after <c>app.</c> the methods of <c>app</c>'s type, each
        /// of which replaces <c>app.</c> with the call written the way Emojicode wants it.
        /// </summary>
        private List<CompletionEntry> MethodsOf(string receiver, string filter, int offset, Scope scope, TypeIndex types)
        {
            var start = offset - receiver.Length - 1 - filter.Length;
            if (types.Find(scope.TypeOf(receiver, start)) is not { } type)
                return [];

            // A method written as an assignment needs its value first, which a suggestion
            // after the receiver cannot put there. It is found by name instead.
            var entries = new List<CompletionEntry>();
            foreach (var method in type.Methods.Where(method => !method.IsAssignment && Matches(method, filter, alsoByWhatItSays: true)))
            {
                var insert = method.IsOperator
                    ? $"{receiver} {method.Name} "
                    : method.Parameters.Count == 0
                        ? $"{method.Name} {receiver}{method.Mood}"
                        : $"{method.Name} {receiver} ";

                entries.Add(new CompletionEntry(
                    Label(method),
                    CompletionKind.Method,
                    insert,
                    start,
                    offset,
                    $"{type.Name}{Returns(method)}",
                    Document(method, receiver, CallForm.Method),
                    entries.Count));
            }

            return entries;
        }

        /// <summary>
        /// Everything that goes by a name: variables in scope, methods of their types, keywords
        /// and plain emoji, in that order.
        /// </summary>
        private List<CompletionEntry> Named(string query, int start, int offset, bool includeVariables, Scope scope, TypeIndex types)
        {
            var entries = new List<CompletionEntry>();
            var near = scope.Near(offset);

            if (includeVariables)
            {
                foreach (var variable in near.Where(variable => variable.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) && variable.Name != query))
                {
                    entries.Add(new CompletionEntry(variable.Name, CompletionKind.Variable, variable.Name, start, offset, variable.TypeName, DescribeVariable(variable.Name, variable.TypeName, types), entries.Count));
                }
            }

            var offered = new HashSet<string>();
            foreach (var variable in near.DistinctBy(variable => EmojiText.Bare(variable.TypeName)))
            {
                if (types.Find(variable.TypeName) is not { } type)
                    continue;

                foreach (var method in type.Methods.Where(method => Matches(method, query, alsoByWhatItSays: false) && offered.Add(type.Name + method.Name)))
                {
                    entries.Add(new CompletionEntry(
                        Label(method),
                        CompletionKind.Method,
                        method.Name + " ",
                        start,
                        offset,
                        $"{type.Name} ({variable.Name}){Returns(method)}",
                        Document(method, variable.Name, CallForm.Method),
                        entries.Count));
                }
            }

            foreach (var keyword in _keywords.Where(keyword => Matches(keyword, query)).OrderBy(keyword => keyword.Keyword.Length))
            {
                entries.Add(new CompletionEntry(
                    $"{keyword.Emoji} {keyword.Keyword}",
                    CompletionKind.Keyword,
                    keyword.Emoji,
                    start,
                    offset,
                    keyword.Category,
                    Document(keyword),
                    entries.Count));
            }

            foreach (var emoji in emojiNames.Search(query, MostEmoji))
            {
                entries.Add(new CompletionEntry(
                    $"{emoji.Emoji} {emoji.Alias}",
                    CompletionKind.Emoji,
                    emoji.Emoji,
                    start,
                    offset,
                    "emoji",
                    emoji.Description,
                    entries.Count));
            }

            return entries;
        }

        /// <param name="alsoByWhatItSays">
        /// Match on parameter names and the words of the documentation too. That suits narrowing
        /// the methods of one type after a dot. Searching every type in scope by name, it would
        /// bury the emoji that was asked for.
        /// </param>
        private bool Matches(MethodDocumentation method, string query, bool alsoByWhatItSays)
        {
            if (query.Length == 0)
                return true;

            var wanted = query.ToLowerInvariant();
            if (emojiNames.NamesOf(method.Name).Any(name => name.StartsWith(wanted, StringComparison.Ordinal) || name.Contains("_" + wanted, StringComparison.Ordinal)))
                return true;

            return alsoByWhatItSays
                && (method.Parameters.Any(parameter => parameter.Name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                    || (wanted.Length >= ShortestDescriptionSearch && HasWordStarting(method.Documentation, wanted)));
        }

        private static bool Matches(CatalogKeyword keyword, string query) =>
            keyword.Description.Length > 0
            && (keyword.Keyword.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                || keyword.ShortCode.TrimStart(':').StartsWith(query, StringComparison.OrdinalIgnoreCase)
                || keyword.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                || (query.Length >= ShortestDescriptionSearch && HasWordStarting(keyword.Description, query)));

        private static bool HasWordStarting(string text, string start) =>
            text.Split([' ', '\n', '\t', '.', ',', '(', ')', '*', '`', ':', '"'], StringSplitOptions.RemoveEmptyEntries)
                .Any(word => word.StartsWith(start, StringComparison.OrdinalIgnoreCase));

        private static int IndexAt(IReadOnlyList<SourceToken> tokens, int offset)
        {
            for (var index = 0; index < tokens.Count; index++)
            {
                if (tokens[index].Start <= offset && offset < tokens[index].End)
                    return index;
            }

            return -1;
        }

        private string? DescribeCallAt(IReadOnlyList<SourceToken> tokens, int index, Scope scope, TypeIndex types)
        {
            if (scope.IsDeclaration(index))
                return null;

            return CallNamedBy(index, ExpressionsOfLine(tokens, index)) is { } call && Resolve(call, tokens, scope, types) is { } resolved
                ? Document(resolved.Method, resolved.Subject, resolved.Form)
                : null;
        }

        /// <summary>An operator is a method of the operand on its left.</summary>
        private string? DescribeOperatorAt(IReadOnlyList<SourceToken> tokens, int index, Scope scope, TypeIndex types)
        {
            if (!ExpressionReader.IsOperator(tokens[index]) || scope.IsDeclaration(index))
                return null;

            foreach (var binary in All(ExpressionsOfLine(tokens, index)).Where(expression => expression.Kind == ExpressionKind.Binary))
            {
                var type = scope.TypeOf(binary.Operands[0]);
                for (var position = 0; position < binary.Operators.Count; position++)
                {
                    var (token, text) = binary.Operators[position];
                    var method = types.FindMethod(type, text);
                    if (token == index || (token == index - 1 && text.Length > EmojiText.Bare(tokens[token].Text).Length))
                    {
                        var left = binary.Operands[position];
                        var subject = position == 0 && left.Kind == ExpressionKind.Variable ? tokens[left.First].Text : type ?? "…";
                        return method is null ? null : Document(method, subject, CallForm.Method);
                    }

                    type = method?.ReturnType?.TypeName;
                }
            }

            return null;
        }

        /// <summary>The expressions of the line a token is on. A call is never read across a line break.</summary>
        private static IReadOnlyList<Expression> ExpressionsOfLine(IReadOnlyList<SourceToken> tokens, int index)
        {
            var first = index;
            while (first > 0 && tokens[first - 1].Line == tokens[index].Line)
                first--;

            var limit = index;
            while (limit < tokens.Count && tokens[limit].Line == tokens[index].Line)
                limit++;

            return new ExpressionReader(tokens).ReadStatement(first, limit);
        }

        private static Expression? CallNamedBy(int tokenIndex, IEnumerable<Expression> expressions) =>
            All(expressions).FirstOrDefault(expression => expression.Kind == ExpressionKind.Call && expression.NameToken == tokenIndex);

        /// <summary>Each expression and everything inside it.</summary>
        private static IEnumerable<Expression> All(IEnumerable<Expression> expressions)
        {
            foreach (var expression in expressions)
            {
                yield return expression;

                var parts = expression.Arguments.Concat(expression.Operands);
                if (expression.Receiver is not null)
                    parts = parts.Append(expression.Receiver);
                if (expression.Inner is not null)
                    parts = parts.Append(expression.Inner);

                foreach (var part in All(parts))
                    yield return part;
            }
        }

        private static string? DescribeType(IReadOnlyList<SourceToken> tokens, int index, TypeIndex types)
        {
            var name = EmojiText.Bare(tokens[index].Text) == "🆕" && index + 1 < tokens.Count ? tokens[index + 1].Text : tokens[index].Text;
            return types.Find(name) is { } type ? DescribeType(type) : null;
        }

        private static string DescribeType(TypeDocumentation type)
        {
            var kind = type.Kind switch
            {
                TypeKind.Class => "class",
                TypeKind.ValueType => "value type",
                TypeKind.Protocol => "protocol",
                TypeKind.Enumeration => "enumeration",
                _ => "type",
            };

            return $"**{type.Name}** {kind} in `{type.Package}`{Paragraph(type.Documentation)}";
        }

        private string? DescribeKeyword(string emoji) =>
            _keywords.FirstOrDefault(keyword => keyword.Description.Length > 0 && EmojiText.Same(keyword.Emoji, emoji)) is { } keyword ? Document(keyword) : null;

        private static string? DescribeAnyMethod(string emoji, TypeIndex types)
        {
            var owners = types.All
                .Where(type => type.Methods.Concat(type.TypeMethods).Any(method => EmojiText.Same(method.Name, emoji)))
                .Select(type => type.Name)
                .ToList();

            return owners.Count == 0 ? null : $"**{emoji}** is a method of {string.Join(", ", owners)}.";
        }

        private string? DescribeEmoji(string emoji) =>
            emojiNames.NameOf(emoji) is { } name ? $"{emoji} {name}" : null;

        private static string DescribeVariable(string name, string typeName, TypeIndex types)
        {
            var summary = types.Find(typeName) is { Documentation.Length: > 0 } type ? Paragraph(FirstParagraph(type.Documentation)) : string.Empty;
            return $"`{name}` is a {typeName}{summary}";
        }

        private static string Document(CatalogKeyword keyword)
        {
            var text = new StringBuilder($"**{keyword.Emoji}** {keyword.Description}");
            if (!string.IsNullOrWhiteSpace(keyword.Keyword))
                text.Append($" (like `{keyword.Keyword}`)");

            if (!string.IsNullOrWhiteSpace(keyword.Example))
                text.Append("\n\n```emojiscript\n").Append(keyword.Example.ReplaceLineEndings("\n")).Append("\n```");

            return text.ToString();
        }

        private string Document(MethodDocumentation method, string subject, CallForm form)
        {
            var name = emojiNames.NameOf(method.Name) is { } known ? $"\n\n{method.Name} {known}" : string.Empty;
            return $"```emojiscript\n{SignatureLabel(method, subject, form)}\n```{Paragraph(method.Documentation)}{name}";
        }

        private static SignatureInfo Describe(MethodDocumentation method, string subject, CallForm form, int activeParameter) =>
            new(
                SignatureLabel(method, subject, form),
                method.Documentation,
                method.Parameters.Select(parameter => new SignatureParameter(ParameterLabel(parameter))).ToList(),
                activeParameter);

        private static string SignatureLabel(MethodDocumentation method, string subject, CallForm form)
        {
            var parameters = string.Concat(method.Parameters.Select(parameter => " " + ParameterLabel(parameter)));
            if (form == CallForm.Method && method.IsOperator)
                return $"{subject} {method.Name}{parameters}{Returns(method)}";

            // The value comes first and is the first parameter: value ➡️ 🐽 list index❗️.
            if (form == CallForm.Method && method.IsAssignment && method.Parameters.Count > 0)
            {
                var rest = string.Concat(method.Parameters.Skip(1).Select(parameter => " " + ParameterLabel(parameter)));
                return $"{ParameterLabel(method.Parameters[0])} ➡️ {method.Name} {subject}{rest}❗️";
            }

            var head = form switch
            {
                CallForm.Initializer => method.Name.Length == 0 ? $"🆕{subject}" : $"🆕{subject}▶️{method.Name}",
                CallForm.TypeMethod => $"{method.Name}🐇{subject}",
                _ => $"{method.Name} {subject}",
            };

            return $"{head}{parameters}{method.Mood}{(form == CallForm.Initializer ? string.Empty : Returns(method))}";
        }

        private static string ParameterLabel(ParameterDocumentation parameter) => $"{parameter.Name} {parameter.Type.Display}";

        private static string Label(MethodDocumentation method) =>
            method.Parameters.Count == 0 ? method.Name : $"{method.Name} {string.Join(' ', method.Parameters.Select(parameter => parameter.Name))}";

        private static string Returns(MethodDocumentation method) => method.ReturnType is null ? string.Empty : $" ➡️ {method.ReturnType.Display}";

        private static string Paragraph(string text) => text.Length == 0 ? string.Empty : "\n\n" + text;

        private static string FirstParagraph(string text)
        {
            var end = text.IndexOf("\n\n", StringComparison.Ordinal);
            return end < 0 ? text : text[..end];
        }

        /// <summary>
        /// A keyword of the catalog as plain values. A few catalog entries are unfinished and
        /// throw from the properties nobody has written yet; those read as empty here, and an
        /// entry without a description is not offered.
        /// </summary>
        private sealed record CatalogKeyword(string Emoji, string Keyword, string ShortCode, string Name, string Description, string Category, string Example)
        {
            public static CatalogKeyword From(EmojicodeKeyword keyword) => new(
                Read(() => keyword.Emoji),
                Read(() => keyword.Keyword),
                Read(() => keyword.ShortCode),
                Read(() => keyword.Name),
                Read(() => keyword.Description),
                Read(() => keyword.Category),
                Read(() => keyword.Example));

            private static string Read(Func<string?> property)
            {
                try
                {
                    return property() ?? string.Empty;
                }
                catch (NotImplementedException)
                {
                    return string.Empty;
                }
            }
        }

        private enum CallForm
        {
            Method,
            TypeMethod,
            Initializer,
        }
    }
}
