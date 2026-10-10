using Blazemoji.Services.Projects;
using Blazemoji.Services.Settings;
using Blazemoji.Shared.Models.Settings;
using Microsoft.Extensions.Options;

namespace Blazemoji.Shared.State
{
    /// <summary>
    /// The settings of one session: one visitor on the website, the one window on the desktop.
    /// It makes the sections, is the only thing that changes a setting, and keeps them through
    /// an <see cref="ISettingsStore"/> when the host has one. A store that fails never stops a
    /// setting from changing.
    /// </summary>
    public sealed class SettingsState
    {
        private readonly SettingHosts _host;
        private readonly ILogger<SettingsState> _logger;
        private readonly ISettingsStore? _store;
        private readonly List<SettingsBase> _all;
        private Task? _loading;
        private int _saves;

        public SettingsState(IOptions<SettingsOptions> options, ILogger<SettingsState> logger, ISettingsStore? store = null)
        {
            _host = options.Value.Host;
            _logger = logger;
            _store = store;
            _all = options.Value.Sections.Select(type => (SettingsBase)Activator.CreateInstance(type)!).ToList();
            Sections = _all
                .Where(section => (section.Hosts & _host) != 0)
                .OrderBy(section => section.Order)
                .ThenBy(section => section.DisplayName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Raised with the section whose settings changed.</summary>
        public event Action<SettingsBase>? SettingsChanged;

        public event Action? SaveFailedChanged;

        /// <summary>This host's sections, in the order the panel shows them.</summary>
        public IReadOnlyList<SettingsBase> Sections { get; }

        /// <summary>The newest save did not succeed, so what is set lasts only as long as the session.</summary>
        public bool SaveFailed { get; private set; }

        /// <summary>A section this host does not show is given at its defaults.</summary>
        public T Get<T>() where T : SettingsBase =>
            _all.OfType<T>().FirstOrDefault()
            ?? throw new InvalidOperationException($"{typeof(T).Name} was not added to {nameof(SettingsOptions)}.");

        /// <summary>The settings of a section that the panel shows on this host.</summary>
        public IReadOnlyList<SettingProperty> ShownIn(SettingsBase section) =>
            SettingProperties.Of(section).Where(setting => setting.IsFor(_host) && !setting.Attribute.Hide).ToList();

        public bool IsAtDefaults(SettingsBase section) =>
            ShownIn(section).All(setting => Equals(setting.Read(section), setting.Default));

        /// <summary>
        /// Takes up what was kept, once. Only to be called where the browser can be reached:
        /// after the first render, or from something the user did.
        /// </summary>
        public Task LoadAsync()
        {
            if (_store is null)
                return Task.CompletedTask;

            if (_loading is not null)
                return _loading;

            // A store that answers or fails at once has finished before there is a task to
            // remember, and has already left a finished one here. A failure is for whoever
            // asked first; after it the defaults stand.
            var loading = LoadFromAsync(_store);
            _loading ??= loading;
            return loading;
        }

        private async Task LoadFromAsync(ISettingsStore from)
        {
            var changed = new List<SettingsBase>();
            try
            {
                string? text = null;
                try
                {
                    text = await from.LoadAsync();
                }
                catch (ProjectStoreException exception)
                {
                    _logger.LogWarning(exception, "The kept settings could not be read, so the defaults are used");
                }

                foreach (var kept in SettingsJson.Read(text, _all, _host))
                {
                    if (Equals(kept.Setting.Read(kept.Section), kept.Value))
                        continue;

                    kept.Setting.Assign(kept.Section, kept.Value);
                    if (!changed.Contains(kept.Section))
                        changed.Add(kept.Section);
                }
            }
            finally
            {
                // The load is over before anyone hears of it, however it ended. A failed load must
                // not stay remembered for every later change to rethrow, and a handler that asks
                // for the settings must not start another.
                _loading = Task.CompletedTask;
            }

            foreach (var section in changed)
                SettingsChanged?.Invoke(section);
        }

        public async Task SetAsync(SettingsBase section, string property, object? value)
        {
            var setting = SettingProperties.Of(Own(section)).FirstOrDefault(candidate => candidate.Name == property && candidate.IsFor(_host))
                ?? throw new ArgumentException($"{section.GetType().Name} has no setting called {property} on this host.", nameof(property));

            // A change made before the kept settings have arrived would be written over by them.
            await LoadAsync();

            if (!setting.TryAccept(value, out var accepted) || Equals(setting.Read(section), accepted))
                return;

            setting.Assign(section, accepted);
            SettingsChanged?.Invoke(section);
            await KeepAsync();
        }

        public async Task RestoreDefaultsAsync(SettingsBase section)
        {
            var shown = ShownIn(Own(section));
            await LoadAsync();

            var away = shown.Where(setting => !Equals(setting.Read(section), setting.Default)).ToList();
            if (away.Count == 0)
                return;

            foreach (var setting in away)
                setting.Assign(section, setting.Default);

            SettingsChanged?.Invoke(section);
            await KeepAsync();
        }

        private SettingsBase Own(SettingsBase section) =>
            Sections.Contains(section)
                ? section
                : throw new ArgumentException($"{section.GetType().Name} is not one of this host's sections, or belongs to another session.", nameof(section));

        private async Task KeepAsync()
        {
            if (_store is null)
                return;

            var mine = ++_saves;
            bool failed;
            try
            {
                await _store.SaveAsync(SettingsJson.Write(_all, _host));
                failed = false;
            }
            catch (ProjectStoreException exception)
            {
                _logger.LogWarning(exception, "The settings could not be kept, so they last only as long as this session");
                failed = true;
            }

            // Each save holds every setting, so only the newest says whether what is kept is what is set.
            if (mine != _saves || failed == SaveFailed)
                return;

            SaveFailed = failed;
            SaveFailedChanged?.Invoke();
        }
    }
}
