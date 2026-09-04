namespace ChaySocial.MainProject.Persistence
{
    /// <summary>
    /// Storage that belongs to the device rather than the server — where the master seed is held while the app runs.
    /// Nothing written here is ever sent anywhere.
    /// </summary>
    public interface ILocalStore
    {
        /// <summary> Reads the value stored under a key. </summary>
        /// <param name="key"> Key to read. </param>
        /// <returns> The stored text, or null when the key is absent. </returns>
        Task<string?> ReadAsync(string key);

        /// <summary> Stores a value under a key, replacing whatever was there. </summary>
        /// <param name="key"> Key to write. </param>
        /// <param name="value"> Text to store. </param>
        Task WriteAsync(string key, string value);

        /// <summary> Removes a key. Removing an absent key is not an error. </summary>
        /// <param name="key"> Key to remove. </param>
        Task DeleteAsync(string key);
    }

    /// <summary>
    /// Device storage that lives as long as the app is running. On the web this means a page refresh asks for the
    /// seed again — reaching the browser's own storage would need JavaScript, which this project does not ship. A
    /// MAUI host swaps in an implementation over the platform preferences API and the session survives restarts.
    /// </summary>
    public sealed class InMemoryLocalStore : ILocalStore
    {
        readonly Dictionary<string, string> _entries = [];
        readonly Lock _gate = new();

        public Task<string?> ReadAsync(string key)
        {
            lock (_gate) return Task.FromResult(_entries.TryGetValue(key, out string? value) ? value : null);
        }

        public Task WriteAsync(string key, string value)
        {
            lock (_gate) _entries[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key)
        {
            lock (_gate) _entries.Remove(key);
            return Task.CompletedTask;
        }
    }
}
