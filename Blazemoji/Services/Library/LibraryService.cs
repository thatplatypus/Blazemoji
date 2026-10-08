using Blazemoji.Services.Projects;


namespace Blazemoji.Services.Library
{
    public class LibraryService : ILibraryService
    {
        private readonly ILocalStorageService _localStorageService;

        public LibraryService(ILocalStorageService localStorageService)
        {
            _localStorageService = localStorageService;
        }

        /// <summary>
        /// Removes the saved snippets and nothing else. Projects and settings share the same
        /// storage and are not the library's to delete.
        /// </summary>
        public async Task ClearLocalStorageAsync()
        {
            var keys = await _localStorageService.KeysAsync();
            foreach (var key in keys.Where(IsSnippetKey).ToList())
                await _localStorageService.RemoveItemAsync(key);
        }

        /// <summary>
        /// A snippet is kept under the name it was saved with, which ends like a file's. A
        /// project keeps each of its files under a key that ends the same way, and those
        /// belong to the project store.
        /// </summary>
        private static bool IsSnippetKey(string key) =>
            !key.StartsWith(LocalStorageProjectStore.KeyPrefix, StringComparison.Ordinal)
            && (key.Contains(".🍇") || key.Contains(".emojic"));

        public async Task<List<EmojicFile>> GetUserSavedFiles()
        {
            var files = new ConcurrentBag<EmojicFile>();
            var keys = await _localStorageService.KeysAsync();
            var emojicodeKeys = keys.Where(IsSnippetKey);

            foreach (var key in emojicodeKeys)
            {
                try
                {
                    var code = await _localStorageService.GetItemAsStringAsync(key);
                    files.Add(new EmojicFile
                    {
                        Name = key,
                        Code = code ?? string.Empty,
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

            return files.ToList();
        }

        public async Task SaveFileToLocalStorageAsync(EmojicFile file)
        {
            await _localStorageService.SetItemAsStringAsync(file.Name, file.Code);
        }
    }
}
