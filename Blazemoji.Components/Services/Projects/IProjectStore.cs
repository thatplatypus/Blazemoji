using Blazemoji.Shared.Models.Projects;

namespace Blazemoji.Services.Projects
{
    /// <summary>
    /// Where projects are kept between visits. The web host keeps them in the browser's local
    /// storage; a desktop host would keep them on disk.
    /// </summary>
    /// <remarks>Every method throws <see cref="ProjectStoreException"/> when the storage cannot be used.</remarks>
    public interface IProjectStore
    {
        Task<IReadOnlyList<ProjectSummary>> ListAsync();

        Task<Project?> LoadAsync(string projectId);

        Task SaveAsync(Project project);

        Task DeleteAsync(string projectId);

        /// <summary>The project that was open last, if it is known.</summary>
        Task<string?> GetLastOpenedAsync();

        Task SetLastOpenedAsync(string projectId);
    }

    public sealed class ProjectStoreException(string message, Exception inner) : Exception(message, inner);
}
