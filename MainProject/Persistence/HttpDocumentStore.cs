using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChaySocial.MainProject.Protection;

namespace ChaySocial.MainProject.Persistence
{
    /// <summary>
    /// Talks to a document server over HTTP. Everything the app does with data goes through <see cref="IDocumentStore"/>,
    /// so this class is the only place that knows a network exists — replacing it with a Firestore client changes
    /// which instance is constructed and nothing else.
    /// </summary>
    /// <param name="httpClient"> Client whose <c>BaseAddress</c> points at the document server. </param>
    /// <param name="proofOfWork"> Supplies the proof the server charges for writes, or null when the server asks for none. </param>
    public sealed class HttpDocumentStore(HttpClient httpClient, ProofOfWorkClient? proofOfWork = null) : IDocumentStore
    {
        static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        public async Task<TDocument?> ReadAsync<TDocument>(DocumentId<TDocument> id, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>
        {
            try
            {
                HttpResponseMessage response = await httpClient.GetAsync(
                    DocumentRoutes.Document(TDocument.CollectionName, id.Value), cancellationToken);

                if (response.StatusCode == HttpStatusCode.NotFound) return default;

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TDocument>(SerializerOptions, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Log($"Reading {TDocument.CollectionName}/{id} failed.\n{error}", LogLevel.Error);
                return default;
            }
        }

        public async Task WriteAsync<TDocument>(DocumentId<TDocument> id, TDocument document, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>
        {
            HttpRequestMessage request = new(HttpMethod.Put, DocumentRoutes.Document(TDocument.CollectionName, id.Value))
            {
                Content = JsonContent.Create(document, options: SerializerOptions)
            };

            // Writes that carry an account's words name that account, so the server can see it holds a permit.
            // Nothing is solved here: the permit was earned once, long before this write.
            if (proofOfWork is { HasWritingPermit: true } permitted)
            {
                request.Headers.Add(ProofRoutes.AccountHeader, permitted.PermittedAddress);
            }

            HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

            // The server refuses writes from an account with no writing permit. Named as its own failure rather
            // than left as a generic one, because it is the only refusal a person can actually do something about.
            if (response.StatusCode == HttpStatusCode.PaymentRequired) throw new WritingNotPermittedException();

            response.EnsureSuccessStatusCode();
        }

        public async Task DeleteAsync<TDocument>(DocumentId<TDocument> id, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>
        {
            HttpResponseMessage response = await httpClient.DeleteAsync(
                DocumentRoutes.Document(TDocument.CollectionName, id.Value), cancellationToken);

            response.EnsureSuccessStatusCode();
        }

        public async Task<DocumentPage<TDocument>> QueryAsync<TDocument>(DocumentQuery<TDocument> query, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>
        {
            DocumentQueryRequest request = new(
                [.. query.Matches.Select(match => new DocumentMatchRequest(match.Field.Name, match.Value))],
                query.SortField?.Name,
                query.SortDescending,
                query.Limit,
                query.Cursor);

            try
            {
                HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                    DocumentRoutes.Query(TDocument.CollectionName), request, SerializerOptions, cancellationToken);

                response.EnsureSuccessStatusCode();

                DocumentQueryResponse<TDocument>? answer =
                    await response.Content.ReadFromJsonAsync<DocumentQueryResponse<TDocument>>(SerializerOptions, cancellationToken);

                return answer is null
                    ? new DocumentPage<TDocument>([], null)
                    : new DocumentPage<TDocument>(answer.Documents, answer.NextCursor);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Log($"Querying {TDocument.CollectionName} failed.\n{error}", LogLevel.Error);
                return new DocumentPage<TDocument>([], null);
            }
        }
    }
}
