using Blazemoji.Services.Layout;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Layout;
using Microsoft.Extensions.Logging;

namespace Blazemoji.Shared.State
{
    /// <summary>
    /// How the workspace is divided, and the keeping of it between visits. A host with
    /// somewhere to keep it registers an <see cref="ILayoutStore"/>; without one the layout
    /// lasts as long as the page does. Keeping it is a convenience, so a store that fails is
    /// logged and never stops a divider from moving.
    /// </summary>
    public sealed class LayoutState(ILogger<LayoutState> logger, ILayoutStore? store = null)
    {
        // Counts the changes made here, so that a kept layout that arrives late can tell.
        private int _changes;

        public event Action? StateChanged;

        public WorkspaceLayout Current { get; private set; } = WorkspaceLayout.Default;

        /// <summary>Takes up the layout that was kept, if there is one.</summary>
        public async Task LoadAsync()
        {
            if (store is null)
                return;

            var before = _changes;
            WorkspaceLayout? kept;
            try
            {
                kept = (await store.LoadAsync())?.Mended();
            }
            catch (ProjectStoreException exception)
            {
                logger.LogWarning(exception, "The kept layout could not be read, so the default is used");
                return;
            }

            // A divider moved while the store was answering is newer than what it kept.
            if (kept is null || before != _changes || kept == Current)
                return;

            Current = kept;
            StateChanged?.Invoke();
        }

        /// <param name="share">The sidebar's part of the workspace's width, from 0 to 1.</param>
        public Task ResizeSidebarAsync(double share) => ChangeAsync(Current with { SidebarShare = share });

        /// <param name="share">The editor's part of the height it shares with the output, from 0 to 1.</param>
        public Task ResizeEditorAsync(double share) => ChangeAsync(Current with { EditorShare = share });

        public Task ToggleSidebarAsync() => ChangeAsync(Current with { SidebarHidden = !Current.SidebarHidden });

        private async Task ChangeAsync(WorkspaceLayout wanted)
        {
            var layout = wanted.Mended();
            if (layout == Current)
                return;

            Current = layout;
            _changes++;
            StateChanged?.Invoke();

            if (store is null)
                return;

            try
            {
                await store.SaveAsync(layout);
            }
            catch (ProjectStoreException exception)
            {
                logger.LogWarning(exception, "The layout could not be kept, so it lasts only as long as this page");
            }
        }
    }
}
