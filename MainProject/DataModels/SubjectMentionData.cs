using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// A note that one post named one subject. It is an index and nothing else — the post already holds the words,
    /// and this only makes "everything written under #tea" a question the store can answer, which it cannot do by
    /// searching text.
    /// </summary>
    /// <remarks>
    /// It carries no signature, and does not need one: a reader who follows a subject reads the posts themselves
    /// and keeps only those whose own text names that subject. A server that invents an entry here therefore gains
    /// nothing — the post it points at will not back the claim up.
    /// </remarks>
    public sealed record SubjectMentionData : IStoredDocument<SubjectMentionData>
    {
        public static string CollectionName => "subjects";

        /// <summary> The subject that was named, in the lowercase form subjects are stored under. </summary>
        public required string Subject { get; init; }

        /// <summary> Post that named it. </summary>
        public required string PostId { get; init; }

        /// <summary> When that post was published; this is what orders a subject's page. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Id this note is stored under. </summary>
        public DocumentId<SubjectMentionData> Id => IdFor(Subject, PostId);

        /// <summary> Builds the id one post's note about one subject is stored under. </summary>
        /// <param name="subject"> Subject named, already lowercased. </param>
        /// <param name="postId"> Post that named it. </param>
        /// <returns> The document id, which is the same for a repeat so naming a subject twice writes one note. </returns>
        public static DocumentId<SubjectMentionData> IdFor(string subject, string postId) => new($"{subject}:{postId}");

        /// <summary> Subject, for reading everything written under one. </summary>
        public static readonly DocumentField<SubjectMentionData> SubjectField = new(nameof(Subject), mention => mention.Subject);

        /// <summary> Post id, for clearing a deleted post's notes. </summary>
        public static readonly DocumentField<SubjectMentionData> PostField = new(nameof(PostId), mention => mention.PostId);

        /// <summary> Publication time, for ordering a subject's page newest first. </summary>
        public static readonly DocumentField<SubjectMentionData> CreatedAtField = new(nameof(CreatedAtUnixMs), mention => mention.CreatedAtUnixMs);
    }
}
