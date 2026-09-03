namespace Groundwork.Persistence
{
    /// <summary> How a <see cref="DocumentFilter"/> compares a stored field against the value it carries. </summary>
    public enum DocumentComparison
    {
        Equal,
        NotEqual,
        LessThan,
        LessThanOrEqual,
        GreaterThan,
        GreaterThanOrEqual,

        /// <summary> The stored field is an array and contains the filter value. </summary>
        ArrayContains
    }

    /// <summary>
    /// One condition a queried document has to satisfy. Deliberately expressed as data rather than a lambda, so the
    /// same query can be answered by an in-memory store, translated into a Firestore query, or serialized into a
    /// request to a custom server without rewriting the call site.
    /// </summary>
    /// <param name="Field"> Name of the stored field to test. </param>
    /// <param name="Comparison"> How the field is compared to <paramref name="Value"/>. </param>
    /// <param name="Value"> The value the field is compared against. </param>
    public readonly record struct DocumentFilter(string Field, DocumentComparison Comparison, object? Value);

    /// <summary>
    /// A read over one collection: which documents to match, in what order, and how many at a time. Every backend
    /// receives this same shape, which is what keeps <see cref="IDocumentStore"/> implementations interchangeable.
    /// </summary>
    /// <param name="Collection"> Collection to read from. </param>
    public sealed record DocumentQuery(string Collection)
    {
        /// <summary> Conditions a document must satisfy; all of them, combined with AND. Empty matches the whole collection. </summary>
        public IReadOnlyList<DocumentFilter> Filters { get; init; } = [];

        /// <summary> Field the results are sorted by, or null to let the backend use its natural order. </summary>
        public string? OrderByField { get; init; }

        /// <summary> True sorts from the highest value down — the usual choice for "newest first" feeds. </summary>
        public bool Descending { get; init; }

        /// <summary> Largest number of documents a single call may return. </summary>
        public int Limit { get; init; } = DefaultLimit;

        /// <summary> Opaque marker from a previous page's <see cref="DocumentPage{TDocument}.NextCursor"/>, or null to start at the first page. </summary>
        public string? Cursor { get; init; }

        /// <summary> Page size used when a caller does not set <see cref="Limit"/>. </summary>
        public const int DefaultLimit = 50;
    }
}

