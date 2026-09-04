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

        /// <summary> Longest post accepted. </summary>
        public const int MaximumTextLength = 500;

        /// <summary> Id this post is stored under. </summary>
        public DocumentId<PostData> Id => new(PostId);

        /// <summary> Publication time, for sorting a wall newest-first. </summary>
        public static readonly DocumentField<PostData> CreatedAtField = new(nameof(CreatedAtUnixMs), post => post.CreatedAtUnixMs);

        /// <summary> Author address, for reading one account's own posts. </summary>
        public static readonly DocumentField<PostData> AuthorField = new(nameof(AuthorAddress), post => post.AuthorAddress);

        /// <summary> Topic, for the category filter that will exist later. </summary>
        public static readonly DocumentField<PostData> TopicField = new(nameof(Topic), post => post.Topic);
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
}
