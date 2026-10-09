namespace Blazemoji.Shared.Models.Settings
{
    /// <summary>
    /// Marks a property of a section as a setting and says how the panel shows it. The names
    /// are Mythetech.Framework's, so a section written for one reads the same in the other.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class SettingAttribute : Attribute
    {
        public string Label { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>The heading it is shown under. Settings with none come under no heading.</summary>
        public string? Group { get; set; }

        public int Order { get; set; } = 100;

        public double Min { get; set; } = double.NaN;

        public double Max { get; set; } = double.NaN;

        public double Step { get; set; } = 1;

        /// <summary>For a string: the values it may have, separated by commas. With these it is a choice, not free text.</summary>
        public string? Options { get; set; }

        /// <summary>Kept like any other, but not shown in the panel.</summary>
        public bool Hide { get; set; }

        public SettingHosts Hosts { get; set; } = SettingHosts.All;

        public bool HasRange => !double.IsNaN(Min) && !double.IsNaN(Max);
    }
}
