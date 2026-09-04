using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// One reply under a post. Like a post, it is signed on the author's device before it is stored, so the server
    /// can serve comments but cannot put words under someone else's address, and any reader can check that.
    /// </summary>
    public sealed record CommentData : IStoredDocument<CommentData>
    {
        public static string CollectionName => "comments";

        /// <summary> Id this comment is stored under. </summary>
        public required string CommentId { get; init; }

        /// <summary> Post this comment replies to. </summary>
        public required string PostId { get; init; }

        /// <summary> Address of the account that wrote it. </summary>
        public required string AuthorAddress { get; init; }

        /// <summary> What was written. </summary>
        public required string Text { get; init; }

        /// <summary>
        /// Comment this one answers, or empty when it answers the post itself. It names the real parent even when
        /// that parent is itself an answer, so who was speaking to whom survives; a thread is still drawn two deep,
        /// because a conversation indented six times is a conversation nobody can read.
        /// </summary>
        public string ParentCommentId { get; init; } = string.Empty;

        /// <summary> True when this comment answers another comment rather than the post. </summary>
        public bool IsReply => ParentCommentId.Length > 0;

        /// <summary> When the author published it. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Base64 signature over the comment's own fields, produced by the author's signing key. </summary>
        public required string Signature { get; init; }

        /// <summary> Longest comment accepted. </summary>
        public const int MaximumTextLength = 300;

        /// <summary> Id this comment is stored under. </summary>
        public DocumentId<CommentData> Id => new(CommentId);

        /// <summary> Post id, for reading the comments under one post. </summary>
        public static readonly DocumentField<CommentData> PostField = new(nameof(PostId), comment => comment.PostId);

        /// <summary> Author address, for reading everything one account has replied. </summary>
        public static readonly DocumentField<CommentData> AuthorField = new(nameof(AuthorAddress), comment => comment.AuthorAddress);

        /// <summary> Publication time, for ordering a thread oldest-first. </summary>
        public static readonly DocumentField<CommentData> CreatedAtField = new(nameof(CreatedAtUnixMs), comment => comment.CreatedAtUnixMs);
    }
}
