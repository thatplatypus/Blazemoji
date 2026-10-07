using Microsoft.JSInterop;

namespace Blazemoji.Interop
{
    /// <summary>
    /// The system clipboard, through the library's own script.
    /// </summary>
    public sealed class ClipboardInterop(IJSRuntime js) : IAsyncDisposable
    {
        private const string ModulePath = "./_content/Blazemoji.Components/js/clipboard.js";
        private const string CopyFunction = "copyText";

        private IJSObjectReference? _module;

        /// <returns>False when the browser would not let the page write to the clipboard.</returns>
        public async Task<bool> CopyAsync(string text)
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return await _module.InvokeAsync<bool>(CopyFunction, text);
        }

        public async ValueTask DisposeAsync()
        {
            if (_module is null)
                return;

            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page has gone, and the module with it.
            }
        }
    }
}
