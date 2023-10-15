using Blazemoji.Shared.Models.Library;
using Blazored.LocalStorage;
using System.Collections.Concurrent;
using System.Linq;

namespace Blazemoji.Services.Library
{
    public class LibraryService : ILibraryService
    {
        private readonly ILocalStorageService _localStorageService;
        private ConcurrentBag<EmojicFile>? _emojicFiles;

        public LibraryService(ILocalStorageService localStorageService)
        {
            _localStorageService = localStorageService;
        }

        public async Task ClearLocalStorageAsync()
        {
            await _localStorageService.ClearAsync();
        }

        public async Task<List<EmojicFile>> GetAllSamplesAsync()
        {
            string directoryPath = "Emojicode/Samples";
            IEnumerable<string> files = Directory.EnumerateFiles(directoryPath);

            List<Task> tasks = new();

            foreach (string file in files)
            {
                tasks.Add(ProcessFileAsync(file));
            }

            await Task.WhenAll(tasks);
            
            List<EmojicFile> emojiList = _emojicFiles?.ToList() ?? new List<EmojicFile>();

            return emojiList;
        }

        public async Task<List<EmojicFile>> GetUserSavedFiles()
        {
            var files = new ConcurrentBag<EmojicFile>();
            var keys = await _localStorageService.KeysAsync();
            foreach (var key in keys)
            {
                try
                {
                    var code = await _localStorageService.GetItemAsStringAsync(key);
                    files.Add(new EmojicFile
                    {
                        Name = key,
                        Code = code,
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

        private async Task ProcessFileAsync(string filePath)
        {
            _emojicFiles ??= new();

            var code = await File.ReadAllTextAsync(filePath);

            _emojicFiles.Add(new EmojicFile 
            {
                Name = filePath[(filePath.LastIndexOf("/") + 1)..],
                Code = code
            });
        }
    }
}
