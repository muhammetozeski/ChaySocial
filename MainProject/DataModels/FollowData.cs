using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// One account's follow of another. Stored as its own document keyed by the pair, so following twice overwrites
    /// rather than counts twice, unfollowing is a delete, and the two directions of a mutual follow stay separate
    /// documents that either side can remove on its own.
    /// </summary>
    public sealed record FollowData : IStoredDocument<FollowData>
    {
        public static string CollectionName => "follows";

        /// <summary> Address of the account doing the following. </summary>
        public required string FollowerAddress { get; init; }

        /// <summary> Address of the account being followed. </summary>
        public required string FolloweeAddress { get; init; }

        /// <summary> When the follow was recorded. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Character placed between the two addresses that make up the id. </summary>
        const string IdSeparator = ":";

        /// <summary> Id this follow is stored under. </summary>
        public DocumentId<FollowData> Id => IdFor(FollowerAddress, FolloweeAddress);

        /// <summary> Builds the id one account's follow of another is stored under. </summary>
        /// <param name="followerAddress"> Account doing the following. </param>
        /// <param name="followeeAddress"> Account being followed. </param>
        /// <returns> The document id, so a caller can check or delete a follow without reading it first. </returns>
        public static DocumentId<FollowData> IdFor(string followerAddress, string followeeAddress)
            => new($"{followerAddress}{IdSeparator}{followeeAddress}");

        /// <summary> Follower address, for listing everyone one account follows. </summary>
        public static readonly DocumentField<FollowData> FollowerField = new(nameof(FollowerAddress), follow => follow.FollowerAddress);

        /// <summary> Followee address, for listing an account's followers. </summary>
        public static readonly DocumentField<FollowData> FolloweeField = new(nameof(FolloweeAddress), follow => follow.FolloweeAddress);

        /// <summary> Follow time, for showing the newest followers first. </summary>
        public static readonly DocumentField<FollowData> CreatedAtField = new(nameof(CreatedAtUnixMs), follow => follow.CreatedAtUnixMs);
    }
}
