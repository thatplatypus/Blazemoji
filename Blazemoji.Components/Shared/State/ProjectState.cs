using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Projects;
using Blazemoji.Toolchain;

namespace Blazemoji.Shared.State
{
    /// <summary>
    /// Owns the project being edited, the list of saved projects and which file is open.
    /// Components render from it and ask it for changes; it saves through <see cref="IProjectStore"/>.
    /// A change that cannot be made is refused with a sentence to show, and null means it was made.
    /// </summary>
    public sealed class ProjectState
    {
        public const string FileNameRule = "Use letters, digits, emoji, dots, hyphens and underscores, with / between folders.";
        public const string HiddenNameRule = "A name cannot start with a dot.";
        public const string DescriptionNameRule = "That name is kept for the project's own description.";

        private const string SourceExtension = ".🍇";
        private const int LongestName = 80;

        private readonly IProjectStore _store;
        private readonly IProjectTemplates _templates;
        private readonly ILogger<ProjectState> _logger;
        private readonly List<ProjectSummary> _projects = [];

        // Projects whose latest state could not be saved. They are only here, so this is
        // where they are opened from for as long as the page lives.
        private readonly Dictionary<string, Project> _onlyInMemory = [];

        public ProjectState(IProjectStore store, IProjectTemplates templates, ILogger<ProjectState> logger)
        {
            _store = store;
            _templates = templates;
            _logger = logger;

            // Something to show and edit before the saved projects have been read.
            Current = FromTemplate(DefaultTemplate, DefaultTemplate.Name);
            OpenPath = Current.Entry;
        }

        public event Action? StateChanged;

        /// <summary>False until the saved projects have been read.</summary>
        public bool Loaded { get; private set; }

        public Project Current { get; private set; }

        /// <summary>The path of the file shown in the editor. Always one of the current project's files.</summary>
        public string OpenPath { get; private set; }

        public ProjectFile OpenFile => Current.Find(OpenPath) ?? Current.Files[0];

        public IReadOnlyList<ProjectSummary> Projects => _projects;

        public IReadOnlyList<ProjectTemplate> Templates => _templates.All;

        /// <summary>
        /// The last attempt to save did not work, so what is on screen is only in memory.
        /// It stays there, and can be come back to, until the app is closed.
        /// </summary>
        public bool SaveFailed { get; private set; }

        private ProjectTemplate DefaultTemplate => _templates.All[0];

        public async Task LoadAsync()
        {
            try
            {
                _projects.Clear();
                _projects.AddRange(await _store.ListAsync());

                var lastOpened = await _store.GetLastOpenedAsync();
                var candidates = _projects.Select(project => project.Id).OrderBy(id => id == lastOpened ? 0 : 1);

                Project? opened = null;
                foreach (var id in candidates)
                {
                    opened = Repair(await _store.LoadAsync(id));
                    if (opened is not null)
                        break;
                }

                if (opened is not null)
                {
                    Show(opened);
                }
                else
                {
                    Show(FromTemplate(DefaultTemplate, DefaultTemplate.Name));
                    _projects.Add(Summary(Current));
                    await _store.SaveAsync(Current);
                    await _store.SetLastOpenedAsync(Current.Id);
                }

                SaveFailed = false;
            }
            catch (ProjectStoreException exception)
            {
                _logger.LogWarning(exception, "Saved projects could not be read");
                SaveFailed = true;
                _projects.Clear();
                _projects.Add(Summary(Current));
                _onlyInMemory[Current.Id] = Current;
            }

            Loaded = true;
            NotifyStateChanged();
        }

        public async Task<string?> CreateAsync(string name, string templateId)
        {
            if (CleanName(name) is not { } cleanName)
                return "Give the project a name.";

            if (_templates.All.FirstOrDefault(template => template.Id == templateId) is not { } template)
                return "That template is not available.";

            Show(FromTemplate(template, cleanName));
            _projects.Add(Summary(Current));

            await SaveAsync(alsoLastOpened: true);
            NotifyStateChanged();
            return null;
        }

        public async Task OpenAsync(string projectId)
        {
            if (projectId == Current.Id)
                return;

            try
            {
                if (await FindAsync(projectId) is not { } project)
                    return;

                Show(project);
                await _store.SetLastOpenedAsync(project.Id);
            }
            catch (ProjectStoreException exception)
            {
                _logger.LogWarning(exception, "A saved project could not be opened");
                SaveFailed = true;
            }

            NotifyStateChanged();
        }

        public async Task<string?> RenameProjectAsync(string name)
        {
            if (CleanName(name) is not { } cleanName)
                return "Give the project a name.";

            Current = Current with { Name = cleanName };
            var index = _projects.FindIndex(project => project.Id == Current.Id);
            if (index >= 0)
                _projects[index] = Summary(Current);

            await SaveAsync();
            NotifyStateChanged();
            return null;
        }

        public async Task DeleteProjectAsync(string projectId)
        {
            _projects.RemoveAll(project => project.Id == projectId);
            _onlyInMemory.Remove(projectId);

            try
            {
                await _store.DeleteAsync(projectId);
            }
            catch (ProjectStoreException exception)
            {
                _logger.LogWarning(exception, "A project could not be deleted from storage");
                SaveFailed = true;
            }

            if (projectId == Current.Id)
                await ShowAnotherAsync();

            NotifyStateChanged();
        }

        /// <summary>Shows the first listed project that can be opened, or a fresh one when none can.</summary>
        private async Task ShowAnotherAsync()
        {
            Project? next = null;
            try
            {
                foreach (var candidate in _projects)
                {
                    next = await FindAsync(candidate.Id);
                    if (next is not null)
                        break;
                }
            }
            catch (ProjectStoreException exception)
            {
                _logger.LogWarning(exception, "The remaining projects could not be read");
                SaveFailed = true;
            }

            if (next is not null)
            {
                Show(next);
                await RememberOpenedAsync();
                return;
            }

            Show(FromTemplate(DefaultTemplate, DefaultTemplate.Name));
            _projects.Add(Summary(Current));
            await SaveAsync(alsoLastOpened: true);
        }

        /// <summary>The project as it was last seen on this page if that never reached storage, otherwise as stored.</summary>
        private async Task<Project?> FindAsync(string projectId) =>
            _onlyInMemory.TryGetValue(projectId, out var kept) ? kept : Repair(await _store.LoadAsync(projectId));

        private async Task RememberOpenedAsync()
        {
            try
            {
                await _store.SetLastOpenedAsync(Current.Id);
            }
            catch (ProjectStoreException exception)
            {
                _logger.LogWarning(exception, "The open project could not be remembered");
                SaveFailed = true;
            }
        }

        public async Task<string?> AddFileAsync(string path)
        {
            var (cleanPath, refusal) = CheckNewPath(path, replacing: null);
            if (cleanPath is null)
                return refusal;

            Current = Current with { Files = Sorted([.. Current.Files, new ProjectFile(cleanPath, string.Empty)]) };
            OpenPath = cleanPath;

            await SaveAsync();
            NotifyStateChanged();
            return null;
        }

        public async Task<string?> RenameFileAsync(string from, string to)
        {
            if (Current.Find(from) is not { } file)
                return "That file is no longer in the project.";

            var (cleanPath, refusal) = CheckNewPath(to, replacing: from);
            if (cleanPath is null)
                return refusal;

            if (cleanPath == from)
                return null;

            Current = Current with
            {
                Files = Sorted([.. Current.Files.Where(other => other.Path != from), file with { Path = cleanPath }]),
                Entry = Current.Entry == from ? cleanPath : Current.Entry,
            };
            if (OpenPath == from)
                OpenPath = cleanPath;

            await SaveAsync();
            NotifyStateChanged();
            return null;
        }

        public async Task<string?> DeleteFileAsync(string path)
        {
            if (Current.Find(path) is null)
                return null;

            if (Current.Files.Count == 1)
                return "A project needs at least one file.";

            var remaining = Current.Files.Where(file => file.Path != path).ToList();
            Current = Current with
            {
                Files = remaining,
                Entry = Current.Entry == path ? remaining[0].Path : Current.Entry,
            };
            if (OpenPath == path)
                OpenPath = Current.Entry;

            await SaveAsync();
            NotifyStateChanged();
            return null;
        }

        public async Task SetEntryAsync(string path)
        {
            if (Current.Find(path) is null || Current.Entry == path)
                return;

            Current = Current with { Entry = path };
            await SaveAsync();
            NotifyStateChanged();
        }

        public async Task SetKindAsync(ProjectKind kind)
        {
            if (Current.Kind == kind)
                return;

            Current = Current with { Kind = kind };
            await SaveAsync();
            NotifyStateChanged();
        }

        public void SelectFile(string path)
        {
            if (Current.Find(path) is null || OpenPath == path)
                return;

            OpenPath = path;
            NotifyStateChanged();
        }

        /// <summary>
        /// Takes what was typed into a file. Subscribers are not told: nothing they draw
        /// depends on a file's text, and this is called as often as the editor settles.
        /// </summary>
        public async Task UpdateContentAsync(string path, string content)
        {
            if (Current.Find(path) is not { } file || file.Content == content)
                return;

            Current = Current with { Files = Current.Files.Select(other => other.Path == path ? other with { Content = content } : other).ToList() };

            var failedBefore = SaveFailed;
            await SaveAsync();
            if (SaveFailed != failedBefore)
                NotifyStateChanged();
        }

        private async Task SaveAsync(bool alsoLastOpened = false)
        {
            try
            {
                await _store.SaveAsync(Current);
                _onlyInMemory.Remove(Current.Id);

                // Storage is working: whatever else was waiting for it goes in now.
                foreach (var waiting in _onlyInMemory.Values.ToList())
                {
                    await _store.SaveAsync(waiting);
                    _onlyInMemory.Remove(waiting.Id);
                }

                if (alsoLastOpened)
                    await _store.SetLastOpenedAsync(Current.Id);

                SaveFailed = false;
            }
            catch (ProjectStoreException exception)
            {
                _logger.LogWarning(exception, "The project could not be saved");
                SaveFailed = true;
                _onlyInMemory[Current.Id] = Current;
            }
        }

        private void Show(Project project)
        {
            Current = project;
            OpenPath = project.Entry;
        }

        /// <returns>The path to use, or null and why not.</returns>
        private (string? Path, string? Refusal) CheckNewPath(string path, string? replacing)
        {
            var clean = path.Trim();
            if (clean.Length == 0)
                return (null, "Give the file a name.");

            var lastSegment = clean[(clean.LastIndexOf('/') + 1)..];
            if (!lastSegment.Contains('.'))
                clean += SourceExtension;

            if (!SourceFileNames.IsSafe(clean))
                return (null, FileNameRule);

            // A host that keeps projects as folders leaves hidden files alone, and keeps one
            // file of its own beside the project's. A file the project could not get back
            // from there is not one to make.
            if (clean.Split('/').Any(segment => segment.StartsWith('.')))
                return (null, HiddenNameRule);

            if (SameOnAnyDisk(clean, FileProjectStore.DescriptionFile))
                return (null, DescriptionNameRule);

            if (clean == replacing)
                return (clean, null);

            // Names are compared as a disk that ignores the case of letters would compare
            // them. Two files such a disk cannot keep apart would be one file there.
            var others = Current.Files.Select(file => file.Path).Where(other => other != replacing).ToList();
            if (others.Any(other => SameOnAnyDisk(other, clean)))
                return (null, "A file with that name already exists.");

            if (others.Any(other => StartsWithOnAnyDisk(clean, other + "/")))
                return (null, "A file cannot be inside another file.");

            if (others.Any(other => StartsWithOnAnyDisk(other, clean + "/")))
                return (null, "A folder with that name already exists.");

            return (clean, null);
        }

        private static bool SameOnAnyDisk(string one, string other) =>
            string.Equals(one.Normalize(), other.Normalize(), StringComparison.OrdinalIgnoreCase);

        private static bool StartsWithOnAnyDisk(string path, string start) =>
            path.Normalize().StartsWith(start.Normalize(), StringComparison.OrdinalIgnoreCase);

        private static Project FromTemplate(ProjectTemplate template, string name) =>
            new(Guid.NewGuid().ToString("N"), name, template.Kind, template.Entry, Sorted(template.Files));

        /// <summary>
        /// What is in storage was written by an earlier version of this page, or by hand. A
        /// project is only shown once it has files the toolchain would accept and an entry
        /// among them.
        /// </summary>
        private static Project? Repair(Project? stored)
        {
            if (stored?.Files is null)
                return null;

            var files = Sorted(stored.Files
                .Where(file => file?.Path is not null && SourceFileNames.IsSafe(file.Path))
                .DistinctBy(file => file.Path)
                .Select(file => file with { Content = file.Content ?? string.Empty }));
            if (files.Count == 0)
                return null;

            return stored with
            {
                Name = CleanName(stored.Name ?? string.Empty) ?? "Untitled",
                Files = files,
                Entry = files.Any(file => file.Path == stored.Entry) ? stored.Entry : files[0].Path,
            };
        }

        private static List<ProjectFile> Sorted(IEnumerable<ProjectFile> files) =>
            files.OrderBy(file => file.Path, StringComparer.Ordinal).ToList();

        private static string? CleanName(string name)
        {
            var clean = name.Trim();
            if (clean.Length == 0)
                return null;

            if (clean.Length <= LongestName)
                return clean;

            // Never through the middle of a character that takes two places, as most emoji do.
            var end = char.IsHighSurrogate(clean[LongestName - 1]) ? LongestName - 1 : LongestName;
            return clean[..end].TrimEnd();
        }

        private static ProjectSummary Summary(Project project) => new(project.Id, project.Name);

        private void NotifyStateChanged() => StateChanged?.Invoke();
    }
}
