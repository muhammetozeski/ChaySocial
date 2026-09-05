using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary> Why an account or a post was reported. </summary>
    public enum ReportReason
    {
        Spam,
        Harassment,
        Violence,
        SexualContent,
        Impersonation,
        Other
    }

    /// <summary> What a report is about. </summary>
    public enum ReportKind
    {
        /// <summary> A post on a wall. </summary>
        Post,

        /// <summary> An account, rather than any one thing it wrote. </summary>
        Account,

        /// <summary> A reply under a post. </summary>
        Comment,

        /// <summary> A private message, which only its recipient could have disclosed. </summary>
        Message
    }

    /// <summary> Where a report has got to. </summary>
    public enum ReportStatus
    {
        /// <summary> Filed and not yet looked at. </summary>
        Open,

        /// <summary> Looked at and acted on. </summary>
        Upheld,

        /// <summary> Looked at and found to need nothing. </summary>
        Dismissed,

        /// <summary> Taken back by whoever filed it. </summary>
        Withdrawn
    }

    /// <summary>
    /// One account's decision to stop seeing another. Stored under a deterministic id, so blocking twice
    /// overwrites the same row and unblocking is a delete.
    /// </summary>
    public sealed record BlockData : IStoredDocument<BlockData>
    {
        public static string CollectionName => "blocks";

        /// <summary> Account that placed the block. </summary>
        public required string BlockerAddress { get; init; }

        /// <summary> Account that was blocked. </summary>
        public required string BlockedAddress { get; init; }

        /// <summary> When the block was placed. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Id this block is stored under. </summary>
        public DocumentId<BlockData> Id => IdFor(BlockerAddress, BlockedAddress);

        /// <summary> Builds the id one account's block on another is stored under. </summary>
        /// <param name="blockerAddress"> Account placing the block. </param>
        /// <param name="blockedAddress"> Account being blocked. </param>
        /// <returns> The document id. </returns>
        public static DocumentId<BlockData> IdFor(string blockerAddress, string blockedAddress)
            => new($"{blockerAddress}:{blockedAddress}");

        /// <summary> Blocker address, for reading who an account has blocked. </summary>
        public static readonly DocumentField<BlockData> BlockerField = new(nameof(BlockerAddress), block => block.BlockerAddress);

        /// <summary> Blocked address, for reading who has blocked an account. </summary>
        public static readonly DocumentField<BlockData> BlockedField = new(nameof(BlockedAddress), block => block.BlockedAddress);
    }

    /// <summary>
    /// A complaint about a post or an account. This record is also where the app's content policy lives: the
    /// server is not given post content as a matter of course, so <see cref="DisclosedContent"/> — filled in by
    /// the reporter at the moment they report — is the one path by which content reaches it for review.
    /// </summary>
    public sealed record ReportData : IStoredDocument<ReportData>
    {
        public static string CollectionName => "reports";

        /// <summary> Id this report is stored under. </summary>
        public required string ReportId { get; init; }

        /// <summary> Account that filed it. </summary>
        public required string ReporterAddress { get; init; }

        /// <summary> What this report is about, which says which of the target fields below is the one filled in. </summary>
        public ReportKind Kind { get; init; } = ReportKind.Post;

        /// <summary> Post being reported; empty when the report is about an account. </summary>
        public string TargetPostId { get; init; } = string.Empty;

        /// <summary> Account being reported; empty when the report is about a post. </summary>
        public string TargetAddress { get; init; } = string.Empty;

        /// <summary> Comment being reported; empty for every other kind. </summary>
        public string TargetCommentId { get; init; } = string.Empty;

        /// <summary> Message being reported; empty for every other kind. </summary>
        public string TargetMessageId { get; init; } = string.Empty;

        /// <summary> Where this report has got to. </summary>
        public ReportStatus Status { get; init; } = ReportStatus.Open;

        /// <summary> When it stopped being open, or zero while it still is. </summary>
        public long ResolvedAtUnixMs { get; init; }

        /// <summary> Category the reporter chose. </summary>
        public required ReportReason Reason { get; init; }

        /// <summary> What the reporter wrote in their own words. </summary>
        public string Detail { get; init; } = string.Empty;

        /// <summary>
        /// The reported content, handed over by the reporter. Empty for an account report. This is deliberate:
        /// a report is the moment content stops being something only its readers can see.
        /// </summary>
        public string DisclosedContent { get; init; } = string.Empty;

        /// <summary> When it was filed. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Longest free-text detail accepted. </summary>
        public const int MaximumDetailLength = 500;

        /// <summary> Id this report is stored under. </summary>
        public DocumentId<ReportData> Id => new(ReportId);

        /// <summary> Reporter address, for finding what one account reported. </summary>
        public static readonly DocumentField<ReportData> ReporterField = new(nameof(ReporterAddress), report => report.ReporterAddress);

        /// <summary> Reported post, for finding every complaint about one post. </summary>
        public static readonly DocumentField<ReportData> TargetPostField = new(nameof(TargetPostId), report => report.TargetPostId);

        /// <summary> Reported account, for finding every complaint about one account. </summary>
        public static readonly DocumentField<ReportData> TargetAddressField = new(nameof(TargetAddress), report => report.TargetAddress);

        /// <summary> Filing time, for reviewing newest complaints first. </summary>
        public static readonly DocumentField<ReportData> CreatedAtField = new(nameof(CreatedAtUnixMs), report => report.CreatedAtUnixMs);

        /// <summary> Reported comment, for finding every complaint about one reply. </summary>
        public static readonly DocumentField<ReportData> TargetCommentField = new(nameof(TargetCommentId), report => report.TargetCommentId);

        /// <summary> Reported message, for finding every complaint about one message. </summary>
        public static readonly DocumentField<ReportData> TargetMessageField = new(nameof(TargetMessageId), report => report.TargetMessageId);
    }
}
