using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Library;
using Microsoft.Extensions.Options;

namespace Blazemoji.Services.Library
{
    /// <summary>
    /// Keeps what was saved from the editor as files in a folder of their own, beside the
    /// project folders. As with projects, nothing is destroyed: a file that is replaced or
    /// cleared away goes to the trash under the same root. The folder may hold other things,
    /// and what is not listed as saved is never touched.
    /// </summary>
    public sealed class FileLibraryService(IOptions<FileProjectStoreOptions> options, TimeProvider clock, ILogger<FileLibraryService>? logger = null) : ILibraryService
    {
        /// <summary>The folder under <see cref="FileProjectStoreOptions.Root"/> that saved files are kept in.</summary>
        public const string Folder = ProjectsFolder.SnippetsFolder;

        private const string NameWhenNothingIsLeft = "Untitled.🍇";

        private readonly ProjectsFolder _disk = new(options.Value.Root, clock);

        private string Snippets => Path.Combine(_disk.Root, Folder);

        public Task<List<EmojicFile>> GetSavedAsync() => _disk.InTurn(async () =>
            (await ReadSavedAsync()).Select(saved => new EmojicFile { Name = Path.GetFileName(saved.Path), Code = saved.Text }).ToList());

        public Task SaveAsync(EmojicFile file) => _disk.InTurn(async () =>
        {
            var name = ProjectsFolder.NameOnDisk(file.Name, NameWhenNothingIsLeft);
            var path = Path.Combine(Snippets, name);
            if (File.Exists(path) && ProjectsFolder.IsALink(new FileInfo(path)))
                throw new IOException($"{name} is a link to somewhere outside the snippets.");

            if (File.Exists(path))
                _disk.MoveToTrash(path, Snippets, name);

            await ProjectsFolder.WriteTextAsync(path, file.Code);
        });

        public Task ClearSavedAsync() => _disk.InTurn(async () =>
        {
            foreach (var saved in await ReadSavedAsync())
                _disk.MoveToTrash(saved.Path, Snippets, Path.GetFileName(saved.Path));
        });

        /// <summary>What the Library tab lists: the text files in the folder that are neither hidden nor links.</summary>
        private async Task<List<(string Path, string Text)>> ReadSavedAsync()
        {
            var saved = new List<(string Path, string Text)>();
            if (!Directory.Exists(Snippets))
                return saved;

            foreach (var path in Directory.GetFiles(Snippets).Order(StringComparer.Ordinal))
            {
                if (Path.GetFileName(path).StartsWith('.'))
                    continue;

                try
                {
                    var file = new FileInfo(path);
                    if (!ProjectsFolder.IsALink(file) && file.Length <= FileProjectStore.LargestFile && await ProjectsFolder.ReadTextAsync(path) is { } text)
                        saved.Add((path, text));
                }
                catch (Exception exception) when (ProjectsFolder.IsTheDisksDoing(exception))
                {
                    logger?.LogWarning(exception, "The saved file {Path} could not be read, and is left out", path);
                }
            }

            return saved;
        }
    }
}
