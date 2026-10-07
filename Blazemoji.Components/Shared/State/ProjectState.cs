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

        private const string SourceExtension = ".🍇";
        private const int LongestName = 80;

        private readonly IProjectStore _store;
        private readonly IProjectTemplates _templates;
        private readonly ILogger<ProjectState> _logger;
        private readonly List<ProjectSummary> _projects = [];

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
                if (Repair(await _store.LoadAsync(projectId)) is not { } project)
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

            try
            {
                await _store.DeleteAsync(projectId);

                if (projectId == Current.Id)
                {
                    Project? next = null;
                    foreach (var candidate in _projects)
                    {
                        next = Repair(await _store.LoadAsync(candidate.Id));
                        if (next is not null)
                            break;
                    }

                    if (next is null)
                    {
                        next = FromTemplate(DefaultTemplate, DefaultTemplate.Name);
                        _projects.Add(Summary(next));
                        await _store.SaveAsync(next);
                    }

                    Show(next);
                    await _store.SetLastOpenedAsync(next.Id);
                }
            }
            catch (ProjectStoreException exception)
            {
                _logger.LogWarning(exception, "A project could not be deleted from storage");
                SaveFailed = true;
            }

            NotifyStateChanged();
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
                if (alsoLastOpened)
                    await _store.SetLastOpenedAsync(Current.Id);

                SaveFailed = false;
            }
            catch (ProjectStoreException exception)
            {
                _logger.LogWarning(exception, "The project could not be saved");
                SaveFailed = true;
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

            if (clean == replacing)
                return (clean, null);

            var others = Current.Files.Select(file => file.Path).Where(other => other != replacing).ToList();
            if (others.Contains(clean))
                return (null, "A file with that name already exists.");

            if (others.Any(other => clean.StartsWith(other + "/", StringComparison.Ordinal)))
                return (null, "A file cannot be inside another file.");

            if (others.Any(other => other.StartsWith(clean + "/", StringComparison.Ordinal)))
                return (null, "A folder with that name already exists.");

            return (clean, null);
        }

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

            return clean.Length <= LongestName ? clean : clean[..LongestName];
        }

        private static ProjectSummary Summary(Project project) => new(project.Id, project.Name);

        private void NotifyStateChanged() => StateChanged?.Invoke();
    }
}
