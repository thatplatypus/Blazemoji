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
    /// that the compiler would take by name, read afresh each time the project is opened.
    /// Everything else is left alone and left out: what is hidden, what is a link to somewhere
    /// else, what is not text, and what cannot be read.
    /// </para>
    /// <para>
    /// Nothing is ever destroyed. A file that leaves a project, a file that something else put
    /// or changed where the project is about to write, and a project that is deleted all go to
    /// <c>.blazemoji/trash</c> under the root, in a folder named for the moment and the project.
    /// </para>
    /// <para>
    /// A disk may not tell <c>Main</c> from <c>main</c>, so nothing here asks only whether a
    /// path exists when the answer decides what is thrown away. What leaves a project is dealt
    /// with before what arrives, so that a file renamed in the case of its letters, or to the
    /// name a folder had, finds its place free.
    /// </para>
    /// </remarks>
    public sealed class FileProjectStore(IOptions<FileProjectStoreOptions> options, TimeProvider clock, ILogger<FileProjectStore>? logger = null) : IProjectStore
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

        public Task<IReadOnlyList<ProjectSummary>> ListAsync() => _disk.InTurn<IReadOnlyList<ProjectSummary>>(async () =>
        {
            await LookAsync();
            return _kept.Select(pair => new ProjectSummary(pair.Key, pair.Value.Name)).ToList();
        });

        public Task<Project?> LoadAsync(string projectId) => _disk.InTurn(async () =>
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

        public Task SaveAsync(Project project) => _disk.InTurn(async () =>
        {
            var description = Describe(project);
            ProjectsFolder.MustBeWritable(description);

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
            var arriving = files.Where(file => !kept.Files.ContainsKey(file.Path)).ToList();

            foreach (var path in kept.Files.Keys.Except(files.Select(file => file.Path)).ToList())
            {
                var onDisk = ProjectsFolder.In(kept.Folder, path);
                var renamedTo = arriving.FirstOrDefault(file => file.Content == kept.Files[path]);
                if (renamedTo is not null && File.Exists(onDisk) && await ProjectsFolder.HoldsAsync(onDisk, renamedTo.Content))
                {
                    await MoveWithinAsync(kept, path, renamedTo.Path);
                    arriving.Remove(renamedTo);
                }
                else if (File.Exists(onDisk))
                {
                    _disk.MoveToTrash(onDisk, kept.Folder, path);
                }

                RemoveFoldersLeftEmpty(Path.GetDirectoryName(onDisk)!, kept.Folder);
                kept.Files.Remove(path);
            }

            foreach (var file in files)
            {
                if (kept.Files.TryGetValue(file.Path, out var known) && known == file.Content)
                    continue;

                var onDisk = ProjectsFolder.In(kept.Folder, file.Path);
                RefuseToFollowALink(kept.Folder, file.Path);
                if (File.Exists(onDisk) && !await ProjectsFolder.HoldsAsync(onDisk, known))
                    _disk.MoveToTrash(onDisk, kept.Folder, file.Path);

                await ProjectsFolder.WriteTextAsync(onDisk, file.Content);
                kept.Files[file.Path] = file.Content;
            }

            if (description != kept.Description)
            {
                await ProjectsFolder.WriteTextAsync(Path.Combine(kept.Folder, DescriptionFile), description);
                kept.Description = description;
            }
        });

        public Task DeleteAsync(string projectId) => _disk.InTurn(async () =>
        {
            if (await FindAsync(projectId) is not { } kept)
                return;

            if (Directory.Exists(kept.Folder))
                _disk.MoveToTrash(kept.Folder, kept.Folder, relativePath: null);

            _kept.Remove(projectId);
            if (await ReadLastOpenedAsync() == projectId)
                File.Delete(StatePath);
        });

        public Task<string?> GetLastOpenedAsync() => _disk.InTurn(ReadLastOpenedAsync);

        public Task SetLastOpenedAsync(string projectId) => _disk.InTurn(async () =>
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

        /// <summary>
        /// The project's folder as far as this store knows. A folder that is no longer where it
        /// was may have been renamed by hand, and is looked for by what its description says.
        /// </summary>
        private async Task<Kept?> FindAsync(string projectId)
        {
            await LookAsync();
            if (_kept.GetValueOrDefault(projectId) is not { } kept)
                return null;

            if (!Directory.Exists(kept.Folder))
            {
                var claimed = _kept.Values.Select(other => other.Folder).ToHashSet(StringComparer.Ordinal);
                foreach (var folder in ProjectFolders().Where(folder => !claimed.Contains(folder)))
                {
                    if (await DescriptionInAsync(folder) is { } elsewhere && elsewhere.Description.Id == projectId)
                    {
                        kept.Folder = folder;
                        break;
                    }
                }
            }

            return kept;
        }

        /// <summary>
        /// Finds the project folders, once. A folder that was copied has the same description
        /// as the one it was copied from, and is given an identity of its own. Nothing is
        /// remembered until every folder has been looked at, so that looking again after a
        /// failure does not find the folders it had already seen and take them for copies.
        /// </summary>
        private async Task LookAsync()
        {
            if (_looked)
                return;

            var found = new Dictionary<string, Kept>();
            foreach (var folder in ProjectFolders())
            {
                try
                {
                    if (await DescriptionInAsync(folder) is not { } read)
                        continue;

                    var (description, raw) = read;
                    var id = description.Id!;
                    if (found.ContainsKey(id))
                    {
                        id = Guid.NewGuid().ToString("N");
                        raw = Describe(id, description.Name!, KindOf(description.Kind), description.Entry ?? string.Empty);
                        await ProjectsFolder.WriteTextAsync(Path.Combine(folder, DescriptionFile), raw);
                    }

                    found[id] = new Kept(folder, description.Name!) { Description = raw };
                }
                catch (Exception exception) when (ProjectsFolder.IsTheDisksDoing(exception))
                {
                    logger?.LogWarning(exception, "The folder {Folder} could not be read as a project, and is left out", folder);
                }
            }

            foreach (var (id, kept) in found)
                _kept[id] = kept;

            _looked = true;
        }

        /// <summary>The folders under the root that could be projects, in the order of their names.</summary>
        private List<string> ProjectFolders() =>
            Directory.Exists(Root)
                ? Directory.GetDirectories(Root)
                    .Where(folder => !Path.GetFileName(folder).StartsWith('.') && !IsKeptForSnippets(Path.GetFileName(folder)))
                    .Order(StringComparer.Ordinal)
                    .ToList()
                : [];

        private static async Task<(Description Description, string Raw)?> DescriptionInAsync(string folder)
        {
            var raw = await ProjectsFolder.ReadTextAsync(Path.Combine(folder, DescriptionFile));
            return Described(raw) is { } description ? (description, raw!) : null;
        }

        private static bool IsKeptForSnippets(string folderName) => ProjectsFolder.SameName(folderName, ProjectsFolder.SnippetsFolder);

        /// <summary>One entry that cannot be read is left out. It does not take the project with it.</summary>
        private async Task ReadFilesAsync(string folder, string prefix, Dictionary<string, string> into)
        {
            foreach (var path in Directory.GetFiles(folder))
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith('.') || !IsAProjectFile(prefix + name))
                    continue;

                try
                {
                    var file = new FileInfo(path);
                    if (!ProjectsFolder.IsALink(file) && file.Length <= LargestFile && await ProjectsFolder.ReadTextAsync(path) is { } text)
                        into[prefix + name] = text;
                }
                catch (Exception exception) when (ProjectsFolder.IsTheDisksDoing(exception))
                {
                    logger?.LogWarning(exception, "The file {Path} could not be read, and is left out of its project", path);
                }
            }

            foreach (var inner in Directory.GetDirectories(folder))
            {
                var name = Path.GetFileName(inner);
                if (name.StartsWith('.'))
                    continue;

                try
                {
                    if (!ProjectsFolder.IsALink(new DirectoryInfo(inner)))
                        await ReadFilesAsync(inner, prefix + name + "/", into);
                }
                catch (Exception exception) when (ProjectsFolder.IsTheDisksDoing(exception))
                {
                    logger?.LogWarning(exception, "The folder {Path} could not be read, and is left out of its project", inner);
                }
            }
        }

        /// <summary>The project's files are those the compiler would take by name. Its description is the store's own.</summary>
        private static bool IsAProjectFile(string path) =>
            SourceFileNames.IsSafe(path) && !ProjectsFolder.SameName(path, DescriptionFile);

        /// <summary>
        /// A file that was renamed is moved, and keeps being the same file. Something else at
        /// the new name, spelt exactly so, is kept first. On a disk that ignores case the old
        /// name may be all that is "there", and that is the file being moved.
        /// </summary>
        private async Task MoveWithinAsync(Kept kept, string from, string to)
        {
            var source = ProjectsFolder.In(kept.Folder, from);
            var target = ProjectsFolder.In(kept.Folder, to);
            RefuseToFollowALink(kept.Folder, to);
            if (ProjectsFolder.IsListedExactly(target) && File.Exists(target) && !await ProjectsFolder.HoldsAsync(target, kept.Files[from]))
                _disk.MoveToTrash(target, kept.Folder, to);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target, overwrite: ProjectsFolder.IsListedExactly(target));
            kept.Files[to] = kept.Files[from];
        }

        /// <summary>
        /// A folder in the project that is a link leads somewhere else, and so does a file
        /// that is one. Writing there would be writing outside the project, so it is refused.
        /// </summary>
        private static void RefuseToFollowALink(string projectFolder, string path)
        {
            var segments = path.Split('/');
            var here = projectFolder;
            for (var index = 0; index < segments.Length; index++)
            {
                here = Path.Combine(here, segments[index]);
                FileSystemInfo entry = index == segments.Length - 1 ? new FileInfo(here) : new DirectoryInfo(here);
                if (entry.Exists && ProjectsFolder.IsALink(entry))
                    throw new IOException($"{path} goes through a link to somewhere outside the project.");
            }
        }

        private static void RemoveFoldersLeftEmpty(string from, string projectFolder)
        {
            for (var folder = from; folder.Length > projectFolder.Length && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any(); folder = Path.GetDirectoryName(folder)!)
                Directory.Delete(folder);
        }

        /// <summary>
        /// A folder for a project of this name: the name itself where that is free, or is the
        /// project's own already, and otherwise the name with the first number that is. A name
        /// is taken when anything under the root has it, in any case of letters, since the disk
        /// may not tell those apart. The folder that snippets are kept in is always taken.
        /// </summary>
        private string FreeFolderFor(string projectName, string? mine)
        {
            var name = ProjectsFolder.NameOnDisk(projectName, NameWhenNothingIsLeft);
            var myName = mine is null ? null : Path.GetFileName(mine);
            var taken = _kept.Values.Select(kept => Path.GetFileName(kept.Folder)).ToList();
            if (Directory.Exists(Root))
                taken.AddRange(Directory.EnumerateFileSystemEntries(Root).Select(entry => Path.GetFileName(entry)!));

            for (var attempt = 1; ; attempt++)
            {
                var candidate = attempt == 1 ? name : $"{name} {attempt}";
                if (candidate == myName)
                    return mine!;

                if (!IsKeptForSnippets(candidate) && !taken.Any(other => other != myName && ProjectsFolder.SameName(other, candidate)))
                    return Path.Combine(Root, candidate);
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
