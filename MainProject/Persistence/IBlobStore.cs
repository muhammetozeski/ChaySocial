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

        /// <summary>
        /// Fetches stored bytes and destroys them in the same step, so a second request finds nothing. This is
        /// what makes a vanishing message actually vanish: the promise is kept by the server no longer holding
        /// the bytes, not by a client agreeing to forget them.
        /// </summary>
        /// <param name="blobId"> Id from <see cref="UploadAsync"/>. </param>
        /// <param name="cancellationToken"> Cancels the read. </param>
        /// <returns> The bytes, or null when they were already taken or never existed. </returns>
        Task<byte[]?> ConsumeAsync(string blobId, CancellationToken cancellationToken = default);
    }

    /// <summary> Routes the blob endpoints answer on, shared so client and server cannot drift apart. </summary>
    public static class BlobRoutes
    {
        /// <summary> Prefix every blob route sits under. </summary>
        public const string Base = "/api/blobs";

        /// <summary> Largest upload the server accepts, in bytes. </summary>
        public const int MaximumUploadBytes = 12 * 1024 * 1024;

        /// <summary> Path segment that turns a read into a read-and-destroy. </summary>
        public const string ConsumeSegment = "once";

        /// <summary> Builds the route one blob is read from, written to or removed at. </summary>
        /// <param name="blobId"> Id of the blob. </param>
        /// <returns> The route path. </returns>
        public static string Blob(string blobId) => $"{Base}/{Uri.EscapeDataString(blobId)}";

        /// <summary> Builds the route that reads a blob and destroys it in the same step. </summary>
        /// <param name="blobId"> Id of the blob. </param>
        /// <returns> The route path. </returns>
        public static string ConsumeBlob(string blobId) => $"{Blob(blobId)}/{ConsumeSegment}";
    }
}
