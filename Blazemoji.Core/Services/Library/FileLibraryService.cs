using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Library;
using Microsoft.Extensions.Options;

namespace Blazemoji.Services.Library
{
    /// <summary>
    /// Keeps what was saved from the editor as files in a folder of their own, beside the
    /// project folders. As with projects, nothing is destroyed: a file that is replaced or
    /// cleared away goes to the trash under the same root.
    /// </summary>
    public sealed class FileLibraryService(IOptions<FileProjectStoreOptions> options, TimeProvider clock) : ILibraryService
    {
        /// <summary>The folder under <see cref="FileProjectStoreOptions.Root"/> that saved files are kept in.</summary>
        public const string Folder = "Snippets";

        private const string NameWhenNothingIsLeft = "Untitled.🍇";

        private readonly ProjectsFolder _disk = new(options.Value.Root, clock);

        private string Snippets => Path.Combine(_disk.Root, Folder);

        public Task<List<EmojicFile>> GetSavedAsync() => ProjectsFolder.Guarded(async () =>
        {
            var saved = new List<EmojicFile>();
            foreach (var path in Kept())
            {
                if (new FileInfo(path).Length <= FileProjectStore.LargestFile && await ProjectsFolder.ReadTextAsync(path) is { } text)
                    saved.Add(new EmojicFile { Name = Path.GetFileName(path), Code = text });
            }

            return saved;
        });

        public Task SaveAsync(EmojicFile file) => ProjectsFolder.Guarded(async () =>
        {
            var name = ProjectsFolder.NameOnDisk(file.Name, NameWhenNothingIsLeft);
            var path = Path.Combine(Snippets, name);
            if (File.Exists(path))
                _disk.MoveToTrash(path, Snippets, name);

            await ProjectsFolder.WriteTextAsync(path, file.Code);
        });

        public Task ClearSavedAsync() => ProjectsFolder.Guarded(() =>
        {
            foreach (var path in Kept())
                _disk.MoveToTrash(path, Snippets, Path.GetFileName(path));

            return Task.CompletedTask;
        });

        private List<string> Kept() =>
            Directory.Exists(Snippets)
                ? Directory.GetFiles(Snippets).Where(path => !Path.GetFileName(path).StartsWith('.')).Order(StringComparer.Ordinal).ToList()
                : [];
    }
}
