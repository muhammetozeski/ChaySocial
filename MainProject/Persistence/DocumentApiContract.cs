namespace ChaySocial.MainProject.Persistence
{
    /// <summary>
    /// The wire shape of a query, shared by the client that sends it and the server that answers it. Only field
    /// <em>names</em> travel — the server never receives the document type, and does not need to know what a post or
    /// a profile is to sort and page one.
    /// </summary>
    /// <param name="Matches"> Field name and the value it must equal. </param>
    /// <param name="SortField"> Field name to sort by, or null for the stored order. </param>
    /// <param name="SortDescending"> True for highest first. </param>
    /// <param name="Limit"> Maximum documents in the answer. </param>
    /// <param name="Cursor"> Where to continue from, or null to start at the first page. </param>
    public sealed record DocumentQueryRequest(
        IReadOnlyList<DocumentMatchRequest> Matches,
        string? SortField,
        bool SortDescending,
        int Limit,
        string? Cursor);

    /// <summary> One equality condition inside a <see cref="DocumentQueryRequest"/>. </summary>
    /// <param name="Field"> Stored field name. </param>
    /// <param name="Value"> Value the field must equal. </param>
    public sealed record DocumentMatchRequest(string Field, object? Value);

    /// <summary> The wire shape of a query answer. </summary>
    /// <typeparam name="TDocument"> Type the documents deserialize into on the client. </typeparam>
    /// <param name="Documents"> Matching documents, already ordered. </param>
    /// <param name="NextCursor"> Cursor for the next page, or null when this was the last one. </param>
    public sealed record DocumentQueryResponse<TDocument>(IReadOnlyList<TDocument> Documents, string? NextCursor);

    /// <summary> Route shapes both the client and the server build their URLs from, so the two can never drift apart. </summary>
    public static class DocumentRoutes
    {
        /// <summary> Prefix every document route sits under. </summary>
        public const string Base = "api/documents";

        /// <summary> Route of a single document. </summary>
        /// <param name="collection"> Collection the document lives in. </param>
        /// <param name="documentId"> Id inside that collection. </param>
        /// <returns> The relative URL. </returns>
        public static string Document(string collection, string documentId)
            => $"{Base}/{Uri.EscapeDataString(collection)}/{Uri.EscapeDataString(documentId)}";

        /// <summary> Route that answers queries over a collection. </summary>
        /// <param name="collection"> Collection to query. </param>
        /// <returns> The relative URL. </returns>
        public static string Query(string collection) => $"{Base}/{Uri.EscapeDataString(collection)}/query";
    }
}
