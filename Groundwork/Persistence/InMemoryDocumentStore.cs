using System.Text.Json;
using Groundwork.Diagnostics;
using Groundwork.Outcomes;

namespace Groundwork.Persistence
{
    /// <summary>
    /// Keeps documents as JSON in a dictionary for the lifetime of the process. Documents are serialized on write and
    /// deserialized on read exactly as a network backend would do it, and queries run against the serialized fields,
    /// so code developed against this store behaves the same once a real backend replaces it. Everything is lost when
    /// the process exits — that is the point: it is the development and test backend, not a cache in front of another one.
    /// </summary>
    public sealed class InMemoryDocumentStore : IDocumentStore
    {
        const string SourceName = nameof(InMemoryDocumentStore);

        readonly Dictionary<string, Dictionary<string, string>> _collections = [];
        readonly Lock _gate = new();
        readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

        public Task<Result<TDocument>> ReadAsync<TDocument>(string collection, string documentId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? storedJson;
            lock (_gate)
            {
                if (!_collections.TryGetValue(collection, out Dictionary<string, string>? documents) ||
                    !documents.TryGetValue(documentId, out storedJson))
                {
                    return Task.FromResult(Result<TDocument>.Failure($"'{collection}/{documentId}' does not exist."));
                }
            }

            return Task.FromResult(Deserialize<TDocument>(storedJson, $"{collection}/{documentId}"));
        }

        public Task<Result> WriteAsync<TDocument>(string collection, string documentId, TDocument document, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string documentJson = JsonSerializer.Serialize(document, _serializerOptions);

                lock (_gate)
                {
                    if (!_collections.TryGetValue(collection, out Dictionary<string, string>? documents))
                    {
                        documents = [];
                        _collections[collection] = documents;
                    }

                    documents[documentId] = documentJson;
                }

                return Task.FromResult(Result.Success());
            }
            catch (Exception error)
            {
                DiagnosticLog.Write(DiagnosticSeverity.Error, SourceName, $"Serializing '{collection}/{documentId}' failed.", error);
                return Task.FromResult(Result.Failure("The document could not be stored.", error));
            }
        }

        public Task<Result> DeleteAsync(string collection, string documentId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_collections.TryGetValue(collection, out Dictionary<string, string>? documents))
                {
                    documents.Remove(documentId);
                }
            }

            return Task.FromResult(Result.Success());
        }

        public Task<Result<DocumentPage<TDocument>>> QueryAsync<TDocument>(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            KeyValuePair<string, string>[] snapshot;
            lock (_gate)
            {
                snapshot = _collections.TryGetValue(query.Collection, out Dictionary<string, string>? documents)
                    ? [.. documents]
                    : [];
            }

            try
            {
                List<(string Id, string Json, JsonElement Root)> parsed = [];
                foreach ((string id, string json) in snapshot)
                {
                    using JsonDocument parsedDocument = JsonDocument.Parse(json);
                    JsonElement root = parsedDocument.RootElement.Clone();

                    if (query.Filters.All(filter => Matches(root, filter)))
                    {
                        parsed.Add((id, json, root));
                    }
                }

                List<(string Id, string Json, JsonElement Root)> ordered = Order(parsed, query);
                int startIndex = ParseCursor(query.Cursor);
                int pageSize = Math.Max(1, query.Limit);

                List<StoredDocument<TDocument>> page = [];
                foreach ((string id, string json, _) in ordered.Skip(startIndex).Take(pageSize))
                {
                    Result<TDocument> deserialized = Deserialize<TDocument>(json, $"{query.Collection}/{id}");
                    if (deserialized.IsFailure) return Task.FromResult(Result<DocumentPage<TDocument>>.Propagate(deserialized.Outcome));

                    page.Add(new StoredDocument<TDocument>(id, deserialized.Value!));
                }

                int nextIndex = startIndex + page.Count;
                string? nextCursor = nextIndex < ordered.Count ? nextIndex.ToString() : null;

                return Task.FromResult(Result<DocumentPage<TDocument>>.Success(new DocumentPage<TDocument>(page, nextCursor)));
            }
            catch (Exception error)
            {
                DiagnosticLog.Write(DiagnosticSeverity.Error, SourceName, $"Querying '{query.Collection}' failed.", error);
                return Task.FromResult(Result<DocumentPage<TDocument>>.Failure("The query could not be answered.", error));
            }
        }

        /// <summary> Turns stored JSON back into a document, reporting rather than throwing when the stored shape no longer matches the requested type. </summary>
        /// <typeparam name="TDocument"> Shape to deserialize into. </typeparam>
        /// <param name="documentJson"> The stored JSON text. </param>
        /// <param name="path"> <c>collection/id</c> of the document, used in the failure message. </param>
        /// <returns> The document on success; a failure describing the mismatch otherwise. </returns>
        Result<TDocument> Deserialize<TDocument>(string documentJson, string path)
        {
            try
            {
                TDocument? document = JsonSerializer.Deserialize<TDocument>(documentJson, _serializerOptions);
                return document is null
                    ? Result<TDocument>.Failure($"'{path}' deserialized to null.")
                    : Result<TDocument>.Success(document);
            }
            catch (Exception error)
            {
                DiagnosticLog.Write(DiagnosticSeverity.Error, SourceName, $"Deserializing '{path}' failed.", error);
                return Result<TDocument>.Failure("The stored document could not be read.", error);
            }
        }

        /// <summary> Sorts matched documents by <see cref="DocumentQuery.OrderByField"/>, leaving them in insertion order when no field was named. </summary>
        /// <param name="matched"> Documents that passed every filter. </param>
        /// <param name="query"> The query carrying the sort field and direction. </param>
        /// <returns> The documents in the order the query asked for. </returns>
        static List<(string Id, string Json, JsonElement Root)> Order(List<(string Id, string Json, JsonElement Root)> matched, DocumentQuery query)
        {
            if (string.IsNullOrEmpty(query.OrderByField)) return matched;

            matched.Sort((left, right) =>
            {
                int comparison = CompareFields(left.Root, right.Root, query.OrderByField);
                return query.Descending ? -comparison : comparison;
            });

            return matched;
        }

        /// <summary> Compares the same field of two documents; a document missing the field sorts before one that has it. </summary>
        /// <param name="left"> First document. </param>
        /// <param name="right"> Second document. </param>
        /// <param name="field"> Field to compare. </param>
        /// <returns> Negative, zero or positive following the <see cref="IComparable"/> convention. </returns>
        static int CompareFields(JsonElement left, JsonElement right, string field)
        {
            bool hasLeft = TryFindProperty(left, field, out JsonElement leftValue);
            bool hasRight = TryFindProperty(right, field, out JsonElement rightValue);

            if (!hasLeft || !hasRight) return hasLeft.CompareTo(hasRight);

            return leftValue.ValueKind switch
            {
                JsonValueKind.Number when rightValue.ValueKind == JsonValueKind.Number
                    => leftValue.GetDouble().CompareTo(rightValue.GetDouble()),
                JsonValueKind.String when rightValue.ValueKind == JsonValueKind.String
                    => string.CompareOrdinal(leftValue.GetString(), rightValue.GetString()),
                _ => string.CompareOrdinal(leftValue.GetRawText(), rightValue.GetRawText())
            };
        }

        /// <summary> Tests one document against one filter. </summary>
        /// <param name="root"> The document to test. </param>
        /// <param name="filter"> The condition it has to satisfy. </param>
        /// <returns> True when the document satisfies the filter. </returns>
        static bool Matches(JsonElement root, DocumentFilter filter)
        {
            if (!TryFindProperty(root, filter.Field, out JsonElement storedValue)) return false;

            if (filter.Comparison == DocumentComparison.ArrayContains)
            {
                return storedValue.ValueKind == JsonValueKind.Array
                    && storedValue.EnumerateArray().Any(item => CompareToFilterValue(item, filter.Value) == 0);
            }

            int? comparison = CompareToFilterValue(storedValue, filter.Value);
            if (comparison is null) return false;

            return filter.Comparison switch
            {
                DocumentComparison.Equal => comparison == 0,
                DocumentComparison.NotEqual => comparison != 0,
                DocumentComparison.LessThan => comparison < 0,
                DocumentComparison.LessThanOrEqual => comparison <= 0,
                DocumentComparison.GreaterThan => comparison > 0,
                DocumentComparison.GreaterThanOrEqual => comparison >= 0,
                _ => false
            };
        }

        /// <summary> Compares a stored JSON value against a filter value of an unknown runtime type. </summary>
        /// <param name="storedValue"> The value read out of the document. </param>
        /// <param name="filterValue"> The value the filter carries. </param>
        /// <returns> Negative, zero or positive as in <see cref="IComparable"/>; null when the two are not comparable at all. </returns>
        static int? CompareToFilterValue(JsonElement storedValue, object? filterValue)
        {
            switch (storedValue.ValueKind)
            {
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    return filterValue is null ? 0 : null;

                case JsonValueKind.True or JsonValueKind.False:
                    return filterValue is bool expectedFlag
                        ? (storedValue.ValueKind == JsonValueKind.True).CompareTo(expectedFlag)
                        : null;

                case JsonValueKind.Number:
                    return filterValue is null or bool
                        ? null
                        : storedValue.GetDouble().CompareTo(Convert.ToDouble(filterValue, System.Globalization.CultureInfo.InvariantCulture));

                case JsonValueKind.String:
                    return filterValue is null ? null : string.CompareOrdinal(storedValue.GetString(), filterValue.ToString());

                default:
                    return null;
            }
        }

        /// <summary> Looks a field up on a document, ignoring case so a caller's PascalCase field name still finds a camelCase stored one. </summary>
        /// <param name="root"> The document to search. </param>
        /// <param name="field"> Field name to find. </param>
        /// <param name="value"> Receives the field's value, or the default element when it is absent. </param>
        /// <returns> True when the field exists on the document. </returns>
        static bool TryFindProperty(JsonElement root, string field, out JsonElement value)
        {
            value = default;
            if (root.ValueKind != JsonValueKind.Object) return false;

            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!string.Equals(property.Name, field, StringComparison.OrdinalIgnoreCase)) continue;

                value = property.Value;
                return true;
            }

            return false;
        }

        /// <summary> Reads a page cursor back into the index it stands for; an unusable cursor restarts from the first page rather than failing the query. </summary>
        /// <param name="cursor"> Cursor from a previous page, or null. </param>
        /// <returns> Index of the first document of the requested page. </returns>
        static int ParseCursor(string? cursor)
            => int.TryParse(cursor, out int index) && index > 0 ? index : 0;
    }
}

