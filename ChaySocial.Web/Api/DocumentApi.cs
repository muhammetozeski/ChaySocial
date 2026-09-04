using System.Text.Json;
using System.Text.Json.Nodes;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.Web.Api
{
    /// <summary>
    /// The document server, holding everything in memory for now. It stores documents as raw JSON and never
    /// deserializes them into app types: it knows only that a document has fields it can sort and filter by. That
    /// keeps it free of the app's model, so replacing it with Firestore changes no shape the client depends on.
    /// </summary>
    public sealed class JsonDocumentStore
    {
        readonly Dictionary<string, Dictionary<string, JsonNode>> _collections = [];
        readonly Lock _gate = new();

        /// <summary> Fetches one document. </summary>
        /// <param name="collection"> Collection holding it. </param>
        /// <param name="documentId"> Id inside that collection. </param>
        /// <returns> The stored JSON, or null when nothing is there. </returns>
        public JsonNode? Read(string collection, string documentId)
        {
            lock (_gate)
            {
                return _collections.TryGetValue(collection, out Dictionary<string, JsonNode>? documents)
                       && documents.TryGetValue(documentId, out JsonNode? document)
                    ? document
                    : null;
            }
        }

        /// <summary> Stores a document, replacing whatever was under that id. </summary>
        /// <param name="collection"> Collection to write into. </param>
        /// <param name="documentId"> Id to store it under. </param>
        /// <param name="document"> The JSON to store. </param>
        public void Write(string collection, string documentId, JsonNode document)
        {
            lock (_gate)
            {
                if (!_collections.TryGetValue(collection, out Dictionary<string, JsonNode>? documents))
                {
                    documents = [];
                    _collections[collection] = documents;
                }

                documents[documentId] = document;
            }
        }

        /// <summary> Removes a document. Removing an absent id is not an error. </summary>
        /// <param name="collection"> Collection holding it. </param>
        /// <param name="documentId"> Id to remove. </param>
        public void Delete(string collection, string documentId)
        {
            lock (_gate)
            {
                if (_collections.TryGetValue(collection, out Dictionary<string, JsonNode>? documents))
                {
                    documents.Remove(documentId);
                }
            }
        }

        /// <summary> Answers one page of a query. </summary>
        /// <param name="collection"> Collection to read. </param>
        /// <param name="request"> Conditions, ordering and paging. </param>
        /// <returns> The matching page and the cursor for the next one. </returns>
        public (List<JsonNode> Documents, string? NextCursor) Query(string collection, DocumentQueryRequest request)
        {
            List<JsonNode> candidates;
            lock (_gate)
            {
                candidates = _collections.TryGetValue(collection, out Dictionary<string, JsonNode>? documents)
                    ? [.. documents.Values]
                    : [];
            }

            List<JsonNode> matched = [.. candidates.Where(document => request.Matches.All(match => Matches(document, match)))];

            if (!string.IsNullOrEmpty(request.SortField))
            {
                matched.Sort((left, right) =>
                {
                    int comparison = Compare(FindField(left, request.SortField), FindField(right, request.SortField));
                    return request.SortDescending ? -comparison : comparison;
                });
            }

            int startIndex = int.TryParse(request.Cursor, out int parsed) && parsed > 0 ? parsed : 0;
            List<JsonNode> page = [.. matched.Skip(startIndex).Take(Math.Clamp(request.Limit, 1, MaximumPageSize))];
            int nextIndex = startIndex + page.Count;

            return (page, nextIndex < matched.Count ? nextIndex.ToString() : null);
        }

        /// <summary> Largest page a client may ask for, so one request cannot pull a whole collection. </summary>
        const int MaximumPageSize = 200;

        /// <summary> Tests one document against one equality condition. </summary>
        /// <param name="document"> Document to test. </param>
        /// <param name="match"> Field name and expected value. </param>
        /// <returns> True when the document's field equals the value. </returns>
        static bool Matches(JsonNode document, DocumentMatchRequest match)
        {
            JsonNode? stored = FindField(document, match.Field);
            if (stored is null) return match.Value is null;

            string? expected = match.Value switch
            {
                null => null,
                JsonElement element => element.ToString(),
                _ => match.Value.ToString()
            };

            return stored.ToString() == expected;
        }

        /// <summary> Reads a field by name, ignoring case so a caller's PascalCase name finds the camelCase stored one. </summary>
        /// <param name="document"> Document to search. </param>
        /// <param name="fieldName"> Field to find. </param>
        /// <returns> The field's value, or null when the document does not carry it. </returns>
        static JsonNode? FindField(JsonNode document, string fieldName)
        {
            if (document is not JsonObject jsonObject) return null;

            foreach (KeyValuePair<string, JsonNode?> property in jsonObject)
            {
                if (string.Equals(property.Key, fieldName, StringComparison.OrdinalIgnoreCase)) return property.Value;
            }

            return null;
        }

        /// <summary> Orders two field values, numerically when both are numbers and textually otherwise. </summary>
        /// <param name="left"> First value. </param>
        /// <param name="right"> Second value. </param>
        /// <returns> Negative, zero or positive following the <see cref="IComparable"/> convention. </returns>
        static int Compare(JsonNode? left, JsonNode? right)
        {
            if (left is null || right is null) return (left is not null).CompareTo(right is not null);

            return double.TryParse(left.ToString(), out double leftNumber) && double.TryParse(right.ToString(), out double rightNumber)
                ? leftNumber.CompareTo(rightNumber)
                : string.CompareOrdinal(left.ToString(), right.ToString());
        }
    }

    /// <summary> Publishes <see cref="JsonDocumentStore"/> over the routes <see cref="DocumentRoutes"/> describes. </summary>
    public static class DocumentApi
    {
        /// <summary> Registers every document route on the application. </summary>
        /// <param name="app"> Application to register on. </param>
        public static void MapDocumentApi(this WebApplication app)
        {
            JsonDocumentStore store = app.Services.GetRequiredService<JsonDocumentStore>();

            app.MapGet($"{DocumentRoutes.Base}/{{collection}}/{{documentId}}", (string collection, string documentId) =>
            {
                JsonNode? document = store.Read(collection, documentId);
                return document is null ? Results.NotFound() : Results.Content(document.ToJsonString(), "application/json");
            });

            app.MapPut($"{DocumentRoutes.Base}/{{collection}}/{{documentId}}", async (string collection, string documentId, HttpRequest request) =>
            {
                JsonNode? document = await JsonNode.ParseAsync(request.Body);
                if (document is null) return Results.BadRequest();

                store.Write(collection, documentId, document);
                return Results.Ok();
            });

            app.MapDelete($"{DocumentRoutes.Base}/{{collection}}/{{documentId}}", (string collection, string documentId) =>
            {
                store.Delete(collection, documentId);
                return Results.Ok();
            });

            app.MapPost($"{DocumentRoutes.Base}/{{collection}}/query", (string collection, DocumentQueryRequest request) =>
            {
                (List<JsonNode> documents, string? nextCursor) = store.Query(collection, request);

                JsonObject answer = new()
                {
                    ["documents"] = new JsonArray([.. documents.Select(document => JsonNode.Parse(document.ToJsonString()))]),
                    ["nextCursor"] = nextCursor
                };

                return Results.Content(answer.ToJsonString(), "application/json");
            });
        }
    }
}
