using Blazemoji.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazemoji.Interop
{
    /// <summary>
    /// The script behind the line that is typed for a running program. It sends the line and
    /// empties the box as Enter is pressed, in the browser, and hands the line to
    /// <see cref="OutputPanel.SendLineAsync"/>. What is in the box is never bound to .NET: a
    /// box emptied from here a round trip later would lose what had been typed since.
    /// </summary>
    public sealed class ProgramInputInterop(IJSRuntime js) : IAsyncDisposable
    {
        private const string ModulePath = "./_content/Blazemoji.Components/js/programInput.js";
        private const string AttachFunction = "attach";
        private const string DetachFunction = "detach";

        private Task<IJSObjectReference>? _module;

        /// <param name="line">The element around the text box.</param>
        public async Task AttachAsync(ElementReference line, DotNetObjectReference<OutputPanel> panel) =>
            await (await ModuleAsync()).InvokeVoidAsync(AttachFunction, line, panel);

        public async Task DetachAsync(ElementReference line) =>
            await (await ModuleAsync()).InvokeVoidAsync(DetachFunction, line);

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
