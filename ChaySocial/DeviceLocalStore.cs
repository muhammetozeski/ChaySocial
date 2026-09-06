using ChaySocial.MainProject.Persistence;
using static Logger;

namespace ChaySocial
{
    /// <summary>
    /// Device storage for the native builds, so closing the app does not throw the signed-in account away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The master seed is written to the platform's secure store where there is one — the Android keystore, and
    /// Windows' own credential protection — and to the ordinary preference store where there is not. Both stay on
    /// the device: nothing written here is ever sent anywhere, which is the whole reason a seed may live here.
    /// </para>
    /// <para>
    /// The fallback is not silent. An unpackaged Windows build has no credential vault to write to, and a person
    /// whose secret is being kept somewhere less protected than they might assume should be able to find that in
    /// the log rather than discover it later.
    /// </para>
    /// </remarks>
    public sealed class DeviceLocalStore : ILocalStore
    {
        const string SourceName = nameof(DeviceLocalStore);

        /// <summary> Reads a value, trying the secure store first and the preference store after it. </summary>
        /// <param name="key"> Key to read. </param>
        /// <returns> The stored text, or null when neither store holds it. </returns>
        public async Task<string?> ReadAsync(string key)
        {
            try
            {
                string? secure = await SecureStorage.Default.GetAsync(key);
                if (secure is not null) return secure;
            }
            catch (Exception error)
            {
                Log($"{SourceName} could not read '{key}' from the secure store; trying preferences.\n{error}", LogLevel.Warning);
            }

            try
            {
                string stored = Preferences.Default.Get(key, string.Empty);
                return stored.Length == 0 ? null : stored;
            }
            catch (Exception error)
            {
                Log($"{SourceName} could not read '{key}' at all. The session will not survive a restart.\n{error}", LogLevel.Error);
                return null;
            }
        }

        /// <summary> Writes a value to whichever store will take it. </summary>
        /// <param name="key"> Key to write. </param>
        /// <param name="value"> Text to store. </param>
        public async Task WriteAsync(string key, string value)
        {
            try
            {
                await SecureStorage.Default.SetAsync(key, value);
                return;
            }
            catch (Exception error)
            {
                Log($"{SourceName} could not write '{key}' to the secure store; falling back to preferences, which this device does not encrypt.\n{error}", LogLevel.Warning);
            }

            try
            {
                Preferences.Default.Set(key, value);
            }
            catch (Exception error)
            {
                Log($"{SourceName} could not write '{key}' at all. Nothing has been kept.\n{error}", LogLevel.Error);
            }
        }

        /// <summary> Removes a key from both stores, so signing out leaves nothing behind in either. </summary>
        /// <param name="key"> Key to remove. </param>
        public Task DeleteAsync(string key)
        {
            try
            {
                SecureStorage.Default.Remove(key);
            }
            catch (Exception error)
            {
                Log($"{SourceName} could not remove '{key}' from the secure store.\n{error}", LogLevel.Warning);
            }

            try
            {
                Preferences.Default.Remove(key);
            }
            catch (Exception error)
            {
                Log($"{SourceName} could not remove '{key}' from preferences.\n{error}", LogLevel.Warning);
            }

            return Task.CompletedTask;
        }
    }
}
