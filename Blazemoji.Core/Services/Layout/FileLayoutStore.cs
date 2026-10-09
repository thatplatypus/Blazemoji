using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Layout;
using Microsoft.Extensions.Options;

namespace Blazemoji.Services.Layout
{
    /// <summary>
    /// Keeps the layout as one small file in the app's own folder under
    /// <see cref="FileProjectStoreOptions.Root"/>, beside what the project store keeps there.
    /// </summary>
    public sealed class FileLayoutStore(IOptions<FileProjectStoreOptions> options, TimeProvider clock) : ILayoutStore
    {
        private const string LayoutFile = "layout.json";

        private readonly ProjectsFolder _disk = new(options.Value.Root, clock);

        private string LayoutPath => Path.Combine(_disk.Root, ProjectsFolder.OwnFolder, LayoutFile);

        public Task<WorkspaceLayout?> LoadAsync() => _disk.InTurn(async () =>
            WorkspaceLayoutJson.Read(await ProjectsFolder.ReadTextAsync(LayoutPath)));

        public Task SaveAsync(WorkspaceLayout layout) => _disk.InTurn(() =>
            ProjectsFolder.WriteTextAsync(LayoutPath, WorkspaceLayoutJson.Write(layout)));
    }
}
