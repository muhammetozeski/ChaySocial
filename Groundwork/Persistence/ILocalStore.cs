using Groundwork.Outcomes;

namespace Groundwork.Persistence
{
    /// <summary>
    /// Key/value storage that belongs to the device rather than to the backend — the place a client keeps things the
    /// server must never receive, such as an encrypted identity seed or an unsent draft. Implementations differ per
    /// host (browser storage, MAUI secure storage, a file on disk) which is exactly why callers only see this contract.
    /// </summary>
    public interface ILocalStore
    {
        /// <summary> Reads the value stored under a key. </summary>
        /// <param name="key"> Key to read. </param>
        /// <param name="cancellationToken"> Cancels the read. </param>
        /// <returns> The stored text on success; a failure when the key is absent or the device refused the read. </returns>
        Task<Result<string>> ReadAsync(string key, CancellationToken cancellationToken = default);

        /// <summary> Stores a value under a key, replacing whatever was there. </summary>
        /// <param name="key"> Key to write. </param>
        /// <param name="value"> Text to store. </param>
        /// <param name="cancellationToken"> Cancels the write. </param>
        /// <returns> Success, or the reason the device refused the write. </returns>
        Task<Result> WriteAsync(string key, string value, CancellationToken cancellationToken = default);

        /// <summary> Removes a key. Removing an absent key succeeds, so callers do not have to check first. </summary>
        /// <param name="key"> Key to remove. </param>
        /// <param name="cancellationToken"> Cancels the delete. </param>
        /// <returns> Success, or the reason the device refused the delete. </returns>
        Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Device storage that lives only as long as the process. Used while a host has no persistent store wired up yet,
    /// and in tests, where a run must not leave anything behind on the machine.
    /// </summary>
    public sealed class InMemoryLocalStore : ILocalStore
    {
        readonly Dictionary<string, string> _entries = [];
        readonly Lock _gate = new();

        public Task<Result<string>> ReadAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                return Task.FromResult(_entries.TryGetValue(key, out string? value)
                    ? Result<string>.Success(value)
                    : Result<string>.Failure($"'{key}' is not stored on this device."));
            }
        }

        public Task<Result> WriteAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate) _entries[key] = value;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate) _entries.Remove(key);
            return Task.FromResult(Result.Success());
        }
    }
}

