using System.Net;
using System.Net.Http.Headers;
using ChaySocial.MainProject.Protection;

namespace ChaySocial.MainProject.Persistence
{
    /// <summary>
    /// Sends encrypted media to the blob server and fetches it back. Uploading costs the same proof of work a
    /// post does, so filling someone else's disk with junk is as expensive as writing.
    /// </summary>
    /// <param name="httpClient"> Client whose <c>BaseAddress</c> points at the blob server. </param>
    /// <param name="proofOfWork"> Supplies the proof the server charges for uploads, or null when it charges none. </param>
    public sealed class HttpBlobStore(HttpClient httpClient, ProofOfWorkClient? proofOfWork = null) : IBlobStore
    {
        /// <summary> Content type every upload is sent as; the real type lives in the encrypted document, not here. </summary>
        static readonly MediaTypeHeaderValue OpaqueContentType = new("application/octet-stream");

        public async Task<string?> UploadAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
        {
            if (content.Length > BlobRoutes.MaximumUploadBytes)
            {
                Log($"Refusing to upload {content.Length} bytes; the limit is {BlobRoutes.MaximumUploadBytes}.", LogLevel.Warning);
                return null;
            }

            try
            {
                ByteArrayContent body = new(content.ToArray());
                body.Headers.ContentType = OpaqueContentType;

                HttpRequestMessage request = new(HttpMethod.Post, BlobRoutes.Base) { Content = body };

                string? answer = proofOfWork is null ? null : await proofOfWork.TakeWriteAnswerAsync(cancellationToken);
                if (answer is not null) request.Headers.Add(ProofRoutes.SolutionHeader, answer);

                HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Log($"Uploading {content.Length} bytes failed.\n{error}", LogLevel.Error);
                return null;
            }
        }

        public async Task<byte[]?> DownloadAsync(string blobId, CancellationToken cancellationToken = default)
        {
            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(BlobRoutes.Blob(blobId), cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Log($"Downloading blob '{blobId}' failed.\n{error}", LogLevel.Error);
                return null;
            }
        }

        public async Task<byte[]?> ConsumeAsync(string blobId, CancellationToken cancellationToken = default)
        {
            try
            {
                HttpResponseMessage response = await httpClient.PostAsync(BlobRoutes.ConsumeBlob(blobId), content: null, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Log($"Consuming blob '{blobId}' failed.\n{error}", LogLevel.Error);
                return null;
            }
        }

        public async Task DeleteAsync(string blobId, CancellationToken cancellationToken = default)
        {
            try
            {
                HttpResponseMessage response = await httpClient.DeleteAsync(BlobRoutes.Blob(blobId), cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Log($"Deleting blob '{blobId}' failed.\n{error}", LogLevel.Error);
            }
        }
    }
}
