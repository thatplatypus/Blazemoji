namespace Blazemoji.Shared.Models.Settings
{
    /// <summary>Which hosts a section or a setting belongs to. Most belong to both.</summary>
    [Flags]
    public enum SettingHosts
    {
        None = 0,
        Web = 1,
        Desktop = 2,
        All = Web | Desktop,
    }
}
