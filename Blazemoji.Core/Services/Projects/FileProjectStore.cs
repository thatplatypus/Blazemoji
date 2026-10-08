using System.Text;
using System.Text.Json;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Toolchain;
using Microsoft.Extensions.Options;

namespace Blazemoji.Services.Projects
{
    public sealed class FileProjectStoreOptions
    {
        public const string SectionName = "Projects";

        /// <summary>The folder that holds a folder for each project. A leading <c>~/</c> is the user's home.</summary>
        public string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Blazemoji");
    }

    /// <summary>
    /// Keeps projects on disk, where other tools can reach them: one folder under
    /// <see cref="FileProjectStoreOptions.Root"/> for each project, named after it, holding its
    /// files as they are and a <see cref="DescriptionFile"/> that says what the project is
    /// called, how it runs and where it starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The folder is also the user's. They may keep other things in it, put it under version
    /// control, and edit its files elsewhere. So a project's files are the text files in it
    /// that the compiler would take by name, read afresh each time the project is opened, and
    /// everything else is left alone and left out.
    /// </para>
    /// <para>
    /// Nothing is ever destroyed. A file that leaves a project, a file that something else
    /// changed and that is about to be written over, and a project that is deleted all go to
    /// <c>.blazemoji/trash</c> under the root, in a folder named for the moment and the project.
    /// </para>
    /// </remarks>
    public sealed class FileProjectStore(IOptions<FileProjectStoreOptions> options, TimeProvider clock) : IProjectStore
    {
        /// <summary>The file in a project's folder that makes it a project.</summary>
        public const string DescriptionFile = "blazemoji.json";

        /// <summary>A file larger than this many bytes is not source, and is left out.</summary>
        public const int LargestFile = 1024 * 1024;

        private const string StateFile = "state.json";
        private const string NameWhenNothingIsLeft = "Project";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ProjectsFolder _disk = new(options.Value.Root, clock);

        // What this store last read from or wrote to each project's folder. A save writes the difference.
        private readonly Dictionary<string, Kept> _kept = [];
        private bool _looked;

        private string Root => _disk.Root;

        public Task<IReadOnlyList<ProjectSummary>> ListAsync() => ProjectsFolder.Guarded<IReadOnlyList<ProjectSummary>>(async () =>
        {
            await LookAsync();
            return _kept.Select(pair => new ProjectSummary(pair.Key, pair.Value.Name)).ToList();
        });

        public Task<Project?> LoadAsync(string projectId) => ProjectsFolder.Guarded(async () =>
        {
            if (await FindAsync(projectId) is not { } kept)
                return null;

            var raw = await ProjectsFolder.ReadTextAsync(Path.Combine(kept.Folder, DescriptionFile));
            if (Described(raw) is not { } description)
                return null;

            kept.Name = description.Name!;
            kept.Description = raw;
            kept.Files.Clear();
            await ReadFilesAsync(kept.Folder, string.Empty, kept.Files);

            var files = kept.Files.OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => new ProjectFile(file.Key, file.Value)).ToList();
            return new Project(projectId, kept.Name, KindOf(description.Kind), description.Entry ?? string.Empty, files);
        });

        public Task SaveAsync(Project project) => ProjectsFolder.Guarded(async () =>
        {
            var kept = await FindAsync(project.Id);
            if (kept is null)
            {
                _kept[project.Id] = kept = new Kept(FreeFolderFor(project.Name, mine: null), project.Name);
            }
            else if (!Directory.Exists(kept.Folder))
            {
                kept.Description = null;
                kept.Files.Clear();
            }
            else if (kept.Name != project.Name)
            {
                var renamed = FreeFolderFor(project.Name, mine: kept.Folder);
                if (renamed != kept.Folder)
                {
                    Directory.Move(kept.Folder, renamed);
                    kept.Folder = renamed;
                }
            }

            kept.Name = project.Name;
            Directory.CreateDirectory(kept.Folder);

            var files = project.Files.Where(file => IsAProjectFile(file.Path)).ToList();
            foreach (var file in files)
            {
                if (kept.Files.TryGetValue(file.Path, out var known) && known == file.Content)
                    continue;

                var onDisk = ProjectsFolder.In(kept.Folder, file.Path);
                if (File.Exists(onDisk) && await ProjectsFolder.ReadTextAsync(onDisk) != known)
                    _disk.MoveToTrash(onDisk, kept.Folder, file.Path);

                await ProjectsFolder.WriteTextAsync(onDisk, file.Content);
                kept.Files[file.Path] = file.Content;
            }

            var description = Describe(project);
            if (description != kept.Description)
            {
                await ProjectsFolder.WriteTextAsync(Path.Combine(kept.Folder, DescriptionFile), description);
                kept.Description = description;
            }

            foreach (var path in kept.Files.Keys.Except(files.Select(file => file.Path)).ToList())
            {
                var onDisk = ProjectsFolder.In(kept.Folder, path);
                if (File.Exists(onDisk))
                    _disk.MoveToTrash(onDisk, kept.Folder, path);

                RemoveFoldersLeftEmpty(Path.GetDirectoryName(onDisk)!, kept.Folder);
                kept.Files.Remove(path);
            }
        });

        public Task DeleteAsync(string projectId) => ProjectsFolder.Guarded(async () =>
        {
            if (await FindAsync(projectId) is not { } kept)
                return;

            if (Directory.Exists(kept.Folder))
                _disk.MoveToTrash(kept.Folder, kept.Folder, relativePath: null);

            _kept.Remove(projectId);
            if (await ReadLastOpenedAsync() == projectId)
                File.Delete(StatePath);
        });

        public Task<string?> GetLastOpenedAsync() => ProjectsFolder.Guarded(ReadLastOpenedAsync);

        public Task SetLastOpenedAsync(string projectId) => ProjectsFolder.Guarded(async () =>
        {
            if (await ReadLastOpenedAsync() != projectId)
                await ProjectsFolder.WriteTextAsync(StatePath, "{ \"lastOpened\": " + Quoted(projectId) + " }\n");
        });

        private string StatePath => Path.Combine(Root, ProjectsFolder.OwnFolder, StateFile);

        private async Task<string?> ReadLastOpenedAsync()
        {
            try
            {
                return JsonSerializer.Deserialize<State>(await ProjectsFolder.ReadTextAsync(StatePath) ?? "null", Json)?.LastOpened;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task<Kept?> FindAsync(string projectId)
        {
            await LookAsync();
            return _kept.GetValueOrDefault(projectId);
        }

        /// <summary>
        /// Finds the project folders, once. A folder that was copied has the same description
        /// as the one it was copied from, and is given an identity of its own.
        /// </summary>
        private async Task LookAsync()
        {
            if (_looked)
                return;

            if (Directory.Exists(Root))
            {
                foreach (var folder in Directory.GetDirectories(Root).Order(StringComparer.Ordinal))
                {
                    if (Path.GetFileName(folder).StartsWith('.'))
                        continue;

                    var raw = await ProjectsFolder.ReadTextAsync(Path.Combine(folder, DescriptionFile));
                    if (Described(raw) is not { } description)
                        continue;

                    var id = description.Id!;
                    if (_kept.ContainsKey(id))
                    {
                        id = Guid.NewGuid().ToString("N");
                        raw = Describe(id, description.Name!, KindOf(description.Kind), description.Entry ?? string.Empty);
                        await ProjectsFolder.WriteTextAsync(Path.Combine(folder, DescriptionFile), raw);
                    }

                    _kept[id] = new Kept(folder, description.Name!) { Description = raw };
                }
            }

            _looked = true;
        }

        private async Task ReadFilesAsync(string folder, string prefix, Dictionary<string, string> into)
        {
            foreach (var path in Directory.GetFiles(folder))
            {
                var relative = prefix + Path.GetFileName(path);
                if (!IsAProjectFile(relative) || Path.GetFileName(path).StartsWith('.') || new FileInfo(path).Length > LargestFile)
                    continue;

                if (await ProjectsFolder.ReadTextAsync(path) is { } text)
                    into[relative] = text;
            }

            foreach (var inner in Directory.GetDirectories(folder))
            {
                var name = Path.GetFileName(inner);
                if (!name.StartsWith('.') && !File.GetAttributes(inner).HasFlag(FileAttributes.ReparsePoint))
                    await ReadFilesAsync(inner, prefix + name + "/", into);
            }
        }

        /// <summary>The project's files are those the compiler would take by name. Its description is the store's own.</summary>
        private static bool IsAProjectFile(string path) => SourceFileNames.IsSafe(path) && path != DescriptionFile;

        private static void RemoveFoldersLeftEmpty(string from, string projectFolder)
        {
            for (var folder = from; folder.Length > projectFolder.Length && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any(); folder = Path.GetDirectoryName(folder)!)
                Directory.Delete(folder);
        }

        /// <summary>
        /// A folder for a project of this name: the name itself where that is free, or is the
        /// project's own already, and otherwise the name with the first number that is.
        /// </summary>
        private string FreeFolderFor(string projectName, string? mine)
        {
            var name = ProjectsFolder.NameOnDisk(projectName, NameWhenNothingIsLeft);
            var taken = _kept.Values.Select(kept => kept.Folder).Where(folder => folder != mine).ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (var attempt = 1; ; attempt++)
            {
                var folder = Path.Combine(Root, attempt == 1 ? name : $"{name} {attempt}");
                if (folder == mine || (!taken.Contains(folder) && !Directory.Exists(folder) && !File.Exists(folder)))
                    return folder;
            }
        }

        private static string Describe(Project project) => Describe(project.Id, project.Name, project.Kind, project.Entry);

        // Written by hand so that emoji are written as they are. The serializer writes anything
        // outside the basic plane as an escape, which is no use to a person reading the file.
        private static string Describe(string id, string name, ProjectKind kind, string entry) =>
            "{\n" +
            $"  \"id\": {Quoted(id)},\n" +
            $"  \"name\": {Quoted(name)},\n" +
            $"  \"kind\": {Quoted(kind == ProjectKind.Server ? "server" : "program")},\n" +
            $"  \"entry\": {Quoted(entry)}\n" +
            "}\n";

        private static string Quoted(string text)
        {
            var quoted = new StringBuilder("\"");
            foreach (var c in text)
            {
                if (c is '"' or '\\')
                    quoted.Append('\\').Append(c);
                else if (char.IsControl(c))
                    quoted.Append("\\u").Append(((int)c).ToString("x4"));
                else
                    quoted.Append(c);
            }

            return quoted.Append('"').ToString();
        }

        /// <summary>
        /// A description may have been written by another version of the app, or by hand. One
        /// that does not say who the project is and what it is called counts as not being there.
        /// </summary>
        private static Description? Described(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            try
            {
                var description = JsonSerializer.Deserialize<Description>(raw, Json);
                return string.IsNullOrWhiteSpace(description?.Id) || string.IsNullOrWhiteSpace(description.Name) ? null : description;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static ProjectKind KindOf(string? kind) =>
            string.Equals(kind, "server", StringComparison.OrdinalIgnoreCase) ? ProjectKind.Server : ProjectKind.Program;

        private sealed record Description(string? Id, string? Name, string? Kind, string? Entry);

        private sealed record State(string? LastOpened);

        /// <param name="folder">Where the project is on disk.</param>
        /// <param name="name">What the project is called, which its folder may not be able to be.</param>
        private sealed class Kept(string folder, string name)
        {
            public string Folder { get; set; } = folder;

            public string Name { get; set; } = name;

            /// <summary>The description as last read or written. Null when it has to be written again.</summary>
            public string? Description { get; set; }

            /// <summary>The text of each of the project's files as last read or written.</summary>
            public Dictionary<string, string> Files { get; } = [];
        }
    }
}
