using Blazemoji.Emojicode.Editing;
using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Shared.State;
using Microsoft.JSInterop;

namespace Blazemoji.Interop
{
    /// <param name="Kind">The name of a <see cref="CompletionKind"/>.</param>
    public sealed record CompletionAnswer(string Label, string Kind, string Insert, int ReplaceStart, int ReplaceEnd, string Detail, string Documentation, int Order);

    public sealed record HoverAnswer(int Start, int End, string Markdown);

    public sealed record SignatureAnswer(string Label, string Documentation, IReadOnlyList<string> Parameters, int ActiveParameter);

    /// <summary>What Monaco is told about the language's pairs, comments and escape mark. Each pair is its opener and its closer.</summary>
    public sealed record LanguageSyntax(IReadOnlyList<string[]> Matched, IReadOnlyList<string[]> Completed, string LineComment, string[] BlockComment, string Escape, string[] Interpolation);

    /// <param name="Stamp">Names the text and the selection as they were when this was read.</param>
    public sealed record AroundCursorAnswer(string Before, string Selected, string After, string Stamp)
    {
        public TextAroundCursor Text => new(Before, Selected, After);
    }

    /// <summary>
    /// A <see cref="TypingEdit"/> on its way to the editor.
    /// </summary>
    /// <param name="Plain">What was typed. It goes in as it is if the text or the selection has changed since <paramref name="Stamp"/>.</param>
    /// <param name="Stamp">The <see cref="AroundCursorAnswer.Stamp"/> the edit was worked out from. Null for an edit that looked at nothing.</param>
    public sealed record TypedText(int RemoveBefore, int RemoveAfter, string Text, int SelectionStart, int SelectionEnd, string Plain, string? Stamp);

    /// <summary>
    /// The link between Monaco's language providers and <see cref="ICodeIntelligence"/>. The
    /// JavaScript side registers the providers and calls back here with the text and the
    /// cursor's offset; everything that knows about Emojicode is on this side.
    /// </summary>
    public sealed class EmojicodeLanguageInterop(
        IJSRuntime js,
        ICodeIntelligence intelligence,
        IPackageLibrary library,
        ProjectState project,
        ILogger<EmojicodeLanguageInterop> logger) : IAsyncDisposable
    {
        private const string ModulePath = "./_content/Blazemoji.Components/js/emojicodeLanguage.js";
        private const string RegisterFunction = "register";
        private const string DisposeFunction = "dispose";
        private const string AroundCursorFunction = "aroundCursor";
        private const string TypeFunction = "type";
        private const string ApplyThemeFunction = "applyTheme";

        private static readonly LanguageSyntax _syntax = new(
            [.. EmojicodePairs.Matched.Select(pair => new[] { pair.Open, pair.Close })],
            [.. EmojicodePairs.Completed.Select(pair => new[] { pair.Open, pair.Close })],
            EmojicodePairs.LineComment,
            [EmojicodePairs.BlockComment.Open, EmojicodePairs.BlockComment.Close],
            EmojicodePairs.Escape,
            [EmojicodePairs.Interpolation.Open, EmojicodePairs.Interpolation.Close]);

        private IJSObjectReference? _module;
        private IJSObjectReference? _registration;
        private DotNetObjectReference<EmojicodeLanguageInterop>? _self;

        /// <summary>
        /// Registers the providers for a language. Monaco keeps them for the whole page, so
        /// asking twice does nothing the second time.
        /// </summary>
        public async Task RegisterAsync(string languageId)
        {
            if (_registration is not null)
                return;

            _self = DotNetObjectReference.Create(this);
            _registration = await (await ModuleAsync()).InvokeAsync<IJSObjectReference>(RegisterFunction, languageId, _self, _syntax);
        }

        /// <summary>
        /// The text around the cursor of the editor with this element id. Null when there is no
        /// such editor, or it has nothing open.
        /// </summary>
        public async Task<AroundCursorAnswer?> AroundCursorAsync(string editorId) =>
            await (await ModuleAsync()).InvokeAsync<AroundCursorAnswer?>(AroundCursorFunction, editorId);

        /// <summary>Types into the editor with this element id, and gives it the keyboard.</summary>
        /// <param name="plain">What was typed, before <paramref name="edit"/> was made of it.</param>
        /// <param name="stamp">The <see cref="AroundCursorAnswer.Stamp"/> that <paramref name="edit"/> was worked out from, if it was worked out from anything.</param>
        public async Task TypeAsync(string editorId, TypingEdit edit, string plain, string? stamp) =>
            await (await ModuleAsync()).InvokeVoidAsync(
                TypeFunction,
                editorId,
                new TypedText(edit.RemoveBefore, edit.RemoveAfter, edit.Text, edit.SelectionStart, edit.SelectionEnd, plain, stamp));

        /// <summary>Gives every editor the colours the page has now.</summary>
        public async Task ApplyThemeAsync() =>
            await (await ModuleAsync()).InvokeVoidAsync(ApplyThemeFunction);

        private async Task<IJSObjectReference> ModuleAsync() =>
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);

        [JSInvokable]
        public async Task<IReadOnlyList<CompletionAnswer>> CompleteAsync(string text, int offset)
        {
            try
            {
                return intelligence.Complete(text, offset, await PackagesAsync(text))
                    .Select(entry => new CompletionAnswer(entry.Label, entry.Kind.ToString(), entry.Insert, entry.ReplaceStart, entry.ReplaceEnd, entry.Detail, entry.Documentation, entry.Order))
                    .ToList();
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Completion failed");
                return [];
            }
        }

        [JSInvokable]
        public async Task<HoverAnswer?> HoverAsync(string text, int offset)
        {
            try
            {
                return intelligence.Hover(text, offset, await PackagesAsync(text)) is { } hover
                    ? new HoverAnswer(hover.Start, hover.End, hover.Markdown)
                    : null;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Hover failed");
                return null;
            }
        }

        [JSInvokable]
        public async Task<SignatureAnswer?> SignatureAsync(string text, int offset)
        {
            try
            {
                return intelligence.Signature(text, offset, await PackagesAsync(text)) is { } signature
                    ? new SignatureAnswer(signature.Label, signature.Documentation, signature.Parameters.Select(parameter => parameter.Label).ToList(), signature.ActiveParameter)
                    : null;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Signature help failed");
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_registration is not null)
                {
                    await _registration.InvokeVoidAsync(DisposeFunction);
                    await _registration.DisposeAsync();
                }

                if (_module is not null)
                    await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page has gone, and Monaco's registrations with it.
            }

            _self?.Dispose();
        }

        /// <summary>
        /// The documentation of every package the project imports. An import in one file serves
        /// the files it includes, so the imports of all the project's files are taken together.
        /// </summary>
        private Task<IReadOnlyList<PackageDocumentation>> PackagesAsync(string text)
        {
            var packages = project.Current.Files
                .Select(file => file.Content)
                .Append(text)
                .SelectMany(intelligence.Imports)
                .Distinct()
                .ToList();

            return library.GetAsync(packages);
        }
    }
}
