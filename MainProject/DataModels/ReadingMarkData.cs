using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// One reader's whole book of reading marks, sealed to themselves. What is in the clear is only that an account
    /// reads things; which conversations, and how far into each, are inside the seal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Where somebody stopped reading is a better map of what they care about than anything they published, so the
    /// server is left able to say "this address reads" and nothing further.
    /// </para>
    /// <para>
    /// One document per reader, written over on every return, and its id is the reader's address. That is not the
    /// leak <see cref="ClippingData"/> avoids with a random id: the address is already a field in the clear here,
    /// so the id repeats what the document says rather than adding to it — and it buys a plain read by id instead
    /// of a query.
    /// </para>
    /// </remarks>
    public sealed record ReadingMarkData : IStoredDocument<ReadingMarkData>
    {
        public static string CollectionName => "readingmarks";

        /// <summary> Address of the account whose marks these are, and the id they are stored under. </summary>
        public required string ReaderAddress { get; init; }

        /// <summary> Base64 key encapsulation, sealing the book to the reader's own key. </summary>
        public required string Encapsulation { get; init; }

        /// <summary> Base64 nonce for the cipher. </summary>
        public required string Nonce { get; init; }

        /// <summary> Base64 sealed body: every mark, each naming a post and how far into it was read. </summary>
        public required string Ciphertext { get; init; }

        /// <summary>
        /// When the book was last written, rounded to a bucket. The exact moment is not kept at all: a timestamp
        /// in the clear is an hour-by-hour record of when somebody reads.
        /// </summary>
        public required long UpdatedAtUnixMs { get; init; }

        /// <summary>
        /// How many marks a reader's book holds. Past this the oldest is dropped, so the book stays a book rather
        /// than a lifetime's reading history.
        /// </summary>
        public const int MarksKeptPerReader = 200;

        /// <summary> Id this book is stored under, which is the reader's own address. </summary>
        public DocumentId<ReadingMarkData> Id => new(ReaderAddress);
    }
}
