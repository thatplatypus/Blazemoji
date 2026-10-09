using Blazemoji.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazemoji.Interop
{
    /// <summary>
    /// The script that watches a <see cref="SplitView"/>'s divider. The panel inside moves
    /// the divider by itself and says nothing when it is let go, so the script says it: to
    /// <see cref="SplitView.DividerMovedAsync"/>, or <see cref="SplitView.DividerResetAsync"/>
    /// after a double-click.
    /// </summary>
    public sealed class SplitViewInterop(IJSRuntime js) : IAsyncDisposable
    {
        private const string ModulePath = "./_content/Blazemoji.Components/js/splitView.js";
        private const string AttachFunction = "attach";
        private const string DetachFunction = "detach";

        private Task<IJSObjectReference>? _module;

        /// <param name="root">The element around the panel.</param>
        public async Task AttachAsync(ElementReference root, DotNetObjectReference<SplitView> view) =>
            await (await ModuleAsync()).InvokeVoidAsync(AttachFunction, root, view);

        public async Task DetachAsync(ElementReference root) =>
            await (await ModuleAsync()).InvokeVoidAsync(DetachFunction, root);

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
