using MudBlazor;

namespace Blazemoji
{
    /// <summary>
    /// The icons Blazemoji uses, by what they mean in the app. Where Emojicode or everyday
    /// emoji already has a picture for the thing, that emoji is the icon: a grey glyph beside
    /// real emoji looks like it belongs to another app. Controls that only act (run, stop,
    /// add, copy, more) keep a plain glyph, which takes the colour of what it sits on.
    /// </summary>
    public static class BlazemojiIcons
    {
        public static string Run => Icons.Material.Filled.PlayArrow;

        public static string Stop => Icons.Material.Filled.Stop;

        public static string Send => Icons.Material.Filled.Send;

        public static string Add => Icons.Material.Filled.Add;

        public static string MoreActions => Icons.Material.Filled.MoreVert;

        /// <summary>The cross on a file's tab above the editor.</summary>
        public static string CloseTab => Icons.Material.Filled.Close;

        /// <summary>On the button that hides the sidebar while it is shown.</summary>
        public static string HideSidebar => Icons.Material.Filled.MenuOpen;

        /// <summary>On the same button while the sidebar is hidden.</summary>
        public static string ShowSidebar => Icons.Material.Filled.Menu;

        public static string GitHub => Icons.Custom.Brands.GitHub;

        public static string Copy => Icons.Material.Outlined.ContentCopy;

        public static string Error { get; } = Emoji("🚨");

        public static string Warning { get; } = Emoji("⚠️");

        /// <summary>The Files tab.</summary>
        public static string Files { get; } = Emoji("🗂️");

        /// <summary>📜 is how one Emojicode file includes another.</summary>
        public static string File { get; } = Emoji("📜");

        public static string Folder { get; } = Emoji("📁");

        public static string FolderOpen { get; } = Emoji("📂");

        /// <summary>Marks the file the compiler starts from. 🏁 is where an Emojicode program starts.</summary>
        public static string EntryFile { get; } = Emoji("🏁");

        public static string Toolbox { get; } = Emoji("🧰");

        public static string Library { get; } = Emoji("📚");

        public static string LightMode { get; } = Emoji("☀️");

        public static string DarkMode { get; } = Emoji("🌙");

        public static string Documentation { get; } = Emoji("📖");

        public static string KeyCommands { get; } = Emoji("🔣");

        public static string Settings { get; } = Emoji("⚙️");

        /// <summary>The Editor section of the settings.</summary>
        public static string EditorSettings { get; } = Emoji("📝");

        public static string Save { get; } = Emoji("💾");

        public static string EmojiPicker { get; } = Emoji("🙂");

        /// <summary>
        /// An emoji as an icon. MudBlazor draws an icon string as the inside of a 24 by 24
        /// SVG, so a text element holding the emoji goes wherever a glyph icon goes and takes
        /// its size from the same places. It is drawn in whatever emoji font the page uses.
        /// </summary>
        private static string Emoji(string emoji) =>
            $"<text class=\"emoji-icon\" x=\"12\" y=\"12\" font-size=\"19\" text-anchor=\"middle\" dominant-baseline=\"central\">{emoji}</text>";
    }
}
