using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// One post on the wall. The author signs it before it leaves their device, so the server can store and serve
    /// posts but cannot write one in someone else's name — and any reader can check that for themselves.
    /// </summary>
    public sealed record PostData : IStoredDocument<PostData>
    {
        public static string CollectionName => "posts";

        /// <summary> Id this post is stored under. </summary>
        public required string PostId { get; init; }

        /// <summary> Address of the account that wrote it. </summary>
        public required string AuthorAddress { get; init; }

        /// <summary> What was written. </summary>
        public required string Text { get; init; }

        /// <summary> When the author published it. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Base64 signature over the post's own fields, produced by the author's signing key. </summary>
        public required string Signature { get; init; }

        /// <summary>
        /// Topic this post belongs to, empty while the app has no categories. Kept on the record from the start so
        /// adding categories later does not have to migrate every stored post.
        /// </summary>
        public string Topic { get; init; } = string.Empty;

        /// <summary>
        /// Pictures, recordings and video hanging off this post. Each carries the key its bytes were encrypted
        /// with, so anybody who can read the post can open its media and the server can open neither.
        /// </summary>
        public IReadOnlyList<MediaAttachment> Attachments { get; init; } = [];

        /// <summary>
        /// Post this one quotes, or empty when it quotes nothing. Only the id is stored: the quoted post is read
        /// fresh when this one is drawn, so a quote follows its original — including when the original is deleted,
        /// which a reader should see rather than have hidden from them by a stale copy.
        /// </summary>
        public string QuotedPostId { get; init; } = string.Empty;

        /// <summary> True when this post carries somebody else's post inside it. </summary>
        public bool IsQuoting => QuotedPostId.Length > 0;

        /// <summary>
        /// Group this post was written in, or empty when it was written on the wall. A post belongs to one place
        /// only: a group post stays inside its group rather than also appearing in the feeds, which is what makes
        /// a group somewhere to speak rather than a second name for a hashtag.
        /// </summary>
        public string GroupAddress { get; init; } = string.Empty;

        /// <summary> True when this post was written inside a group. </summary>
        public bool IsInGroup => GroupAddress.Length > 0;

        /// <summary>
        /// The answers this post offers, or empty when it is not asking anything. The question itself is the post's
        /// own text, so a poll is a post that happens to carry choices rather than a separate kind of thing with a
        /// separate screen, a separate feed and a separate set of rules.
        /// </summary>
        public IReadOnlyList<string> PollChoices { get; init; } = [];

        /// <summary> When the asking closes, or zero when it stays open. </summary>
        public long PollClosesAtUnixMs { get; init; }

        /// <summary> True when this post is asking something. </summary>
        public bool IsAsking => PollChoices.Count > 0;

        /// <summary> Longest post accepted. </summary>
        public const int MaximumTextLength = 500;

        /// <summary> Most answers a question may offer; past this the row of choices stops being readable. </summary>
        public const int MaximumPollChoiceCount = 4;

        /// <summary> Longest one answer may be. An answer is a label, not a second post. </summary>
        public const int MaximumPollChoiceLength = 60;

        /// <summary> Fewest answers a question needs before it is a question at all. </summary>
        public const int LeastPollChoiceCount = 2;

        /// <summary> Id this post is stored under. </summary>
        public DocumentId<PostData> Id => new(PostId);

        /// <summary> Publication time, for sorting a wall newest-first. </summary>
        public static readonly DocumentField<PostData> CreatedAtField = new(nameof(CreatedAtUnixMs), post => post.CreatedAtUnixMs);

        /// <summary> Author address, for reading one account's own posts. </summary>
        public static readonly DocumentField<PostData> AuthorField = new(nameof(AuthorAddress), post => post.AuthorAddress);

        /// <summary> Topic, for the category filter that will exist later. </summary>
        public static readonly DocumentField<PostData> TopicField = new(nameof(Topic), post => post.Topic);

        /// <summary> Group address, for reading one group's wall and for keeping group posts out of the feeds. </summary>
        public static readonly DocumentField<PostData> GroupField = new(nameof(GroupAddress), post => post.GroupAddress);
    }

    /// <summary>
    /// One account's like on one post. Stored as its own document keyed by post and liker, so liking twice overwrites
    /// rather than counts twice, and unliking is a delete.
    /// </summary>
    public sealed record LikeData : IStoredDocument<LikeData>
    {
        public static string CollectionName => "likes";

        /// <summary> Post that was liked. </summary>
        public required string PostId { get; init; }

        /// <summary> Address of the account that liked it. </summary>
        public required string LikerAddress { get; init; }

        /// <summary> When the like was recorded. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Id this like is stored under. </summary>
        public DocumentId<LikeData> Id => IdFor(PostId, LikerAddress);

        /// <summary> Builds the id one account's like on one post is stored under. </summary>
        /// <param name="postId"> Post being liked. </param>
        /// <param name="likerAddress"> Account doing the liking. </param>
        /// <returns> The document id. </returns>
        public static DocumentId<LikeData> IdFor(string postId, string likerAddress) => new($"{postId}:{likerAddress}");

        /// <summary> Post id, for counting the likes on one post. </summary>
        public static readonly DocumentField<LikeData> PostField = new(nameof(PostId), like => like.PostId);

        /// <summary> Liker address, for finding what one account liked. </summary>
        public static readonly DocumentField<LikeData> LikerField = new(nameof(LikerAddress), like => like.LikerAddress);
    }

    /// <summary>
    /// One account's answer to one question. Keyed by post and voter, so answering twice overwrites and changing
    /// your mind is a single write rather than a second vote.
    /// </summary>
    /// <remarks>
    /// Signed, unlike a like. A tally is a claim about what a group of people said, and the whole point of asking
    /// here rather than anywhere else is that a reader recomputes that claim on their own machine instead of being
    /// handed a number by whoever runs the server. An unsigned vote would make the number worth exactly as much as
    /// the server's word.
    /// </remarks>
    public sealed record PollVoteData : IStoredDocument<PollVoteData>
    {
        public static string CollectionName => "pollvotes";

        /// <summary> Post that was being asked. </summary>
        public required string PostId { get; init; }

        /// <summary> Address of the account that answered. </summary>
        public required string VoterAddress { get; init; }

        /// <summary> Which of the post's choices was picked, counted from zero. </summary>
        public required int ChoiceIndex { get; init; }

        /// <summary> When the answer was given. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Base64 signature over this answer, produced by the voter's signing key. </summary>
        public required string Signature { get; init; }

        /// <summary> Id this answer is stored under. </summary>
        public DocumentId<PollVoteData> Id => IdFor(PostId, VoterAddress);

        /// <summary> Builds the id one account's answer to one question is stored under. </summary>
        /// <param name="postId"> Post being answered. </param>
        /// <param name="voterAddress"> Account doing the answering. </param>
        /// <returns> The document id. </returns>
        public static DocumentId<PollVoteData> IdFor(string postId, string voterAddress) => new($"{postId}:{voterAddress}");

        /// <summary> Post id, for reading every answer to one question in a single query. </summary>
        public static readonly DocumentField<PollVoteData> PostField = new(nameof(PostId), vote => vote.PostId);

        /// <summary> Voter address, for finding what one account answered. </summary>
        public static readonly DocumentField<PollVoteData> VoterField = new(nameof(VoterAddress), vote => vote.VoterAddress);
    }

    /// <summary>
    /// One account carrying somebody else's post onto its own wall. Nothing of the original is copied — only a
    /// pointer to it — so a repost follows the post it names, including when that post is edited away or deleted.
    /// Keyed by post and reposter, so reposting twice is the same document and taking it back is a delete.
    /// </summary>
    /// <remarks>
    /// It is signed, unlike a like: a repost puts the original in front of the reposter's own followers under the
    /// reposter's name, and that is a published act somebody could otherwise invent on their behalf.
    /// </remarks>
    public sealed record RepostData : IStoredDocument<RepostData>
    {
        public static string CollectionName => "reposts";

        /// <summary> Post that was carried over. </summary>
        public required string PostId { get; init; }

        /// <summary> Address of the account that carried it. </summary>
        public required string ReposterAddress { get; init; }

        /// <summary> When it was carried over; this, not the original's time, is where it lands in a feed. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Base64 signature over this record's own fields, produced by the reposter's signing key. </summary>
        public required string Signature { get; init; }

        /// <summary> Id this repost is stored under. </summary>
        public DocumentId<RepostData> Id => IdFor(PostId, ReposterAddress);

        /// <summary> Builds the id one account's repost of one post is stored under. </summary>
        /// <param name="postId"> Post being carried over. </param>
        /// <param name="reposterAddress"> Account carrying it. </param>
        /// <returns> The document id. </returns>
        public static DocumentId<RepostData> IdFor(string postId, string reposterAddress) => new($"{postId}:{reposterAddress}");

        /// <summary> Post id, for counting how often one post was carried over. </summary>
        public static readonly DocumentField<RepostData> PostField = new(nameof(PostId), repost => repost.PostId);

        /// <summary> Reposter address, for reading what one account carried onto its wall. </summary>
        public static readonly DocumentField<RepostData> ReposterField = new(nameof(ReposterAddress), repost => repost.ReposterAddress);

        /// <summary> Time it was carried over, for ordering a wall newest-first. </summary>
        public static readonly DocumentField<RepostData> CreatedAtField = new(nameof(CreatedAtUnixMs), repost => repost.CreatedAtUnixMs);
    }
}
