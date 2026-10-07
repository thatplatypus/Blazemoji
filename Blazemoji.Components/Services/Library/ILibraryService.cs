using Blazemoji.Shared.Models.Library;

namespace Blazemoji.Services.Library
{
    /// <summary>
    /// Provides services to back the Library UI
    /// </summary>
    public interface ILibraryService
    {
        /// <summary>
        /// Gets all sample files in the project
        /// </summary>
        public Task<List<EmojicFile>> GetAllSamplesAsync();

        /// <summary>
        /// Gets all files the user has saved in browser local storage
        /// </summary>
        public Task<List<EmojicFile>> GetUserSavedFiles();

        /// <summary>
        /// Saves an emojicode .🍇 file to user's local browser storage for persistence
        /// </summary>
        /// <param name="file">An emojicode file</param>
        public Task SaveFileToLocalStorageAsync(EmojicFile file);

        /// <summary>
        /// Clears local storage
        /// </summary>
        public Task ClearLocalStorageAsync();
    }
}
