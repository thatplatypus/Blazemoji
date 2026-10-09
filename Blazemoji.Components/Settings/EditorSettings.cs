using Blazemoji.Shared.Models.Settings;

namespace Blazemoji.Settings
{
    /// <summary>
    /// How the editor shows text. Each default is what Monaco does when it is told nothing,
    /// except the font size: Monaco's own is 12 on macOS and 14 elsewhere, and a setting has
    /// one default.
    /// </summary>
    public sealed class EditorSettings : SettingsBase
    {
        public override string SettingsId => "editor";

        public override string DisplayName => "Editor";

        public override string Icon => BlazemojiIcons.EditorSettings;

        public override int Order => 10;

        [Setting(Label = "Font size", Description = "How large the editor's text is, in pixels.", Group = "Text", Order = 1, Min = 8, Max = 32)]
        public int FontSize { get; private set; } = 14;

        [Setting(Label = "Word wrap", Description = "Carry a long line over to the next instead of scrolling sideways.", Group = "Text", Order = 2)]
        public bool WordWrap { get; private set; }

        [Setting(Label = "Minimap", Description = "A small picture of the whole file beside the text.", Group = "Display", Order = 3)]
        public bool Minimap { get; private set; } = true;

        [Setting(Label = "Line numbers", Group = "Display", Order = 4)]
        public bool LineNumbers { get; private set; } = true;

        [Setting(Label = "Show whitespace", Description = "Where spaces and tabs are drawn as marks.", Group = "Display", Order = 5, Options = "none,boundary,selection,trailing,all")]
        public string RenderWhitespace { get; private set; } = "selection";
    }
}
