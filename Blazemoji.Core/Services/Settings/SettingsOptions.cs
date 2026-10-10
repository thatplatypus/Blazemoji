using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Services.Settings
{
    /// <summary>
    /// Which host this is and which sections of settings there are. The editor adds its own
    /// sections; a host adds any it has and says which host it is.
    /// </summary>
    public sealed class SettingsOptions
    {
        private readonly List<Type> _sections = [];

        /// <summary>A host that says nothing is taken for the website, which has no desktop-only settings.</summary>
        public SettingHosts Host { get; set; } = SettingHosts.Web;

        public IReadOnlyList<Type> Sections => _sections;

        public SettingsOptions Add<T>() where T : SettingsBase, new()
        {
            if (!_sections.Contains(typeof(T)))
                _sections.Add(typeof(T));

            return this;
        }
    }
}
