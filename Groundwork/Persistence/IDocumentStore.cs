using Groundwork.Outcomes;

namespace Groundwork.Persistence
{
    /// <summary> A document together with the id it is stored under, which a query result needs but the document body does not carry. </summary>
    /// <typeparam name="TDocument"> Shape the document was deserialized into. </typeparam>
    /// <param name="Id"> Id the document is stored under inside its collection. </param>
    /// <param name="Value"> The document itself. </param>
    public readonly record struct StoredDocument<TDocument>(string Id, TDocument Value);

    /// <summary> One page of query results plus the cursor that asks for the next page. </summary>
    /// <typeparam name="TDocument"> Shape the documents were deserialized into. </typeparam>
    /// <param name="Documents"> Documents in this page, already ordered as the query asked. </param>
    /// <param name="NextCursor"> Value to put in the next <see cref="DocumentQuery.Cursor"/>, or null when this was the last page. </param>
    public readonly record struct DocumentPage<TDocument>(IReadOnlyList<StoredDocument<TDocument>> Documents, string? NextCursor);

    /// <summary>
    /// Reads and writes documents grouped into named collections. This is the seam that decides nothing about where
    /// the data actually lives: an in-memory store, a folder on the developer's machine, Firestore, or a custom
    /// server all implement this same contract, so swapping backends is a single registration change.
    /// </summary>
    public interface IDocumentStore
    {
        /// <summary> Fetches one document by id. </summary>
        /// <typeparam name="TDocument"> Shape to deserialize into. </typeparam>
        /// <param name="collection"> Collection holding the document. </param>
        /// <param name="documentId"> Id inside that collection. </param>
        /// <param name="cancellationToken"> Cancels the read. </param>
        /// <returns> The document on success; a failure when it does not exist or the backend refused the read. </returns>
        Task<Result<TDocument>> ReadAsync<TDocument>(string collection, string documentId, CancellationToken cancellationToken = default);

        /// <summary> Stores a document, replacing whatever was under that id. </summary>
        /// <typeparam name="TDocument"> Shape being serialized. </typeparam>
        /// <param name="collection"> Collection to write into. </param>
        /// <param name="documentId"> Id inside that collection. </param>
        /// <param name="document"> The document to store. </param>
        /// <param name="cancellationToken"> Cancels the write. </param>
        /// <returns> Success, or the reason the backend refused the write. </returns>
        Task<Result> WriteAsync<TDocument>(string collection, string documentId, TDocument document, CancellationToken cancellationToken = default);

        /// <summary> Removes a document. Deleting an id that is not there succeeds, so callers do not have to check first. </summary>
        /// <param name="collection"> Collection holding the document. </param>
        /// <param name="documentId"> Id inside that collection. </param>
        /// <param name="cancellationToken"> Cancels the delete. </param>
        /// <returns> Success, or the reason the backend refused the delete. </returns>
        Task<Result> DeleteAsync(string collection, string documentId, CancellationToken cancellationToken = default);

        /// <summary> Reads one page of the documents matching a query. </summary>
        /// <typeparam name="TDocument"> Shape to deserialize into. </typeparam>
        /// <param name="query"> Which documents to match, in what order, and how many. </param>
        /// <param name="cancellationToken"> Cancels the query. </param>
        /// <returns> The matching page on success; a failure when the backend refused the query. </returns>
        Task<Result<DocumentPage<TDocument>>> QueryAsync<TDocument>(DocumentQuery query, CancellationToken cancellationToken = default);
    }
}

