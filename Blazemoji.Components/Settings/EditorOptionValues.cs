using BlazorMonaco.Editor;

namespace Blazemoji.Settings
{
    /// <summary>
    /// The editor's settings as Monaco wants them. Two of these are equal when they would make
    /// the editor look the same, which is how the editor knows whether there is anything to do.
    /// </summary>
    public sealed record EditorOptionValues(int FontSize, string WordWrap, bool Minimap, string LineNumbers, string RenderWhitespace)
    {
        private const string On = "on";
        private const string Off = "off";

        public static EditorOptionValues From(EditorSettings settings) => new(
            settings.FontSize,
            settings.WordWrap ? On : Off,
            settings.Minimap,
            settings.LineNumbers ? On : Off,
            settings.RenderWhitespace);

        /// <summary>Puts them onto the options an editor is made with, or changed with.</summary>
        public void ApplyTo(EditorOptions options)
        {
            options.FontSize = FontSize;
            options.WordWrap = WordWrap;
            options.Minimap = new EditorMinimapOptions { Enabled = Minimap };
            options.LineNumbers = LineNumbers;
            options.RenderWhitespace = RenderWhitespace;
        }

        public EditorUpdateOptions ToUpdateOptions()
        {
            var options = new EditorUpdateOptions();
            ApplyTo(options);
            return options;
        }
    }
}
