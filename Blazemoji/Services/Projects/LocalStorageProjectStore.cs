using System.Text.Json;
using System.Text.Json.Serialization;
using Blazemoji.Shared.Models.Projects;
using Microsoft.JSInterop;

namespace Blazemoji.Services.Projects
{
    /// <summary>
    /// Keeps projects in the browser's local storage: one entry listing them, and one entry
    /// per project. Local storage can only be reached once the page has rendered, is small
    /// (about 5 MB), and can be switched off, so every failure is a <see cref="ProjectStoreException"/>.
    /// </summary>
    public sealed class LocalStorageProjectStore(ILocalStorageService localStorage) : IProjectStore
    {
        private const string IndexKey = "blazemoji.projects";
        private const string ProjectKeyPrefix = "blazemoji.project.";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };

        public async Task<IReadOnlyList<ProjectSummary>> ListAsync() =>
            (await ReadIndexAsync()).Projects.Select(row => new ProjectSummary(row.Id, row.Name)).ToList();

        public async Task<Project?> LoadAsync(string projectId)
        {
            var stored = Read<StoredProject>(await GetAsync(ProjectKeyPrefix + projectId));
            if (stored?.Id is null || stored.Name is null || stored.Entry is null || stored.Files is null)
                return null;

            var files = stored.Files
                .Where(file => file?.Path is not null)
                .Select(file => new ProjectFile(file.Path!, file.Content ?? string.Empty))
                .ToList();

            return new Project(stored.Id, stored.Name, stored.Kind, stored.Entry, files);
        }

        public async Task SaveAsync(Project project)
        {
            var stored = new StoredProject(
                project.Id,
                project.Name,
                project.Kind,
                project.Entry,
                project.Files.Select(file => new StoredFile(file.Path, file.Content)).ToList());
            await SetAsync(ProjectKeyPrefix + project.Id, JsonSerializer.Serialize(stored, Json));

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

            await Guarded(async () => await localStorage.RemoveItemAsync(ProjectKeyPrefix + projectId));
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

        private sealed record StoredFile(string? Path, string? Content);
    }
}
