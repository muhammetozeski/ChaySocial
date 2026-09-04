using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Puts media on the server without giving the server the media. A file is encrypted on the device with a key
    /// drawn for that one file, the ciphertext is uploaded, and the key travels inside the post or message instead
    /// — so whoever can read the text can see the picture, and nobody else can, including whoever holds the disk.
    /// </summary>
    public static class MediaService
    {
        /// <summary> Media types accepted for each kind, so a file the app cannot draw is refused before it is uploaded. </summary>
        static readonly Dictionary<MediaKind, string[]> AcceptedContentTypes = new()
        {
            [MediaKind.Image] = ["image/png", "image/jpeg", "image/webp", "image/gif", "image/avif"],
            [MediaKind.Audio] = ["audio/webm", "audio/ogg", "audio/mpeg", "audio/wav", "audio/mp4"],
            [MediaKind.Video] = ["video/webm", "video/mp4", "video/ogg"]
        };

        /// <summary>
        /// Encrypts a file and uploads it.
        /// </summary>
        /// <param name="content"> The file's bytes, as read from the picker. </param>
        /// <param name="contentType"> Media type the picker reported. </param>
        /// <param name="description"> Short text describing the media for anyone who cannot see or hear it. </param>
        /// <param name="durationSeconds"> Playing time for audio and video; zero for a still picture. </param>
        /// <param name="cancellationToken"> Cancels the upload. </param>
        /// <returns> The attachment to hang on a post or message, or null when the file was refused or the upload failed. </returns>
        public static async Task<MediaAttachment?> UploadAsync(
            ReadOnlyMemory<byte> content,
            string contentType,
            string description = "",
            int durationSeconds = 0,
            CancellationToken cancellationToken = default)
        {
            if (AppServices.Blobs is null) return null;
            if (content.Length == 0 || content.Length > MediaAttachment.MaximumByteCount) return null;

            if (!TryClassify(contentType, out MediaKind kind))
            {
                Log($"Refusing to upload media of type '{contentType}'; the app cannot draw it.", LogLevel.Warning);
                return null;
            }

            byte[] key = RandomSource.Next(AppCryptography.Cipher.KeySize);
            byte[] nonce = RandomSource.Next(AppCryptography.Cipher.NonceSize);
            byte[] ciphertext = AppCryptography.Cipher.Encrypt(content.Span, key, nonce, []);

            string? blobId = await AppServices.Blobs.UploadAsync(ciphertext, cancellationToken);
            if (blobId is null) return null;

            return new MediaAttachment(
                blobId,
                kind,
                contentType,
                Convert.ToBase64String(key),
                Convert.ToBase64String(nonce),
                content.Length,
                Trim(description, MediaAttachment.MaximumDescriptionLength),
                durationSeconds);
        }

        /// <summary>
        /// Fetches an attachment and decrypts it. A failure here is ordinary — the media may have been removed, or
        /// the attachment may have been tampered with — so it comes back as null rather than an exception.
        /// </summary>
        /// <param name="attachment"> The attachment to open. </param>
        /// <param name="cancellationToken"> Cancels the download. </param>
        /// <returns> The original bytes, or null when they could not be fetched or opened. </returns>
        public static async Task<byte[]?> OpenAsync(MediaAttachment attachment, CancellationToken cancellationToken = default)
        {
            if (AppServices.Blobs is null) return null;

            byte[]? ciphertext = await AppServices.Blobs.DownloadAsync(attachment.BlobId, cancellationToken);
            if (ciphertext is null) return null;

            try
            {
                return AppCryptography.Cipher.TryDecrypt(
                    ciphertext,
                    Convert.FromBase64String(attachment.Key),
                    Convert.FromBase64String(attachment.Nonce),
                    [],
                    out byte[] plaintext)
                    ? plaintext
                    : null;
            }
            catch (FormatException error)
            {
                Log($"Attachment '{attachment.BlobId}' carries malformed base64.\n{error}", LogLevel.Warning);
                return null;
            }
        }

        /// <summary> Removes the stored bytes an attachment points at, for when its post is deleted. </summary>
        /// <param name="attachment"> The attachment whose bytes should go. </param>
        /// <param name="cancellationToken"> Cancels the delete. </param>
        public static async Task RemoveAsync(MediaAttachment attachment, CancellationToken cancellationToken = default)
        {
            if (AppServices.Blobs is null) return;

            await AppServices.Blobs.DeleteAsync(attachment.BlobId, cancellationToken);
        }

        /// <summary>
        /// Turns decrypted bytes into something an <c>&lt;img&gt;</c>, <c>&lt;audio&gt;</c> or <c>&lt;video&gt;</c>
        /// element can show. A data URI is used because the alternative — a blob URL — needs JavaScript, which this
        /// project does not ship.
        /// </summary>
        /// <param name="content"> The decrypted media. </param>
        /// <param name="contentType"> The media's real type. </param>
        /// <returns> A <c>data:</c> URI ready to put in a source attribute. </returns>
        public static string BuildDataUri(ReadOnlySpan<byte> content, string contentType)
            => $"data:{contentType};base64,{Convert.ToBase64String(content)}";

        /// <summary> Every media type the picker should offer, joined for an accept attribute. </summary>
        /// <param name="kinds"> Kinds to offer; all of them when none are named. </param>
        /// <returns> A comma-separated accept list. </returns>
        public static string BuildAcceptList(params MediaKind[] kinds)
        {
            IEnumerable<MediaKind> wanted = kinds.Length > 0 ? kinds : AcceptedContentTypes.Keys;
            return string.Join(",", wanted.SelectMany(kind => AcceptedContentTypes[kind]));
        }

        /// <summary> Decides which kind a media type belongs to. </summary>
        /// <param name="contentType"> Media type the picker reported. </param>
        /// <param name="kind"> Receives the kind, or <see cref="MediaKind.Image"/> when the type is not accepted. </param>
        /// <returns> True when the app can draw this type. </returns>
        public static bool TryClassify(string contentType, out MediaKind kind)
        {
            foreach ((MediaKind candidate, string[] types) in AcceptedContentTypes)
            {
                if (!types.Contains(contentType, StringComparer.OrdinalIgnoreCase)) continue;

                kind = candidate;
                return true;
            }

            kind = MediaKind.Image;
            return false;
        }

        /// <summary> Cuts text to a limit without throwing when it is already short enough. </summary>
        /// <param name="text"> Text to cut. </param>
        /// <param name="maximumLength"> Longest result allowed. </param>
        /// <returns> The trimmed text. </returns>
        static string Trim(string text, int maximumLength)
        {
            string trimmed = text.Trim();
            return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
        }
    }
}
