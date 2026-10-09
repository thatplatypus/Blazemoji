using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Layout;

namespace Blazemoji.Services.Layout
{
    /// <summary>
    /// Where the workspace's layout is kept between visits. A host that has somewhere to keep
    /// it registers one; without one the layout lasts as long as the page does.
    /// </summary>
    /// <remarks>Both methods throw <see cref="ProjectStoreException"/> when the storage cannot be used.</remarks>
    public interface ILayoutStore
    {
        /// <returns>Null when no layout has been kept, or what was kept is not one.</returns>
        Task<WorkspaceLayout?> LoadAsync();

        Task SaveAsync(WorkspaceLayout layout);
    }
}
