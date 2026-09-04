using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Publishing and reading the replies under a post. A comment is signed on its author's device exactly like a
    /// post is, but under its own domain label, so a signature taken from a post can never be pasted onto a comment
    /// and pass verification — the two transcripts can never produce the same bytes.
    /// </summary>
    public static class CommentService
    {
        /// <summary> Separates a comment signature from a post signature, so neither can stand in for the other. </summary>
        static readonly byte[] CommentSignatureDomain = "ChaySocial/Comment/v1"u8.ToArray();

        /// <summary> Comments fetched in one page of a thread. </summary>
        public const int ThreadPageSize = 50;

        /// <summary> Reads a post's thread from the top, the way a conversation is read. </summary>
        /// <param name="postId"> Post whose replies are wanted. </param>
        /// <param name="limit"> Largest number of comments to return. </param>
        /// <returns> That post's comments, oldest first. </returns>
        public static async Task<IReadOnlyList<CommentData>> ReadForPostAsync(string postId, int limit = ThreadPageSize)
        {
            DocumentQuery<CommentData> query = new DocumentQuery<CommentData>()
                .WithMatch(CommentData.PostField, postId)
                .WithSort(CommentData.CreatedAtField)
                .WithLimit(limit);

            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary> Counts the replies under a post, for the comment badge on a post card. </summary>
        /// <param name="postId"> Post to count. </param>
        /// <returns> How many comments are stored for it, at most <see cref="MaximumCountedComments"/>. </returns>
        public static async Task<int> CountForPostAsync(string postId)
        {
            DocumentQuery<CommentData> query = new DocumentQuery<CommentData>()
                .WithMatch(CommentData.PostField, postId)
                .WithLimit(MaximumCountedComments);

            return (await AppServices.Documents.QueryAsync(query)).Documents.Count;
        }

        /// <summary>
        /// Signs a reply as the given account, stores it, and alerts the post's author unless the author is the one
        /// replying — nobody needs to be told about their own comment.
        /// </summary>
        /// <param name="author"> The unlocked account writing the reply. </param>
        /// <param name="post"> Post being replied to. </param>
        /// <param name="text"> What to publish; trimmed, and refused when empty or over <see cref="CommentData.MaximumTextLength"/>. </param>
        /// <returns> The stored comment, or null when the text was not publishable. </returns>
        public static async Task<CommentData?> PublishAsync(PrivateIdentity author, PostData post, string text)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed.Length > CommentData.MaximumTextLength) return null;

            string commentId = Base32.Encode(RandomSource.Next(CommentIdBytes));
            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            byte[] transcript = BuildTranscript(commentId, post.PostId, author.Public.Address, trimmed, createdAt);

            CommentData comment = new()
            {
                CommentId = commentId,
                PostId = post.PostId,
                AuthorAddress = author.Public.Address,
                Text = trimmed,
                CreatedAtUnixMs = createdAt,
                Signature = Convert.ToBase64String(author.Sign(transcript))
            };

            await AppServices.Documents.WriteAsync(comment.Id, comment);

            if (post.AuthorAddress != author.Public.Address)
            {
                await NotificationService.NotifyAsync(
                    post.AuthorAddress,
                    author.Public.Address,
                    NotificationKind.Comment,
                    post.PostId,
                    BuildPreview(trimmed));
            }

            MainEvents.Trigger(MainEvents.Names.CommentsChanged, post.PostId);
            return comment;
        }

        /// <summary> Removes one of the signed-in account's own comments. </summary>
        /// <param name="comment"> Comment to remove. </param>
        /// <param name="author"> Account asking for the removal; anything else is ignored. </param>
        public static async Task DeleteAsync(CommentData comment, PublicIdentity author)
        {
            if (comment.AuthorAddress != author.Address) return;

            await AppServices.Documents.DeleteAsync(comment.Id);
            MainEvents.Trigger(MainEvents.Names.CommentsChanged, comment.PostId);
        }

        /// <summary>
        /// Checks that a comment really was written by the account it names, using the signing key published in that
        /// account's profile. A comment that fails this was altered or forged after it left its author.
        /// </summary>
        /// <param name="comment"> Comment to check. </param>
        /// <param name="authorProfile"> Profile of the account the comment names, or null when it could not be read. </param>
        /// <returns> True when the signature verifies against the author's published key. </returns>
        public static bool VerifyAuthorship(CommentData comment, ProfileData? authorProfile)
        {
            if (authorProfile is null || authorProfile.Address != comment.AuthorAddress) return false;

            try
            {
                PublicIdentity author = new(
                    authorProfile.Address,
                    Convert.FromBase64String(authorProfile.SigningPublicKey),
                    Convert.FromBase64String(authorProfile.EncryptionPublicKey));

                byte[] transcript = BuildTranscript(
                    comment.CommentId,
                    comment.PostId,
                    comment.AuthorAddress,
                    comment.Text,
                    comment.CreatedAtUnixMs);

                return AppCryptography.Identities.Verify(transcript, Convert.FromBase64String(comment.Signature), author);
            }
            catch (FormatException error)
            {
                Log($"Comment '{comment.CommentId}' carries malformed base64.\n{error}", LogLevel.Warning);
                return false;
            }
        }

        /// <summary> Random bytes behind a comment id — enough that two comments never collide. </summary>
        const int CommentIdBytes = 12;

        /// <summary> Largest number of comments read back while counting one post's thread. </summary>
        const int MaximumCountedComments = 500;

        /// <summary> Builds the exact bytes an author signs and a reader verifies. </summary>
        /// <param name="commentId"> The comment's id. </param>
        /// <param name="postId"> Post the comment replies to. </param>
        /// <param name="authorAddress"> Address of the author. </param>
        /// <param name="text"> The comment's text. </param>
        /// <param name="createdAtUnixMs"> Publication time. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTranscript(string commentId, string postId, string authorAddress, string text, long createdAtUnixMs)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(CommentSignatureDomain);
            transcript.WriteText(commentId);
            transcript.WriteText(postId);
            transcript.WriteText(authorAddress);
            transcript.WriteText(text);
            transcript.WriteInt64(createdAtUnixMs);
            return transcript.ToArray();
        }

        /// <summary>
        /// Shortens a comment down to what an alerts line shows. The cut is pulled back off a lone high surrogate, so
        /// an emoji sitting on the boundary is dropped whole instead of leaving half a character behind.
        /// </summary>
        /// <param name="text"> The comment's trimmed text. </param>
        /// <returns> At most <see cref="NotificationData.MaximumPreviewLength"/> characters of it. </returns>
        static string BuildPreview(string text)
        {
            if (text.Length <= NotificationData.MaximumPreviewLength) return text;

            int cut = NotificationData.MaximumPreviewLength;
            if (char.IsHighSurrogate(text[cut - 1])) cut--;

            return text[..cut];
        }
    }
}
