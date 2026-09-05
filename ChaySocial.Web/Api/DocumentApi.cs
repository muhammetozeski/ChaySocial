using System.Text.Json;
using System.Text.Json.Nodes;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Protection;

namespace ChaySocial.Web.Api
{
    /// <summary>
    /// The document server. It stores documents as raw JSON and never deserializes them into app types: it knows
    /// only that a document has fields it can sort and filter by. That keeps it free of the app's model, so
    /// replacing it with Firestore changes no shape the client depends on.
    /// Documents are held in memory for fast queries and mirrored to disk on every write, so a restart reloads
    /// everything instead of starting empty.
    /// </summary>
    /// <param name="storage"> Where documents are persisted, or null to keep them only for the life of the process. </param>
    public sealed class JsonDocumentStore(DocumentFileStorage? storage = null)
    {
        readonly Dictionary<string, Dictionary<string, JsonNode>> _collections = [];
        readonly Lock _gate = new();

        /// <summary> Reads everything back off disk into memory. Called once at startup, before the first request. </summary>
        /// <returns> How many documents were restored. </returns>
        public int RestoreFromDisk()
        {
            if (storage is null) return 0;

            List<(string Collection, string DocumentId, JsonNode Document)> loaded = storage.LoadAll();

            lock (_gate)
            {
                foreach ((string collection, string documentId, JsonNode document) in loaded)
                {
                    if (!_collections.TryGetValue(collection, out Dictionary<string, JsonNode>? documents))
                    {
                        documents = [];
                        _collections[collection] = documents;
                    }

                    documents[documentId] = document;
                }
            }

            return loaded.Count;
        }

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

            // Written after the in-memory update and outside the lock: a failed disk write must not lose the
            // document the client just stored, and a slow disk must not block every other reader.
            Persist(() => storage?.Save(collection, documentId, document), $"save {collection}/{documentId}");
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

            Persist(() => storage?.Remove(collection, documentId), $"remove {collection}/{documentId}");
        }

        /// <summary>
        /// Runs one disk operation. A disk failure is reported and swallowed on purpose: the in-memory copy is
        /// already correct, so the request should still succeed and only durability is lost.
        /// </summary>
        /// <param name="operation"> The disk write or delete to attempt. </param>
        /// <param name="description"> What was being attempted, for the failure line. </param>
        static void Persist(Action operation, string description)
        {
            try
            {
                operation();
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Could not {description} to disk: {error.Message}");
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

            // A request that names no conditions is a request for everything, not a malformed one. Left to the
            // deserialiser the field simply stays null when it is absent from the body, and reading it directly
            // turned a perfectly reasonable "give me the newest" into a 500.
            IReadOnlyList<DocumentMatchRequest> conditions = request.Matches ?? [];
            List<JsonNode> matched = [.. candidates.Where(document => conditions.All(match => Matches(document, match)))];

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
        /// <summary>
        /// Collections that put an account's words in front of other people, and therefore need that account to
        /// hold a writing permit. Everything else — opening an account, following, blocking, pouring somebody a
        /// chay, being notified — is free and instant, because none of it is a place to put spam.
        /// </summary>
        /// <remarks>
        /// The cost sits at one door and is paid once. Charging every write instead made the app punish the people
        /// using it: a second and a half of somebody's phone burnt per message, paid by everyone, to inconvenience
        /// a farm that is happy to spend it. One slow permit per account is what makes a thousand posting accounts
        /// expensive, which is the thing worth making expensive.
        /// </remarks>
        static readonly string[] PermittedWritingCollections =
            ["posts", "comments", "messages", "reposts", "groups", "subjects", "pages", "pageeditors"];

        /// <summary> Registers every document route on the application. </summary>
        /// <param name="app"> Application to register on. </param>
        public static void MapDocumentApi(this WebApplication app)
        {
            JsonDocumentStore store = app.Services.GetRequiredService<JsonDocumentStore>();
            WritingPermitRegistry permits = app.Services.GetRequiredService<WritingPermitRegistry>();

            app.MapGet($"{DocumentRoutes.Base}/{{collection}}/{{documentId}}", (string collection, string documentId) =>
            {
                JsonNode? document = store.Read(collection, documentId);
                return document is null ? Results.NotFound() : Results.Content(document.ToJsonString(), "application/json");
            });

            app.MapPut($"{DocumentRoutes.Base}/{{collection}}/{{documentId}}", async (string collection, string documentId, HttpRequest request) =>
            {
                if (!IsWriteAllowed(permits, collection, request)) return Results.StatusCode(StatusCodes.Status402PaymentRequired);

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

        /// <summary>
        /// Decides whether a write may proceed. Everything may, except writing into a collection that carries an
        /// account's words: that needs the account named in the request to hold a writing permit.
        /// </summary>
        /// <param name="permits"> Registry of accounts that have paid for a permit. </param>
        /// <param name="collection"> Collection being written to. </param>
        /// <param name="request"> Request naming the writing account in its header. </param>
        /// <returns> True when the write may proceed. </returns>
        /// <remarks>
        /// The header names the account rather than proving it, which is enough for what this gate is for. Somebody
        /// could borrow a permitted address to get past it, but every reader checks a post's signature against the
        /// address it claims, so what they smuggle through is drawn as unverified and convinces nobody. Making that
        /// airtight would mean the server verifying every signature it stores, which is exactly the work this app
        /// keeps off the server.
        /// </remarks>
        static bool IsWriteAllowed(WritingPermitRegistry permits, string collection, HttpRequest request)
        {
            if (!PermittedWritingCollections.Contains(collection)) return true;

            return permits.IsGranted(request.Headers[ProofRoutes.AccountHeader].ToString());
        }
    }
}
