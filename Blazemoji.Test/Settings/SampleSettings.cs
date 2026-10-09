using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Test.Settings
{
    /// <summary>A section with one setting of each kind, one that is hidden and one for the desktop alone.</summary>
    internal sealed class SampleSettings : SettingsBase
    {
        public override string SettingsId => "sample";

        public override string DisplayName => "Sample";

        public override string Icon => string.Empty;

        [Setting(Label = "Size", Group = "Numbers", Order = 1, Min = 8, Max = 32)]
        public int Size { get; private set; } = 14;

        [Setting(Label = "Ratio", Group = "Numbers", Order = 2, Min = 0.5, Max = 3, Step = 0.1)]
        public double Ratio { get; private set; } = 1.5;

        [Setting(Label = "Wrap", Description = "Carry long lines over.", Group = "Switches", Order = 3)]
        public bool Wrap { get; private set; }

        [Setting(Label = "Whitespace", Group = "Choices", Order = 4, Options = "none,selection,all")]
        public string Whitespace { get; private set; } = "selection";

        [Setting(Label = "Name", Group = "Text", Order = 5)]
        public string Name { get; private set; } = "plain";

        [Setting(Label = "Remembered", Order = 6, Hide = true)]
        public int Remembered { get; private set; }

        [Setting(Label = "Channel", Group = "Desktop", Order = 7, Hosts = SettingHosts.Desktop)]
        public string Channel { get; private set; } = "stable";

        public int NotASetting { get; set; } = 1;
    }

    /// <summary>A whole section the website does not have.</summary>
    internal sealed class DesktopOnlySettings : SettingsBase
    {
        public override string SettingsId => "desktopOnly";

        public override string DisplayName => "Desktop only";

        public override string Icon => string.Empty;

        public override int Order => 90;

        public override SettingHosts Hosts => SettingHosts.Desktop;

        [Setting(Label = "Check at start")]
        public bool CheckAtStart { get; private set; } = true;
    }
}
