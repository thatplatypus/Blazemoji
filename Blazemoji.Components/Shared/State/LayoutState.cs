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
        // What was changed while the kept layout was still on its way, to be made again on
        // top of it once it is here.
        private readonly List<Func<WorkspaceLayout, WorkspaceLayout>> _whileLoading = [];
        private Task? _loading;

        public event Action? StateChanged;

        public WorkspaceLayout Current { get; private set; } = WorkspaceLayout.Default;

        /// <summary>Takes up the layout that was kept, if there is one.</summary>
        public Task LoadAsync()
        {
            if (store is null)
                return Task.CompletedTask;

            if (_loading is not null)
                return _loading;

            // A store that answers at once has finished before there is a task to remember,
            // and one that has finished must not be remembered as still on its way.
            var loading = LoadFromAsync(store);
            if (!loading.IsCompleted)
                _loading = loading;

            return loading;
        }

        private async Task LoadFromAsync(ILayoutStore from)
        {
            WorkspaceLayout? kept = null;
            try
            {
                kept = (await from.LoadAsync())?.Mended();
            }
            catch (ProjectStoreException exception)
            {
                logger.LogWarning(exception, "The kept layout could not be read, so the default is used");
            }
            catch
            {
                // Not a failure a store is meant to have. Whoever asked hears of it, and what
                // is changed from here on is kept as it would be had nothing been asked.
                _whileLoading.Clear();
                _loading = null;
                throw;
            }

            // A divider moved while the store was answering is newer than what it kept, and
            // only that is: the rest of what was kept still stands.
            var changed = _whileLoading.Count > 0;
            var layout = _whileLoading.Aggregate(kept ?? Current, (joined, change) => change(joined)).Mended();
            _whileLoading.Clear();
            _loading = null;

            if (layout != Current)
            {
                Current = layout;
                StateChanged?.Invoke();
            }

            if (changed)
                await KeepAsync();
        }

        /// <param name="share">The sidebar's part of the workspace's width, from 0 to 1.</param>
        public Task ResizeSidebarAsync(double share) => ChangeAsync(layout => layout with { SidebarShare = share });

        /// <param name="share">The editor's part of the height it shares with the output, from 0 to 1.</param>
        public Task ResizeEditorAsync(double share) => ChangeAsync(layout => layout with { EditorShare = share });

        public Task ToggleSidebarAsync()
        {
            // Whoever asked was looking at the sidebar as it is now, and wants the other.
            var hidden = !Current.SidebarHidden;
            return ChangeAsync(layout => layout with { SidebarHidden = hidden });
        }

        private async Task ChangeAsync(Func<WorkspaceLayout, WorkspaceLayout> change)
        {
            var layout = change(Current).Mended();
            if (layout == Current)
                return;

            Current = layout;
            StateChanged?.Invoke();

            // Kept now, it would be written over what the store has not finished handing back.
            if (_loading is not null)
            {
                _whileLoading.Add(change);
                return;
            }

            await KeepAsync();
        }

        private async Task KeepAsync()
        {
            if (store is null)
                return;

            try
            {
                await store.SaveAsync(Current);
            }
            catch (ProjectStoreException exception)
            {
                logger.LogWarning(exception, "The layout could not be kept, so it lasts only as long as this page");
            }
        }
    }
}
