namespace ChaySocial.MainProject.Persistence
{
    /// <summary>
    /// Storage for the large opaque byte runs documents cannot hold: pictures, recordings, video. What is handed
    /// here is already encrypted on the writer's device, so this layer — and the server behind it — only ever sees
    /// noise of a known length. Splitting it from <see cref="IDocumentStore"/> keeps documents small and queryable
    /// while media goes wherever media belongs.
    /// </summary>
    public interface IBlobStore
    {
        /// <summary> Stores a run of bytes and names it. </summary>
        /// <param name="content"> Already-encrypted bytes. </param>
        /// <param name="cancellationToken"> Cancels the upload. </param>
        /// <returns> The id the bytes can be fetched back with, or null when the store refused them. </returns>
        Task<string?> UploadAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

        /// <summary> Fetches stored bytes. </summary>
        /// <param name="blobId"> Id from <see cref="UploadAsync"/>. </param>
        /// <param name="cancellationToken"> Cancels the download. </param>
        /// <returns> The bytes, or null when nothing is stored under that id. </returns>
        Task<byte[]?> DownloadAsync(string blobId, CancellationToken cancellationToken = default);

        /// <summary> Removes stored bytes. Removing something absent is not an error. </summary>
        /// <param name="blobId"> Id from <see cref="UploadAsync"/>. </param>
        /// <param name="cancellationToken"> Cancels the delete. </param>
        Task DeleteAsync(string blobId, CancellationToken cancellationToken = default);
    }

    /// <summary> Routes the blob endpoints answer on, shared so client and server cannot drift apart. </summary>
    public static class BlobRoutes
    {
        /// <summary> Prefix every blob route sits under. </summary>
        public const string Base = "/api/blobs";

        /// <summary> Largest upload the server accepts, in bytes. </summary>
        public const int MaximumUploadBytes = 12 * 1024 * 1024;

        /// <summary> Builds the route one blob is read from, written to or removed at. </summary>
        /// <param name="blobId"> Id of the blob. </param>
        /// <returns> The route path. </returns>
        public static string Blob(string blobId) => $"{Base}/{Uri.EscapeDataString(blobId)}";
    }
}
