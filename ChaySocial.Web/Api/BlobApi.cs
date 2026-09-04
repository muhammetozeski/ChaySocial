using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Protection;
using ChaySocial.MainProject.Text;

namespace ChaySocial.Web.Api
{
    /// <summary>
    /// Holds uploaded media on disk, one file per blob. Everything stored here arrived already encrypted, so this
    /// class deals in opaque bytes and knows nothing about pictures or recordings — it cannot tell one from the
    /// other, which is the point.
    /// </summary>
    /// <param name="rootDirectory"> Directory the blobs live in; created when it does not exist. </param>
    public sealed class BlobFileStorage(string rootDirectory)
    {
        /// <summary> Random bytes behind a blob id. </summary>
        const int BlobIdBytes = 16;

        /// <summary> Extension every stored blob carries. </summary>
        const string BlobExtension = ".bin";

        /// <summary> Extension of the half-written file an upload uses before it is moved into place. </summary>
        const string PendingExtension = ".writing";

        /// <summary> Extension a blob is renamed to the instant one reader claims it, which is what makes the claim exclusive. </summary>
        const string ClaimedExtension = ".claimed";

        /// <summary> Where the blobs live, so a caller can report it at startup. </summary>
        public string RootDirectory => rootDirectory;

        /// <summary> Stores bytes under a fresh id. </summary>
        /// <param name="content"> The encrypted bytes. </param>
        /// <param name="cancellationToken"> Cancels the write. </param>
        /// <returns> The id the bytes can be fetched back with. </returns>
        public async Task<string> SaveAsync(byte[] content, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(rootDirectory);

            string blobId = Base32.Encode(RandomSource.Next(BlobIdBytes));
            string blobPath = PathFor(blobId);
            string pendingPath = blobPath + PendingExtension;

            await File.WriteAllBytesAsync(pendingPath, content, cancellationToken);
            File.Move(pendingPath, blobPath, overwrite: true);

            return blobId;
        }

        /// <summary> Fetches stored bytes. </summary>
        /// <param name="blobId"> Id from <see cref="SaveAsync"/>. </param>
        /// <param name="cancellationToken"> Cancels the read. </param>
        /// <returns> The bytes, or null when nothing is stored under that id. </returns>
        public async Task<byte[]?> ReadAsync(string blobId, CancellationToken cancellationToken = default)
        {
            string blobPath = PathFor(blobId);
            return File.Exists(blobPath) ? await File.ReadAllBytesAsync(blobPath, cancellationToken) : null;
        }

        /// <summary> Removes stored bytes. Removing something absent is not an error. </summary>
        /// <param name="blobId"> Id from <see cref="SaveAsync"/>. </param>
        public void Remove(string blobId) => File.Delete(PathFor(blobId));

        /// <summary>
        /// Reads bytes and deletes them in one step. The move to a temporary name happens before the read, so two
        /// requests racing for the same blob cannot both come away with a copy — the loser finds nothing, which is
        /// exactly the guarantee a one-time message needs.
        /// </summary>
        /// <param name="blobId"> Id from <see cref="SaveAsync"/>. </param>
        /// <param name="cancellationToken"> Cancels the read. </param>
        /// <returns> The bytes, or null when they were already taken or never existed. </returns>
        public async Task<byte[]?> ConsumeAsync(string blobId, CancellationToken cancellationToken = default)
        {
            string blobPath = PathFor(blobId);
            string claimedPath = blobPath + ClaimedExtension;

            try
            {
                File.Move(blobPath, claimedPath);
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException or IOException)
            {
                // Already taken, or never there: both mean this reader gets nothing, which is the point.
                return null;
            }

            try
            {
                return await File.ReadAllBytesAsync(claimedPath, cancellationToken);
            }
            finally
            {
                File.Delete(claimedPath);
            }
        }

        /// <summary>
        /// Builds the path one blob lives at, refusing any id that is not the shape this class hands out. Ids
        /// arrive from the network, and an id containing a separator or a parent segment would otherwise let a
        /// caller read or write a file outside the blob directory.
        /// </summary>
        /// <param name="blobId"> Id to resolve. </param>
        /// <returns> The full path to that blob's file. </returns>
        string PathFor(string blobId)
        {
            if (blobId.Length == 0 || !blobId.All(char.IsAsciiLetterOrDigit))
            {
                throw new ArgumentException("A blob id may only contain letters and digits.", nameof(blobId));
            }

            return Path.Combine(rootDirectory, blobId + BlobExtension);
        }
    }

    /// <summary> Publishes <see cref="BlobFileStorage"/> over the routes <see cref="BlobRoutes"/> describes. </summary>
    public static class BlobApi
    {
        /// <summary> Registers every blob route on the application. </summary>
        /// <param name="app"> Application to register on. </param>
        public static void MapBlobApi(this WebApplication app)
        {
            BlobFileStorage storage = app.Services.GetRequiredService<BlobFileStorage>();
            WritingPermitRegistry permits = app.Services.GetRequiredService<WritingPermitRegistry>();

            app.MapPost(BlobRoutes.Base, async (HttpRequest request, CancellationToken cancellationToken) =>
            {
                // Media is something an account puts in front of other people, so it sits behind the same permit
                // the words it travels with do.
                if (!permits.IsGranted(request.Headers[ProofRoutes.AccountHeader].ToString()))
                {
                    return Results.StatusCode(StatusCodes.Status402PaymentRequired);
                }

                using MemoryStream buffer = new();
                await request.Body.CopyToAsync(buffer, cancellationToken);

                if (buffer.Length is 0 or > BlobRoutes.MaximumUploadBytes) return Results.BadRequest();

                return Results.Text(await storage.SaveAsync(buffer.ToArray(), cancellationToken));
            });

            app.MapGet($"{BlobRoutes.Base}/{{blobId}}", async (string blobId, CancellationToken cancellationToken) =>
            {
                byte[]? content = await storage.ReadAsync(blobId, cancellationToken);
                return content is null ? Results.NotFound() : Results.Bytes(content);
            });

            app.MapPost($"{BlobRoutes.Base}/{{blobId}}/{BlobRoutes.ConsumeSegment}", async (string blobId, CancellationToken cancellationToken) =>
            {
                byte[]? content = await storage.ConsumeAsync(blobId, cancellationToken);
                return content is null ? Results.NotFound() : Results.Bytes(content);
            });

            app.MapDelete($"{BlobRoutes.Base}/{{blobId}}", (string blobId) =>
            {
                storage.Remove(blobId);
                return Results.Ok();
            });
        }
    }
}
