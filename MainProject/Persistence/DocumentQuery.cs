namespace ChaySocial.MainProject.Persistence
{
    /// <summary>
    /// One queryable field of a stored type, declared once on that type. It carries both the field's stored name
    /// (what a real backend filters on) and a way to read it from an instance (what an in-process store filters on),
    /// so the same query definition works against either without reflection and without a hand-typed field name.
    /// </summary>
    /// <typeparam name="TDocument"> Type the field belongs to. </typeparam>
    /// <param name="Name"> Field name as the backend stores it. Declare it with <c>nameof</c> so renaming follows. </param>
    /// <param name="Read"> Reads the field out of an instance. </param>
    public sealed record DocumentField<TDocument>(string Name, Func<TDocument, IComparable?> Read)
        where TDocument : IStoredDocument<TDocument>;

    /// <summary>
    /// A read over one type's collection: which documents to match, in what order, and how many. Built by chaining
    /// the <c>With…</c> methods, each of which returns a new query rather than changing this one.
    /// </summary>
    /// <typeparam name="TDocument"> Type being queried. </typeparam>
    public sealed record DocumentQuery<TDocument> where TDocument : IStoredDocument<TDocument>
    {
        /// <summary> Page size used when a caller does not set one. </summary>
        public const int DefaultLimit = 50;

        /// <summary> Fields that must equal the paired value; all of them, combined with AND. </summary>
        public IReadOnlyList<(DocumentField<TDocument> Field, IComparable? Value)> Matches { get; init; } = [];

        /// <summary> Field the results are sorted by, or null to leave the backend's natural order. </summary>
        public DocumentField<TDocument>? SortField { get; init; }

        /// <summary> True sorts from the highest value down — how a "newest first" wall reads. </summary>
        public bool SortDescending { get; init; }

        /// <summary> Largest number of documents one call may return. </summary>
        public int Limit { get; init; } = DefaultLimit;

        /// <summary> Marker from a previous page's <see cref="DocumentPage{TDocument}.NextCursor"/>, or null to start at the first page. </summary>
        public string? Cursor { get; init; }

        /// <summary> Narrows the query to documents whose field equals a value. </summary>
        /// <param name="field"> Field to test, from the document type's own field declarations. </param>
        /// <param name="value"> Value it has to equal. </param>
        /// <returns> A new query with the condition added. </returns>
        public DocumentQuery<TDocument> WithMatch(DocumentField<TDocument> field, IComparable? value)
            => this with { Matches = [.. Matches, (field, value)] };

        /// <summary> Sorts the results by a field. </summary>
        /// <param name="field"> Field to sort by. </param>
        /// <param name="descending"> True for highest first. </param>
        /// <returns> A new query with the sort applied. </returns>
        public DocumentQuery<TDocument> WithSort(DocumentField<TDocument> field, bool descending = false)
            => this with { SortField = field, SortDescending = descending };

        /// <summary> Limits how many documents one page returns. </summary>
        /// <param name="limit"> Maximum documents per page. </param>
        /// <returns> A new query with the limit applied. </returns>
        public DocumentQuery<TDocument> WithLimit(int limit) => this with { Limit = limit };

        /// <summary> Continues from where a previous page stopped. </summary>
        /// <param name="cursor"> Cursor from the previous page. </param>
        /// <returns> A new query starting at that cursor. </returns>
        public DocumentQuery<TDocument> WithCursor(string? cursor) => this with { Cursor = cursor };
    }
}
