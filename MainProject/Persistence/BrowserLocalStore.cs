using Blazored.LocalStorage;

namespace ChaySocial.MainProject.Persistence
{
    /// <summary>
    /// Device storage backed by the browser's own localStorage, so a page refresh does not throw the signed-in
    /// account away. What is written here stays in the browser and is never sent anywhere — which is the whole
    /// reason the master seed may live here at all.
    /// </summary>
    /// <param name="localStorage"> Blazored's typed wrapper over the browser storage API. </param>
    public sealed class BrowserLocalStore(ILocalStorageService localStorage) : ILocalStore
    {
        const string SourceName = nameof(BrowserLocalStore);

        public async Task<string?> ReadAsync(string key)
        {
            try
            {
                return await localStorage.GetItemAsStringAsync(key);
            }
            catch (Exception error)
            {
                // Browser storage is refused outright in some privacy modes; losing the session is survivable,
                // crashing the app on startup is not.
                Log($"{SourceName} could not read '{key}'.\n{error}", LogLevel.Warning);
                return null;
            }
        }

        public async Task WriteAsync(string key, string value)
        {
            try
            {
                await localStorage.SetItemAsStringAsync(key, value);
            }
            catch (Exception error)
            {
                Log($"{SourceName} could not write '{key}'; the session will not survive a refresh.\n{error}", LogLevel.Warning);
            }
        }

        public async Task DeleteAsync(string key)
        {
            try
            {
                await localStorage.RemoveItemAsync(key);
            }
            catch (Exception error)
            {
                Log($"{SourceName} could not remove '{key}'.\n{error}", LogLevel.Warning);
            }
        }
    }
}
