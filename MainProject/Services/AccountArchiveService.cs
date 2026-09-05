using System.Text.Json;
using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Puts everything an account has written into one file, and puts it back somewhere else. This is the piece that
    /// makes moving to another server mean something: without it, leaving costs a person their whole history, which
    /// is not really a choice.
    /// </summary>
    public static class AccountArchiveService
    {
        /// <summary> Separates this signature's meaning from every other signature the app produces. </summary>
        static readonly byte[] ArchiveSignatureDomain = "ChaySocial/Archive/v1"u8.ToArray();

        /// <summary> How many documents are asked for at a time while gathering a collection. </summary>
        const int GatherPageSize = 200;

        /// <summary>
        /// Most pages read from one collection before gathering stops. A safety rail, not a limit anybody is meant
        /// to reach: it stops a broken cursor from looping forever rather than capping any real account.
        /// </summary>
        const int MostPagesPerCollection = 500;

        /// <summary> How the archive is written and read, matching what the document store already uses. </summary>
        static readonly JsonSerializerOptions ArchiveJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        /// <summary>
        /// Gathers everything this account has written and seals it.
        /// </summary>
        /// <param name="owner"> The account whose work is being gathered. </param>
        /// <returns> The sealed archive. </returns>
        public static async Task<AccountArchive> BuildAsync(PrivateIdentity owner)
        {
            string address = owner.Public.Address;

            ProfileData? profile = await AppServices.Documents.ReadAsync(new DocumentId<ProfileData>(address));

            List<PostData> posts = await GatherAsync(PostData.AuthorField, address, post => post.PostId);
            List<CommentData> comments = await GatherAsync(CommentData.AuthorField, address, comment => comment.CommentId);
            List<RepostData> reposts = await GatherAsync(RepostData.ReposterField, address, repost => repost.Id.Value);
            List<FollowData> follows = await GatherAsync(FollowData.FollowerField, address, follow => follow.Id.Value);
            List<LikeData> likes = await GatherAsync(LikeData.LikerField, address, like => like.Id.Value);

            // Both halves of a conversation: a letter somebody sent this account is as much a part of its history as
            // one it wrote, and both sit in the same document.
            List<MessageData> sent = await GatherAsync(MessageData.SenderField, address, message => message.MessageId);
            List<MessageData> received = await GatherAsync(MessageData.RecipientField, address, message => message.MessageId);
            List<MessageData> messages = [.. sent
                .Concat(received)
                .DistinctBy(message => message.MessageId, StringComparer.Ordinal)
                .OrderBy(message => message.MessageId, StringComparer.Ordinal)];

            AccountArchive archive = new()
            {
                Address = address,
                SealedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Profile = profile,
                Posts = posts,
                Comments = comments,
                Reposts = reposts,
                Follows = follows,
                Likes = likes,
                Messages = messages
            };

            byte[] transcript = BuildTranscript(archive);
            return archive with { Signature = Convert.ToBase64String(owner.Sign(transcript)) };
        }

        /// <summary>
        /// Checks that an archive really was sealed by the account it names.
        /// </summary>
        /// <param name="archive"> The archive as it was read from a file. </param>
        /// <param name="sealer"> The public identity of the account it claims to belong to. </param>
        /// <returns> True when the signature covers exactly these documents and verifies. </returns>
        public static bool VerifySeal(AccountArchive archive, PublicIdentity sealer)
        {
            if (archive.Signature.Length == 0 || archive.Address != sealer.Address) return false;

            try
            {
                return AppCryptography.Identities.Verify(BuildTranscript(archive), Convert.FromBase64String(archive.Signature), sealer);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary> Writes the archive out as the bytes that go into a file. </summary>
        /// <param name="archive"> The archive to write. </param>
        /// <returns> The file's contents. </returns>
        public static byte[] Serialise(AccountArchive archive) => JsonSerializer.SerializeToUtf8Bytes(archive, ArchiveJson);

        /// <summary> Reads an archive back from a file's contents. </summary>
        /// <param name="content"> The bytes that were in the file. </param>
        /// <returns> The archive, or null when the file is not one. </returns>
        public static AccountArchive? Deserialise(ReadOnlySpan<byte> content)
        {
            try
            {
                return JsonSerializer.Deserialize<AccountArchive>(content, ArchiveJson);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Writes an archive's documents back into whichever store this app is pointed at.
        /// </summary>
        /// <param name="archive"> The archive to restore. </param>
        /// <param name="owner"> The account doing the restoring. </param>
        /// <returns> How many documents went in and how many were refused. </returns>
        /// <remarks>
        /// Every document is checked against the restoring account before it is written. A file is just bytes and
        /// anybody can edit one, so without this check an archive handed to somebody else would be a way of writing
        /// documents in their name. A message is the one kind with two rightful owners, so either side may carry it.
        /// </remarks>
        public static async Task<ArchiveRestoreResult> RestoreAsync(AccountArchive archive, PrivateIdentity owner)
        {
            string address = owner.Public.Address;
            int written = 0;
            int refused = 0;

            if (archive.Profile is { } profile)
            {
                if (profile.Address == address) { await AppServices.Documents.WriteAsync(profile.Id, profile); written++; }
                else refused++;
            }

            foreach (PostData post in archive.Posts)
                await PutAsync(post.AuthorAddress == address, post.Id, post);

            foreach (CommentData comment in archive.Comments)
                await PutAsync(comment.AuthorAddress == address, comment.Id, comment);

            foreach (RepostData repost in archive.Reposts)
                await PutAsync(repost.ReposterAddress == address, repost.Id, repost);

            foreach (FollowData follow in archive.Follows)
                await PutAsync(follow.FollowerAddress == address, follow.Id, follow);

            foreach (LikeData like in archive.Likes)
                await PutAsync(like.LikerAddress == address, like.Id, like);

            foreach (MessageData message in archive.Messages)
                await PutAsync(message.SenderAddress == address || message.RecipientAddress == address, message.Id, message);

            return new ArchiveRestoreResult(written, refused);

            async Task PutAsync<TDocument>(bool belongsHere, DocumentId<TDocument> id, TDocument document)
                where TDocument : IStoredDocument<TDocument>
            {
                if (!belongsHere) { refused++; return; }

                await AppServices.Documents.WriteAsync(id, document);
                written++;
            }
        }

        /// <summary>
        /// Reads every document in one collection that names this account, following the cursor to the end.
        /// </summary>
        /// <typeparam name="TDocument"> Kind of document being gathered. </typeparam>
        /// <param name="field"> The field naming the account. </param>
        /// <param name="address"> The account's address. </param>
        /// <param name="identify"> How to read one document's id, for a stable order. </param>
        /// <returns> Every matching document, ordered by id so two archives of the same account are byte-identical. </returns>
        static async Task<List<TDocument>> GatherAsync<TDocument>(
            DocumentField<TDocument> field,
            string address,
            Func<TDocument, string> identify)
            where TDocument : IStoredDocument<TDocument>
        {
            List<TDocument> gathered = [];
            string? cursor = null;

            for (int page = 0; page < MostPagesPerCollection; page++)
            {
                DocumentQuery<TDocument> query = new DocumentQuery<TDocument>()
                    .WithMatch(field, address)
                    .WithLimit(GatherPageSize)
                    .WithCursor(cursor);

                DocumentPage<TDocument> read = await AppServices.Documents.QueryAsync(query);
                gathered.AddRange(read.Documents);

                cursor = read.NextCursor;
                if (string.IsNullOrEmpty(cursor)) break;
            }

            return [.. gathered.OrderBy(identify, StringComparer.Ordinal)];
        }

        /// <summary>
        /// Builds the exact bytes an archive is signed over: who sealed it, when, and precisely which documents were
        /// in it. Ids rather than contents, because every document that carries meaning already carries its own
        /// signature — what this adds is that the set cannot be added to or thinned out afterwards.
        /// </summary>
        /// <param name="archive"> The archive being sealed or checked. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTranscript(AccountArchive archive)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(ArchiveSignatureDomain);
            transcript.WriteText(archive.Address);
            transcript.WriteInt64(archive.SealedAtUnixMs);
            transcript.WriteNamedText("profile", archive.Profile?.Address ?? string.Empty);

            WriteIds("posts", [.. archive.Posts.Select(post => post.PostId)]);
            WriteIds("comments", [.. archive.Comments.Select(comment => comment.CommentId)]);
            WriteIds("reposts", [.. archive.Reposts.Select(repost => repost.Id.Value)]);
            WriteIds("follows", [.. archive.Follows.Select(follow => follow.Id.Value)]);
            WriteIds("likes", [.. archive.Likes.Select(like => like.Id.Value)]);
            WriteIds("messages", [.. archive.Messages.Select(message => message.MessageId)]);

            return transcript.ToArray();

            void WriteIds(string name, IReadOnlyList<string> ids)
            {
                transcript.WriteText(name);
                transcript.WriteInt64(ids.Count);
                foreach (string id in ids) transcript.WriteText(id);
            }
        }
    }

    /// <summary> What came of restoring an archive. </summary>
    /// <param name="Written"> Documents written into the store. </param>
    /// <param name="Refused"> Documents dropped because they named an account other than the one restoring. </param>
    public readonly record struct ArchiveRestoreResult(int Written, int Refused);
}
