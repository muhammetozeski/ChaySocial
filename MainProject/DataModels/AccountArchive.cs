namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// Everything one account has written, in one file it owns. The secret already carries the identity to another
    /// machine; this carries the work. Together they turn "an account is a secret you keep" into "an account is a
    /// secret and a file you keep", which is what makes leaving a server — one that vanished, turned hostile, or
    /// started charging — cost nothing but the time to point the app somewhere else.
    /// </summary>
    /// <remarks>
    /// Nothing in here is anything the server did not already hold. Private messages travel in exactly the sealed
    /// form they were stored in, so a stolen archive reveals no more than a stolen database would, and the account's
    /// secret is deliberately absent: that is the one thing a person carries themselves.
    /// </remarks>
    public sealed record AccountArchive
    {
        /// <summary> The account this archive belongs to. </summary>
        public string Address { get; init; } = string.Empty;

        /// <summary> When it was sealed, so two archives of the same account can be told apart. </summary>
        public long SealedAtUnixMs { get; init; }

        /// <summary> The name, picture and payment address this account publishes, or null when it never published one. </summary>
        public ProfileData? Profile { get; init; }

        /// <summary> Posts this account wrote. </summary>
        public IReadOnlyList<PostData> Posts { get; init; } = [];

        /// <summary> Comments and replies this account wrote. </summary>
        public IReadOnlyList<CommentData> Comments { get; init; } = [];

        /// <summary> Posts this account passed on. </summary>
        public IReadOnlyList<RepostData> Reposts { get; init; } = [];

        /// <summary> Accounts this account follows. </summary>
        public IReadOnlyList<FollowData> Follows { get; init; } = [];

        /// <summary> Posts this account poured a chay for. </summary>
        public IReadOnlyList<LikeData> Likes { get; init; } = [];

        /// <summary> Private messages this account sent or received, in the sealed form the server holds. </summary>
        public IReadOnlyList<MessageData> Messages { get; init; } = [];

        /// <summary>
        /// Signature over this archive's address, its sealing time and the exact set of documents in it, so a file
        /// can prove which account assembled it and that nothing was slipped in or taken out afterwards.
        /// </summary>
        public string Signature { get; init; } = string.Empty;

        /// <summary> How many documents the archive carries, for telling somebody what they are about to import. </summary>
        public int DocumentCount =>
            Posts.Count + Comments.Count + Reposts.Count + Follows.Count + Likes.Count + Messages.Count
            + (Profile is null ? 0 : 1);
    }
}
