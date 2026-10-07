using System.Text.Json;
using System.Text.Json.Serialization;
using Blazemoji.Shared.Models.Projects;
using Microsoft.JSInterop;

namespace Blazemoji.Services.Projects
{
    /// <summary>
    /// Keeps projects in the browser's local storage: one entry listing them, one entry per
    /// project saying what it is and which files it has, and one entry per file holding its
    /// text as it is. Local storage can only be reached once the page has rendered, is small
    /// (about 5 MB), and can be switched off, so every failure is a <see cref="ProjectStoreException"/>.
    /// </summary>
    /// <remarks>
    /// A file has an entry of its own for two reasons. The page reads an entry back in one
    /// message and a message has a size limit, so nothing read at once may grow with the
    /// project. And the same project can be open in two tabs: each writes only the files it
    /// changed, so one does not put back the other's older text.
    /// </remarks>
    public sealed class LocalStorageProjectStore(ILocalStorageService localStorage) : IProjectStore
    {
        /// <summary>
        /// Every key this store uses starts with this. Whatever else shares the browser's
        /// storage (the library's saved snippets) leaves such keys alone.
        /// </summary>
        public const string KeyPrefix = "blazemoji.";

        private const string IndexKey = KeyPrefix + "projects";
        private const string ProjectKeyPrefix = KeyPrefix + "project.";
        private const string FileKeyInfix = ".file.";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // What this page last read from or wrote to storage, by project. A save writes the difference.
        private readonly Dictionary<string, Stored> _stored = [];

        public async Task<IReadOnlyList<ProjectSummary>> ListAsync() =>
            (await ReadIndexAsync()).Projects.Select(row => new ProjectSummary(row.Id, row.Name)).ToList();

        public async Task<Project?> LoadAsync(string projectId)
        {
            var description = await GetAsync(ProjectKeyPrefix + projectId);
            var stored = Read<StoredProject>(description);
            if (stored?.Id is null || stored.Name is null || stored.Entry is null || stored.Files is null)
                return null;

            var files = new List<ProjectFile>();
            var kept = new Dictionary<string, string?>();
            foreach (var file in stored.Files.Where(file => file?.Path is not null))
            {
                // An earlier version of this page kept the text inside the project's entry.
                // It is read from there, and moves to an entry of its own at the next save.
                var content = file.Content;
                if (content is null)
                    kept[file.Path!] = content = await GetAsync(FileKey(projectId, file.Path!)) ?? string.Empty;

                files.Add(new ProjectFile(file.Path!, content));
            }

            _stored[projectId] = new Stored(stored.Files.Any(file => file?.Content is not null) ? null : description, kept);
            return new Project(stored.Id, stored.Name, stored.Kind, stored.Entry, files);
        }

        public async Task SaveAsync(Project project)
        {
            if (!_stored.TryGetValue(project.Id, out var stored))
                _stored[project.Id] = stored = new Stored(null, await StoredPathsAsync(project.Id));

            foreach (var file in project.Files)
            {
                if (stored.Files.TryGetValue(file.Path, out var content) && content == file.Content)
                    continue;

                await SetAsync(FileKey(project.Id, file.Path), file.Content);
                stored.Files[file.Path] = file.Content;
            }

            var description = JsonSerializer.Serialize(
                new StoredProject(project.Id, project.Name, project.Kind, project.Entry, project.Files.Select(file => new StoredFile(file.Path, null)).ToList()),
                Json);
            if (description != stored.Description)
            {
                await SetAsync(ProjectKeyPrefix + project.Id, description);
                stored.Description = description;
            }

            // After the description no longer names them, so that a file is never listed without its text.
            foreach (var path in stored.Files.Keys.Except(project.Files.Select(file => file.Path)).ToList())
            {
                await RemoveAsync(FileKey(project.Id, path));
                stored.Files.Remove(path);
            }

            var index = await ReadIndexAsync();
            var row = new IndexRow(project.Id, project.Name);
            var position = index.Projects.FindIndex(other => other.Id == project.Id);
            if (position < 0)
                index.Projects.Add(row);
            else if (index.Projects[position] == row)
                return;
            else
                index.Projects[position] = row;

            await WriteIndexAsync(index);
        }

        public async Task DeleteAsync(string projectId)
        {
            var index = await ReadIndexAsync();
            index.Projects.RemoveAll(row => row.Id == projectId);
            await WriteIndexAsync(index with { LastOpened = index.LastOpened == projectId ? null : index.LastOpened });

            foreach (var path in (await StoredPathsAsync(projectId)).Keys)
                await RemoveAsync(FileKey(projectId, path));

            await RemoveAsync(ProjectKeyPrefix + projectId);
            _stored.Remove(projectId);
        }

        public async Task<string?> GetLastOpenedAsync() => (await ReadIndexAsync()).LastOpened;

        public async Task SetLastOpenedAsync(string projectId)
        {
            var index = await ReadIndexAsync();
            if (index.LastOpened != projectId)
                await WriteIndexAsync(index with { LastOpened = projectId });
        }

        private async Task<Index> ReadIndexAsync()
        {
            var stored = Read<StoredIndex>(await GetAsync(IndexKey));
            var rows = (stored?.Projects ?? [])
                .Where(row => row?.Id is not null && row.Name is not null)
                .Select(row => new IndexRow(row.Id!, row.Name!))
                .ToList();

            return new Index(rows, stored?.LastOpened);
        }

        private Task WriteIndexAsync(Index index)
        {
            var stored = new StoredIndex(1, index.LastOpened, index.Projects.Select(row => new StoredRow(row.Id, row.Name)).ToList());
            return SetAsync(IndexKey, JsonSerializer.Serialize(stored, Json));
        }

        /// <summary>
        /// What is stored may have been written by another version of the page, or edited by
        /// hand. Anything that does not read as expected counts as not being there.
        /// </summary>
        private static T? Read<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<T>(json, Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task<string?> GetAsync(string key)
        {
            string? value = null;
            await Guarded(async () => value = await localStorage.GetItemAsStringAsync(key));
            return value;
        }

        private Task SetAsync(string key, string value) => Guarded(async () => await localStorage.SetItemAsStringAsync(key, value));

        private Task RemoveAsync(string key) => Guarded(async () => await localStorage.RemoveItemAsync(key));

        private static string FileKey(string projectId, string path) => ProjectKeyPrefix + projectId + FileKeyInfix + path;

        /// <summary>
        /// The files storage has entries for, for a project this page has not read. Their text
        /// is not known, so each is null and will be written.
        /// </summary>
        private async Task<Dictionary<string, string?>> StoredPathsAsync(string projectId)
        {
            var stored = Read<StoredProject>(await GetAsync(ProjectKeyPrefix + projectId));
            return (stored?.Files ?? [])
                .Where(file => file?.Path is not null)
                .Select(file => file.Path!)
                .Distinct()
                .ToDictionary(path => path, _ => (string?)null);
        }

        private static async Task Guarded(Func<Task> useStorage)
        {
            try
            {
                await useStorage();
            }
            catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException or OperationCanceledException)
            {
                throw new ProjectStoreException("The browser's storage could not be used.", exception);
            }
        }

        private sealed record Index(List<IndexRow> Projects, string? LastOpened);

        private sealed record IndexRow(string Id, string Name);

        private sealed record StoredIndex(int Version, string? LastOpened, List<StoredRow>? Projects);

        private sealed record StoredRow(string? Id, string? Name);

        private sealed record StoredProject(string? Id, string? Name, ProjectKind Kind, string? Entry, List<StoredFile>? Files);

        /// <param name="Content">Null since each file has an entry of its own.</param>
        private sealed record StoredFile(string? Path, string? Content);

        /// <param name="Description">The project's entry as last read or written. Null when it has to be written again.</param>
        /// <param name="Files">The text of each file's entry as last read or written.</param>
        private sealed class Stored(string? description, Dictionary<string, string?> files)
        {
            public string? Description { get; set; } = description;

            public Dictionary<string, string?> Files { get; } = files;
        }
    }
}
