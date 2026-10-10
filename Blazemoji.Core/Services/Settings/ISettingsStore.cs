using Blazemoji.Services.Projects;

namespace Blazemoji.Services.Settings
{
    /// <summary>
    /// Where settings are kept between visits, as one piece of text that <see cref="SettingsJson"/>
    /// makes and reads. A host that has somewhere to keep it registers one; without one the
    /// settings last as long as the page does.
    /// </summary>
    /// <remarks>Both methods throw <see cref="ProjectStoreException"/> when the storage cannot be used.</remarks>
    public interface ISettingsStore
    {
        /// <returns>Null when nothing has been kept.</returns>
        Task<string?> LoadAsync();

        Task SaveAsync(string settings);
    }
}
