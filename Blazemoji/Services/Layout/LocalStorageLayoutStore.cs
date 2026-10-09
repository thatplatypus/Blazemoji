using Blazemoji.Services.Projects;
using Blazemoji.Shared.Models.Layout;
using Microsoft.JSInterop;

namespace Blazemoji.Services.Layout
{
    /// <summary>
    /// Keeps the layout in the browser's local storage, under one key beside the project
    /// store's. Local storage can only be reached once the page has rendered, and can be
    /// full or switched off, so every failure is a <see cref="ProjectStoreException"/>.
    /// </summary>
    public sealed class LocalStorageLayoutStore(ILocalStorageService localStorage) : ILayoutStore
    {
        private const string Key = LocalStorageProjectStore.KeyPrefix + "layout";

        public Task<WorkspaceLayout?> LoadAsync() => Guarded(async () =>
            WorkspaceLayoutJson.Read(await localStorage.GetItemAsStringAsync(Key)));

        public Task SaveAsync(WorkspaceLayout layout) => Guarded(async () =>
        {
            await localStorage.SetItemAsStringAsync(Key, WorkspaceLayoutJson.Write(layout));
            return true;
        });

        private static async Task<T> Guarded<T>(Func<Task<T>> useStorage)
        {
            try
            {
                return await useStorage();
            }
            catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException or OperationCanceledException)
            {
                throw new ProjectStoreException("The browser's storage could not be used.", exception);
            }
        }
    }
}
