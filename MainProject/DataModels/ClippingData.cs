using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// One thing somebody kept, sealed to themselves. What is in the clear is only that an account kept
    /// something; which post it was, and whatever they wrote about it, are inside the seal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Liking is public and keeping is not. A shelf is a record of what somebody found worth coming back to, and
    /// that is a far better description of a person than a list of what they applauded — so the server is left
    /// able to say "this address keeps things" and nothing further.
    /// </para>
    /// <para>
    /// The id is drawn at random rather than built out of the keeper and the post, the way a subject follow is.
    /// An id of that shape would spell out who kept what in the one field a document store cannot hide. The cost
    /// is that the id no longer prevents keeping the same post twice, which the service handles by looking first.
    /// </para>
    /// </remarks>
    public sealed record ClippingData : IStoredDocument<ClippingData>
    {
        public static string CollectionName => "clippings";

        /// <summary> Id this clipping is stored under, drawn at random so it says nothing about what is inside. </summary>
        public required string ClippingId { get; init; }

        /// <summary> Address of the account that kept it — the only thing here that names anybody. </summary>
        public required string KeeperAddress { get; init; }

        /// <summary> Base64 key encapsulation, sealing the body to the keeper's own key. </summary>
        public required string Encapsulation { get; init; }

        /// <summary> Base64 nonce for the cipher. </summary>
        public required string Nonce { get; init; }

        /// <summary> Base64 sealed body: the post's id and the keeper's note. </summary>
        public required string Ciphertext { get; init; }

        /// <summary>
        /// When it was kept, rounded to a bucket. The exact moment is inside the seal, because a timestamp in the
        /// clear is an hour-by-hour record of when somebody reads their feed.
        /// </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Longest note accepted, so a shelf entry stays a note rather than becoming a post. </summary>
        public const int MaximumNoteLength = 280;

        /// <summary> Bytes of randomness in a clipping id, matching what a message id is drawn from. </summary>
        public const int ClippingIdBytes = 12;

        /// <summary> Id this clipping is stored under. </summary>
        public DocumentId<ClippingData> Id => new(ClippingId);

        /// <summary> Keeper address, for reading one account's whole shelf. </summary>
        public static readonly DocumentField<ClippingData> KeeperField = new(nameof(KeeperAddress), clipping => clipping.KeeperAddress);

        /// <summary> When it was kept, for listing a shelf newest first. </summary>
        public static readonly DocumentField<ClippingData> CreatedAtField = new(nameof(CreatedAtUnixMs), clipping => clipping.CreatedAtUnixMs);
    }
}
