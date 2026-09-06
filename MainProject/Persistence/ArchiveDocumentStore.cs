using System.Globalization;
using ChaySocial.MainProject.DataModels;

namespace ChaySocial.MainProject.Persistence
{
    /// <summary>
    /// Reads a sealed archive as though it were a server.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An archive that can only be poured back into another server is a backup, and a backup is worth something
    /// only as long as servers exist. Reading the file itself turns exporting into owning: the wall, the profile
    /// and both halves of every conversation come back with nothing listening on any port.
    /// </para>
    /// <para>
    /// Writing does nothing, on purpose. A sealed file is not a place to write, and the seal covers exactly the
    /// documents that were in it — a write here would either be lost or would quietly break that signature. The
    /// screens are kept out of the writing paths instead, so nothing is offered that cannot happen.
    /// </para>
    /// </remarks>
    /// <param name="archive"> The archive to read from, already checked against the seal its owner signed. </param>
    public sealed class ArchiveDocumentStore(AccountArchive archive) : IDocumentStore
    {
        /// <summary> Everything the archive holds, kept per type so a read never looks in the wrong collection. </summary>
        readonly Dictionary<Type, object> _held = new()
        {
            [typeof(ProfileData)] = archive.Profile is null ? (IReadOnlyList<ProfileData>)[] : [archive.Profile],
            [typeof(PostData)] = archive.Posts,
            [typeof(CommentData)] = archive.Comments,
            [typeof(RepostData)] = archive.Reposts,
            [typeof(FollowData)] = archive.Follows,
            [typeof(LikeData)] = archive.Likes,
            [typeof(MessageData)] = archive.Messages
        };

        /// <summary> When the archive was sealed, so a reader can be told how old what they are looking at is. </summary>
        public long SealedAtUnixMs => archive.SealedAtUnixMs;

        /// <summary> Address of the account the archive belongs to. </summary>
        public string Address => archive.Address;

        /// <summary> Finds one document by id. </summary>
        /// <typeparam name="TDocument"> Kind of document to read. </typeparam>
        /// <param name="id"> Id of the document. </param>
        /// <param name="cancellationToken"> Cancels the read; nothing here blocks, so it is never observed. </param>
        /// <returns> The document, or null when the archive does not hold it. </returns>
        public Task<TDocument?> ReadAsync<TDocument>(DocumentId<TDocument> id, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>
            => Task.FromResult(Held<TDocument>().FirstOrDefault(document => document.Id.Value == id.Value));

        /// <summary>
        /// Does nothing, and says so. A sealed archive is not a place to write: the seal covers exactly the
        /// documents inside it.
        /// </summary>
        /// <typeparam name="TDocument"> Kind of document that was offered. </typeparam>
        /// <param name="id"> Id it would have been stored under. </param>
        /// <param name="document"> The document that is not being stored. </param>
        /// <param name="cancellationToken"> Cancels nothing. </param>
        public Task WriteAsync<TDocument>(DocumentId<TDocument> id, TDocument document, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>
            => Task.CompletedTask;

        /// <summary> Does nothing, for the same reason as <see cref="WriteAsync"/>. </summary>
        /// <typeparam name="TDocument"> Kind of document that would have gone. </typeparam>
        /// <param name="id"> Id that is not being removed. </param>
        /// <param name="cancellationToken"> Cancels nothing. </param>
        public Task DeleteAsync<TDocument>(DocumentId<TDocument> id, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>
            => Task.CompletedTask;

        /// <summary>
        /// Answers a query out of what the archive holds. Each match is tested with the field's own reader and each
        /// sort with the sort field's, which is exactly the in-process shape those readers were declared for.
        /// </summary>
        /// <typeparam name="TDocument"> Kind of document to query. </typeparam>
        /// <param name="query"> Which documents to match, in what order, and how many. </param>
        /// <param name="cancellationToken"> Cancels the query; nothing here blocks, so it is never observed. </param>
        /// <returns> The matching page. </returns>
        public Task<DocumentPage<TDocument>> QueryAsync<TDocument>(DocumentQuery<TDocument> query, CancellationToken cancellationToken = default)
            where TDocument : IStoredDocument<TDocument>
        {
            IEnumerable<TDocument> matching = Held<TDocument>();

            foreach ((DocumentField<TDocument> field, IComparable? value) in query.Matches)
            {
                matching = matching.Where(document => Equals(field.Read(document), value));
            }

            if (query.SortField is DocumentField<TDocument> sortField)
            {
                matching = query.SortDescending
                    ? matching.OrderByDescending(sortField.Read)
                    : matching.OrderBy(sortField.Read);
            }

            List<TDocument> ordered = [.. matching];
            int from = ReadCursor(query.Cursor);
            int taken = Math.Clamp(query.Limit, 0, Math.Max(0, ordered.Count - from));

            // The cursor is how far in the reader has already come, which is all a list in memory needs: the whole
            // result is settled before the page is cut out of it, so there is nothing for a later page to miss.
            string? nextCursor = from + taken < ordered.Count
                ? (from + taken).ToString(CultureInfo.InvariantCulture)
                : null;

            return Task.FromResult(new DocumentPage<TDocument>(ordered.GetRange(from, taken), nextCursor));
        }

        /// <summary> The archive's documents of one kind, or an empty list for a kind an archive never carries. </summary>
        /// <typeparam name="TDocument"> Kind of document wanted. </typeparam>
        /// <returns> What the archive holds of that kind. </returns>
        /// <remarks>
        /// An archive holds what its owner wrote, so a kind it never carries — blocks, notifications, groups —
        /// reads as empty rather than as an error. Offline, that is the truth: there is nothing of it to show.
        /// </remarks>
        IReadOnlyList<TDocument> Held<TDocument>() where TDocument : IStoredDocument<TDocument>
            => _held.TryGetValue(typeof(TDocument), out object? documents) ? (IReadOnlyList<TDocument>)documents : [];

        /// <summary> Reads how far a previous page reached, treating anything unreadable as the start. </summary>
        /// <param name="cursor"> Cursor from the previous page, or null. </param>
        /// <returns> The index to carry on from. </returns>
        static int ReadCursor(string? cursor)
            => cursor is not null && int.TryParse(cursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out int from) && from > 0
                ? from
                : 0;
    }
}
