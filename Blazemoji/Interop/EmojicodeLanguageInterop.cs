using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Shared.State;
using Microsoft.JSInterop;

namespace Blazemoji.Interop
{
    /// <param name="Kind">The name of a <see cref="CompletionKind"/>.</param>
    public sealed record CompletionAnswer(string Label, string Kind, string Insert, int ReplaceStart, int ReplaceEnd, string Detail, string Documentation, int Order);

    public sealed record HoverAnswer(int Start, int End, string Markdown);

    public sealed record SignatureAnswer(string Label, string Documentation, IReadOnlyList<string> Parameters, int ActiveParameter);

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
        private const string ModulePath = "./js/emojicodeLanguage.js";
        private const string RegisterFunction = "register";
        private const string DisposeFunction = "dispose";

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

            _module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _self = DotNetObjectReference.Create(this);
            _registration = await _module.InvokeAsync<IJSObjectReference>(RegisterFunction, languageId, _self);
        }

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
