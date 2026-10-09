using Blazemoji.Services.Projects;
using Microsoft.JSInterop;

namespace Blazemoji.Services.Settings
{
    /// <summary>
    /// Keeps the settings in the browser's local storage, under one key beside the project
    /// store's. Local storage can only be reached once the page has rendered, and can be
    /// full or switched off, so every failure is a <see cref="ProjectStoreException"/>.
    /// </summary>
    public sealed class LocalStorageSettingsStore(ILocalStorageService localStorage) : ISettingsStore
    {
        private const string Key = LocalStorageProjectStore.KeyPrefix + "settings";

        public Task<string?> LoadAsync() => Guarded(async () => await localStorage.GetItemAsStringAsync(Key));

        public Task SaveAsync(string settings) => Guarded(async () =>
        {
            await localStorage.SetItemAsStringAsync(Key, settings);
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
