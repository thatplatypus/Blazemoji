using Microsoft.JSInterop;

namespace Blazemoji.Interop
{
    /// <summary>
    /// The system clipboard, through the library's own script. The script copies in the
    /// browser as a Copy button is pressed, because a press that has been to the server and
    /// back is one Safari no longer lets write to the clipboard. This only loads the script
    /// and asks it how the last copy went.
    /// </summary>
    public sealed class ClipboardInterop(IJSRuntime js) : IAsyncDisposable
    {
        private const string ModulePath = "./_content/Blazemoji.Components/js/clipboard.js";
        private const string LastCopySucceededFunction = "lastCopySucceeded";

        private Task<IJSObjectReference>? _module;

        /// <summary>Has every Copy button on the page copy when it is pressed. Asking twice does nothing more.</summary>
        public async Task ListenAsync() => await ModuleAsync();

        /// <returns>False when the browser would not let the page write to the clipboard.</returns>
        public async Task<bool> LastCopySucceededAsync() =>
            await (await ModuleAsync()).InvokeAsync<bool>(LastCopySucceededFunction);

        private Task<IJSObjectReference> ModuleAsync() =>
            _module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

        public async ValueTask DisposeAsync()
        {
            if (_module is not { IsCompletedSuccessfully: true } loaded)
                return;

            try
            {
                await (await loaded).DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page has gone, and the module with it.
            }
        }
    }
}
