using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// One reader's standing interest in a subject. Kept exactly like following a person — a row keyed by who and
    /// what — so the feed can gather posts by subject the same way it gathers them by author, and following
    /// something twice is one document rather than two.
    /// </summary>
    /// <remarks>
    /// Unsigned, like a like and unlike a repost: this says nothing in anybody's name and publishes nothing. It is
    /// a reader telling their own client what to fetch, which is why nobody is notified when a subject gains a
    /// follower — a subject is not an account and there is nobody there to tell.
    /// </remarks>
    public sealed record SubjectFollowData : IStoredDocument<SubjectFollowData>
    {
        public static string CollectionName => "subjectfollows";

        /// <summary> Address of the account following the subject. </summary>
        public required string FollowerAddress { get; init; }

        /// <summary> The subject being followed, in the form subjects are stored under. </summary>
        public required string Subject { get; init; }

        /// <summary> When the interest was declared. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Id this interest is stored under. </summary>
        public DocumentId<SubjectFollowData> Id => IdFor(FollowerAddress, Subject);

        /// <summary> Builds the id one account's interest in one subject is stored under. </summary>
        /// <param name="followerAddress"> Account doing the following. </param>
        /// <param name="subject"> Subject being followed, already normalised. </param>
        /// <returns> The document id. </returns>
        public static DocumentId<SubjectFollowData> IdFor(string followerAddress, string subject)
            => new($"{followerAddress}:{subject}");

        /// <summary> Follower address, for reading everything one account follows. </summary>
        public static readonly DocumentField<SubjectFollowData> FollowerField = new(nameof(FollowerAddress), follow => follow.FollowerAddress);

        /// <summary> Subject, for counting who follows one subject. </summary>
        public static readonly DocumentField<SubjectFollowData> SubjectField = new(nameof(Subject), follow => follow.Subject);

        /// <summary> When it was declared, for listing a reader's subjects newest first. </summary>
        public static readonly DocumentField<SubjectFollowData> CreatedAtField = new(nameof(CreatedAtUnixMs), follow => follow.CreatedAtUnixMs);
    }
}
