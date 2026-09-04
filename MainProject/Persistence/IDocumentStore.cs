namespace ChaySocial.MainProject.Persistence
{
    /// <summary>
    /// Marks a type as something the app stores, and lets the type itself name the collection it lives in. Because
    /// the name comes from the type, a caller can never read a profile out of the posts collection: there is no
    /// place left to type a collection name by hand.
    /// </summary>
    /// <typeparam name="TSelf"> The implementing type itself. </typeparam>
    public interface IStoredDocument<TSelf> where TSelf : IStoredDocument<TSelf>
    {
        /// <summary> Collection this type is stored in. </summary>
        static abstract string CollectionName { get; }
    }

    /// <summary>
    /// A document id that remembers which kind of document it points at, so passing a profile's id where a post's id
    /// is expected does not compile.
    /// </summary>
    /// <typeparam name="TDocument"> Kind of document this id points at. </typeparam>
    /// <param name="Value"> The raw id text as the backend stores it. </param>
    public readonly record struct DocumentId<TDocument>(string Value) where TDocument : IStoredDocument<TDocument>
    {
        public override string ToString() => Value;
    }

    /// <summary> One page of query results plus the cursor that asks for the next page. </summary>
    /// <typeparam name="TDocument"> Kind of document that was queried. </typeparam>
    /// <param name="Documents"> Documents in this page, ordered as the query asked. </param>
    /// <param name="NextCursor"> Value for the next <see cref="DocumentQuery{TDocument}.Cursor"/>, or null when this was the last page. </param>
    public readonly record struct DocumentPage<TDocument>(IReadOnlyList<TDocument> Documents, string? NextCursor)
        where TDocument : IStoredDocument<TDocument>;

    /// <summary>
    /// Reads and writes documents. This is the seam that decides nothing about where data lives: an in-memory store,
    /// a folder on this machine, Firestore, or a custom server all implement it, so moving between them is a change
    /// in one registration and nowhere else.
    /// </summary>
    public interface IDocumentStore
    {
        /// <summary> Fetches one document. </summary>
        /// <typeparam name="TDocument"> Kind of document to read. </typeparam>
        /// <param name="id"> Id of the document. </param>
        /// <param name="cancellationToken"> Cancels the read. </param>
        /// <returns> The document, or null when nothing is stored under that id. </returns>
        Task<TDocument?> ReadAsync<TDocument>(DocumentId<TDocument> id, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>;

        /// <summary> Stores a document, replacing whatever was under that id. </summary>
        /// <typeparam name="TDocument"> Kind of document to write. </typeparam>
        /// <param name="id"> Id to store it under. </param>
        /// <param name="document"> The document. </param>
        /// <param name="cancellationToken"> Cancels the write. </param>
        Task WriteAsync<TDocument>(DocumentId<TDocument> id, TDocument document, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>;

        /// <summary> Removes a document. Removing an id that is not there is not an error. </summary>
        /// <typeparam name="TDocument"> Kind of document to delete. </typeparam>
        /// <param name="id"> Id to remove. </param>
        /// <param name="cancellationToken"> Cancels the delete. </param>
        Task DeleteAsync<TDocument>(DocumentId<TDocument> id, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>;

        /// <summary> Reads one page of the documents matching a query. </summary>
        /// <typeparam name="TDocument"> Kind of document to query. </typeparam>
        /// <param name="query"> Which documents to match, in what order, and how many. </param>
        /// <param name="cancellationToken"> Cancels the query. </param>
        /// <returns> The matching page. </returns>
        Task<DocumentPage<TDocument>> QueryAsync<TDocument>(DocumentQuery<TDocument> query, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>;
    }
}
