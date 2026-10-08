using Blazemoji.Services.Projects;
using Microsoft.JSInterop;

namespace Blazemoji.Services.Library
{
    /// <summary>
    /// Keeps what was saved from the editor in the browser's local storage, each file under
    /// the name it was saved with. As with projects there, the storage can be full, switched
    /// off or out of reach, and every such failure is a <see cref="ProjectStoreException"/>.
    /// </summary>
    public class LibraryService(ILocalStorageService localStorage) : ILibraryService
    {
        /// <summary>
        /// Removes the saved snippets and nothing else. Projects and settings share the same
        /// storage and are not the library's to delete.
        /// </summary>
        public Task ClearSavedAsync() => Guarded(async () =>
        {
            var keys = await localStorage.KeysAsync();
            foreach (var key in keys.Where(IsSnippetKey).ToList())
                await localStorage.RemoveItemAsync(key);

            return true;
        });

        /// <summary>
        /// A snippet is kept under the name it was saved with, which ends like a file's. A
        /// project keeps each of its files under a key that ends the same way, and those
        /// belong to the project store.
        /// </summary>
        private static bool IsSnippetKey(string key) =>
            !key.StartsWith(LocalStorageProjectStore.KeyPrefix, StringComparison.Ordinal)
            && (key.Contains(".🍇") || key.Contains(".emojic"));

        public Task<List<EmojicFile>> GetSavedAsync() => Guarded(async () =>
        {
            var files = new List<EmojicFile>();
            var keys = await localStorage.KeysAsync();
            foreach (var key in keys.Where(IsSnippetKey).ToList())
                files.Add(new EmojicFile { Name = key, Code = await localStorage.GetItemAsStringAsync(key) ?? string.Empty });

            return files;
        });

        public Task SaveAsync(EmojicFile file) => Guarded(async () =>
        {
            await localStorage.SetItemAsStringAsync(file.Name, file.Code);
            return true;
        });

        private static async Task<T> Guarded<T>(Func<Task<T>> useStorage)
        {
            try
            {
                return await useStorage();
            }
            catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException or OperationCanceledException)
            {
                throw new ProjectStoreException("The browser's storage could not be used.", exception);
            }
        }
    }
}
