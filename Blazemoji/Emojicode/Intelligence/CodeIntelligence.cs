using System.Text;

namespace Blazemoji.Emojicode.Intelligence
{
    public sealed class CodeIntelligence(IEnumerable<EmojicodeKeyword> keywords, IEmojiNames emojiNames) : ICodeIntelligence
    {
        public const string StandardPackage = "s";

        private const int MostEmoji = 25;

        // A word this short matches too much to be worth searching descriptions for.
        private const int ShortestDescriptionSearch = 3;

        private static readonly HashSet<string> Moods = ["❗", "❓"];

        // Between two operands these join them into one argument; they do not start another.
        private static readonly HashSet<string> BinaryOperators =
            ["➕", "➖", "✖", "➗", "🚮", "🙌", "😜", "▶", "◀", "🤝", "👐", "⭕", "💢", "❌", "👈", "👉", "🤜", "🤛"];

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

            var markdown = DescribeCallAt(tokens, index, scope, types)
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
            var line = tokens.Where(token => token.Start >= lineStart && token.End <= offset).ToList();
            var open = new Stack<OpenCall>();
            var joined = false;

            for (var index = 0; index < line.Count; index++)
            {
                var token = line[index];
                var bare = EmojiText.Bare(token.Text);

                if (token.Kind == TokenKind.Symbol && Moods.Contains(bare))
                {
                    if (open.Count > 0)
                        open.Pop();

                    CountArgument(open, ref joined);
                    continue;
                }

                if (token.Kind == TokenKind.Symbol && BinaryOperators.Contains(bare))
                {
                    joined = true;
                    continue;
                }

                if (token.Kind == TokenKind.Symbol && bare == "🍇")
                {
                    // A closure is one argument when it closes on this line. When it does not,
                    // the cursor is inside it.
                    var close = MatchingClose(line, index);
                    if (close < 0)
                        return null;

                    index = close;
                    CountArgument(open, ref joined);
                    continue;
                }

                if (token.Kind == TokenKind.Symbol && StartCall(line, index, scope, types) is { } call)
                {
                    open.Push(call.Call);
                    index += call.Consumed - 1;
                    continue;
                }

                if (token.Kind != TokenKind.Symbol)
                    CountArgument(open, ref joined);
            }

            if (open.Count == 0)
                return null;

            var current = open.Peek();
            return Describe(current.Method, current.Subject, current.Form, Math.Min(current.Arguments, current.Method.Parameters.Count));
        }

        private static void CountArgument(Stack<OpenCall> open, ref bool joined)
        {
            if (open.Count > 0 && !joined)
                open.Peek().Arguments++;

            joined = false;
        }

        private static int MatchingClose(List<SourceToken> line, int open)
        {
            var depth = 0;
            for (var index = open; index < line.Count; index++)
            {
                var bare = EmojiText.Bare(line[index].Text);
                if (line[index].Kind != TokenKind.Symbol)
                    continue;

                if (bare == "🍇")
                    depth++;
                else if (bare == "🍉" && --depth == 0)
                    return index;
            }

            return -1;
        }

        /// <summary>
        /// Recognises the start of a call at a symbol: <c>method receiver</c> where the
        /// receiver's type has that method, <c>method🐇Type</c>, or <c>🆕Type</c>.
        /// </summary>
        private static (OpenCall Call, int Consumed)? StartCall(IReadOnlyList<SourceToken> tokens, int index, Scope scope, TypeIndex types)
        {
            var token = tokens[index];
            if (index + 1 >= tokens.Count)
                return null;

            var next = tokens[index + 1];
            if (EmojiText.Bare(token.Text) == "🆕" && next.Kind == TokenKind.Symbol && types.Find(next.Text) is { } created)
            {
                var initializer = created.Initializers.FirstOrDefault(candidate => candidate.Name.Length == 0) ?? created.Initializers.FirstOrDefault();
                return initializer is null ? null : (new OpenCall(initializer, created.Name, CallForm.Initializer), 2);
            }

            if (next.Text == "🐇" && index + 2 < tokens.Count && types.FindTypeMethod(tokens[index + 2].Text, token.Text) is { } typeMethod)
                return (new OpenCall(typeMethod, tokens[index + 2].Text, CallForm.TypeMethod), 3);

            if (next.Kind == TokenKind.Word && types.FindMethod(scope.TypeOf(next.Text, next.Start), token.Text) is { } method)
                return (new OpenCall(method, next.Text, CallForm.Method), 2);

            return null;
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

            var entries = new List<CompletionEntry>();
            foreach (var method in type.Methods.Where(method => Matches(method, filter, alsoByWhatItSays: true)))
            {
                var insert = method.Parameters.Count == 0
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
            // 🆕Type: hovering either part describes the type being made.
            if (index > 0 && EmojiText.Bare(tokens[index - 1].Text) == "🆕")
                return null;

            return StartCall(tokens, index, scope, types) is { } call && EmojiText.Bare(tokens[index].Text) != "🆕"
                ? Document(call.Call.Method, call.Call.Subject, call.Call.Form)
                : null;
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
            var head = form switch
            {
                CallForm.Initializer => $"🆕{subject}{method.Name}",
                CallForm.TypeMethod => $"{method.Name}🐇{subject}",
                _ => $"{method.Name} {subject}",
            };

            var parameters = string.Concat(method.Parameters.Select(parameter => " " + ParameterLabel(parameter)));
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

        /// <param name="Subject">The receiver's name for a method, the type's name otherwise.</param>
        private sealed class OpenCall(MethodDocumentation method, string subject, CallForm form)
        {
            public MethodDocumentation Method => method;

            public string Subject => subject;

            public CallForm Form => form;

            public int Arguments { get; set; }
        }
    }
}
