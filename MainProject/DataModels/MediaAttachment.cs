namespace ChaySocial.MainProject.DataModels
{
    /// <summary> What kind of media an attachment carries, which decides how it is drawn. </summary>
    public enum MediaKind
    {
        /// <summary> A still picture. </summary>
        Image,

        /// <summary> A recording, drawn as a player with a waveform. </summary>
        Audio,

        /// <summary> A video, drawn as a player with a poster frame. </summary>
        Video
    }

    /// <summary>
    /// A piece of media hanging off a post or a message. The bytes live in the blob store and are encrypted before
    /// they leave the device; everything needed to open them again travels here, inside whatever document already
    /// protects it. The server therefore holds the media and the pointer to it, and can read neither.
    /// </summary>
    /// <param name="BlobId"> Id the encrypted bytes are stored under. </param>
    /// <param name="Kind"> How the attachment should be drawn. </param>
    /// <param name="ContentType"> The real media type, kept here rather than on the upload so the server never learns it. </param>
    /// <param name="Key"> Base64 key the bytes were encrypted with. </param>
    /// <param name="Nonce"> Base64 nonce the bytes were encrypted with. </param>
    /// <param name="ByteCount"> Size of the original media, for showing progress and refusing oversize downloads. </param>
    /// <param name="Description"> Short text describing the media for anyone who cannot see or hear it. </param>
    /// <param name="DurationSeconds"> Playing time for audio and video; zero for a still picture. </param>
    public readonly record struct MediaAttachment(
        string BlobId,
        MediaKind Kind,
        string ContentType,
        string Key,
        string Nonce,
        long ByteCount,
        string Description = "",
        int DurationSeconds = 0)
    {
        /// <summary> Longest description accepted. </summary>
        public const int MaximumDescriptionLength = 200;

        /// <summary> Largest media accepted, matching what the blob route will carry once encryption overhead is added. </summary>
        public const long MaximumByteCount = 10 * 1024 * 1024;

        /// <summary> Attachments one post may carry. </summary>
        public const int MaximumPerPost = 4;
    }
}
