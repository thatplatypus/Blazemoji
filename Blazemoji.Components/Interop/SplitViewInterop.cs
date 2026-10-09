using Blazemoji.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazemoji.Interop
{
    /// <summary>
    /// The script that looks after a <see cref="SplitView"/>'s divider. The panel inside moves
    /// the divider by itself and does nothing more when it is let go. The script settles it
    /// there as a share of the room and then says so: to <see cref="SplitView.DividerMovedAsync"/>,
    /// or to <see cref="SplitView.DividerResetAsync"/> after a double-click.
    /// </summary>
    public sealed class SplitViewInterop(IJSRuntime js) : IAsyncDisposable
    {
        private const string ModulePath = "./_content/Blazemoji.Components/js/splitView.js";
        private const string AttachFunction = "attach";
        private const string ReleaseFunction = "release";
        private const string DetachFunction = "detach";

        private Task<IJSObjectReference>? _module;

        /// <param name="root">The element around the panel.</param>
        /// <param name="defaultShare">Where a double-click puts the divider.</param>
        /// <param name="dividerLabel">What the divider is called for someone who cannot see it, or null for the panel's own words.</param>
        public async Task AttachAsync(ElementReference root, DotNetObjectReference<SplitView> view, double defaultShare, string? dividerLabel) =>
            await (await ModuleAsync()).InvokeVoidAsync(AttachFunction, root, view, defaultShare, dividerLabel);

        /// <summary>Hands the divider back to the share .NET rendered, for a share that changed without the divider being moved.</summary>
        public async Task ReleaseAsync(ElementReference root) =>
            await (await ModuleAsync()).InvokeVoidAsync(ReleaseFunction, root);

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
