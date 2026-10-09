namespace Blazemoji.Shared.Models.Settings
{
    /// <summary>
    /// A section of settings. Each setting is a property of the section's own class with a
    /// <see cref="SettingAttribute"/> and a private setter: what a new section holds is the
    /// default, and only the settings state assigns anything else.
    /// </summary>
    public abstract class SettingsBase
    {
        /// <summary>What the section is kept under. It must not change once people have settings.</summary>
        public abstract string SettingsId { get; }

        public abstract string DisplayName { get; }

        public abstract string Icon { get; }

        public virtual int Order => 50;

        public virtual SettingHosts Hosts => SettingHosts.All;
    }
}
