using Blazemoji.Services.Projects;
using Microsoft.Extensions.Options;

namespace Blazemoji.Services.Settings
{
    /// <summary>
    /// Keeps the settings as one file in the app's own folder under
    /// <see cref="FileProjectStoreOptions.Root"/>, beside the layout.
    /// </summary>
    public sealed class FileSettingsStore(IOptions<FileProjectStoreOptions> options, TimeProvider clock) : ISettingsStore
    {
        private const string SettingsFile = "settings.json";

        private readonly ProjectsFolder _disk = new(options.Value.Root, clock);

        private string SettingsPath => Path.Combine(_disk.Root, ProjectsFolder.OwnFolder, SettingsFile);

        public Task<string?> LoadAsync() => _disk.InTurn(() => ProjectsFolder.ReadTextAsync(SettingsPath));

        public Task SaveAsync(string settings) => _disk.InTurn(() => ProjectsFolder.WriteTextAsync(SettingsPath, settings));
    }
}
