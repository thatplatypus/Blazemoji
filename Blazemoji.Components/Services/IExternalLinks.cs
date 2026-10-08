namespace Blazemoji.Services
{
    /// <summary>
    /// Opens a link to somewhere outside the app. A host whose page is not in a browser, where
    /// a link would take the app's own window away, registers one. Without one the links in
    /// the title bar are ordinary links that open in a tab of their own.
    /// </summary>
    public interface IExternalLinks
    {
        Task OpenAsync(string url);
    }

    public static class BlazemojiLinks
    {
        public const string EmojicodeDocumentation = "https://www.emojicode.org/docs/reference/";

        public const string Repository = "https://github.com/thatplatypus/Blazemoji";
    }
}
