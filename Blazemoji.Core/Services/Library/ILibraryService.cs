using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Library;

namespace Blazemoji.Services.Library
{
    /// <summary>
    /// Where the Library tab keeps what was saved from the editor as a file of its own. The
    /// web host keeps these in the browser's local storage; a desktop host keeps them on disk.
    /// </summary>
    /// <remarks>Every method throws <see cref="ProjectStoreException"/> when the storage cannot be used.</remarks>
    public interface ILibraryService
    {
        Task<List<EmojicFile>> GetSavedAsync();

        /// <summary>Keeps the file under its name, in place of any kept under that name before.</summary>
        Task SaveAsync(EmojicFile file);

        /// <summary>Removes every saved file, and nothing else that shares the storage.</summary>
        Task ClearSavedAsync();
    }
}
