using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary> One comment as a thread draws it: the comment, and whichever comment it was answering. </summary>
    /// <param name="Comment"> The comment itself. </param>
    /// <param name="RepliedTo"> The comment it answers, or null when it speaks to the post — which is also what says whether it is indented. </param>
    public readonly record struct ThreadedComment(CommentData Comment, CommentData? RepliedTo)
    {
        /// <summary> True when this comment sits under another one rather than directly under the post. </summary>
        public bool IsAnswer => RepliedTo is not null;
    }

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

        /// <summary>
        /// Lays a flat list of comments out as a thread: each remark on the post, followed by everything written in
        /// answer to it, oldest first. It stays one level deep on screen even when an answer answers an answer —
        /// the line saying who was spoken to carries that, and a conversation indented six times is unreadable.
        /// An answer whose parent was deleted is drawn as a remark of its own rather than dropped.
        /// </summary>
        /// <param name="comments"> One post's comments, in any order. </param>
        /// <returns> The same comments in reading order, each carrying whichever comment it answered. </returns>
        public static IReadOnlyList<ThreadedComment> ArrangeThread(IReadOnlyList<CommentData> comments)
        {
            Dictionary<string, CommentData> byId = comments.ToDictionary(comment => comment.CommentId);

            // The comment a reply is drawn under: its parent, or its parent's parent, up to the remark that started
            // the exchange. A reply whose chain leads nowhere stands on its own.
            string RootOf(CommentData comment)
            {
                CommentData walker = comment;
                for (int step = 0; step < MaximumReplyDepth && walker.IsReply; step++)
                {
                    if (!byId.TryGetValue(walker.ParentCommentId, out CommentData? parent)) return walker.CommentId;
                    walker = parent;
                }

                return walker.CommentId;
            }

            List<CommentData> ordered = [.. comments.OrderBy(comment => comment.CreatedAtUnixMs)];
            Dictionary<string, List<CommentData>> answersByRoot = [];
            List<CommentData> roots = [];

            foreach (CommentData comment in ordered)
            {
                string root = RootOf(comment);

                if (root == comment.CommentId)
                {
                    roots.Add(comment);
                    continue;
                }

                if (!answersByRoot.TryGetValue(root, out List<CommentData>? answers))
                {
                    answers = [];
                    answersByRoot[root] = answers;
                }

                answers.Add(comment);
            }

            List<ThreadedComment> thread = new(ordered.Count);
            foreach (CommentData root in roots)
            {
                thread.Add(new ThreadedComment(root, null));

                if (!answersByRoot.TryGetValue(root.CommentId, out List<CommentData>? answers)) continue;

                foreach (CommentData answer in answers)
                {
                    thread.Add(new ThreadedComment(answer, byId.GetValueOrDefault(answer.ParentCommentId)));
                }
            }

            return thread;
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
        /// <param name="parent"> Comment being answered, or null when the reply is to the post itself. </param>
        /// <returns> The stored comment, or null when the text was not publishable. </returns>
        public static async Task<CommentData?> PublishAsync(
            PrivateIdentity author,
            PostData post,
            string text,
            CommentData? parent = null)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed.Length > CommentData.MaximumTextLength) return null;

            // A reply belongs to the thread it answers. Letting one point at a comment under another post would put
            // an answer somewhere its question is not, and no reader could make sense of it.
            if (parent is not null && parent.PostId != post.PostId) return null;

            string commentId = Base32.Encode(RandomSource.Next(CommentIdBytes));
            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string parentCommentId = parent?.CommentId ?? string.Empty;

            byte[] transcript = BuildTranscript(
                commentId, post.PostId, author.Public.Address, trimmed, createdAt, parentCommentId);

            CommentData comment = new()
            {
                CommentId = commentId,
                PostId = post.PostId,
                AuthorAddress = author.Public.Address,
                Text = trimmed,
                ParentCommentId = parentCommentId,
                CreatedAtUnixMs = createdAt,
                Signature = Convert.ToBase64String(author.Sign(transcript))
            };

            await AppServices.Documents.WriteAsync(comment.Id, comment);

            // Whoever was spoken to hears about it: the post's author for a comment, and the comment's author for an
            // answer to them. One person filling both roles is told once, and nobody is told about their own words.
            HashSet<string> toTell = [post.AuthorAddress];
            if (parent is not null) toTell.Add(parent.AuthorAddress);
            toTell.Remove(author.Public.Address);

            foreach (string address in toTell)
            {
                await NotificationService.NotifyAsync(
                    address,
                    author.Public.Address,
                    NotificationKind.Comment,
                    post.PostId,
                    trimmed);
            }

            // Anyone named in the comment hears about it too, unless they were already told about the comment
            // itself — being both the post's author and named in the reply is one event, not two.
            await NotificationService.NotifyMentionedAsync(trimmed, author.Public.Address, post.PostId, trimmed, toTell);

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
                    comment.CreatedAtUnixMs,
                    comment.ParentCommentId);

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

        /// <summary>
        /// How far a chain of answers is followed back to the remark that started it. A stored thread should never
        /// contain a cycle, but it arrives from a server, and a walk over hostile data needs an end.
        /// </summary>
        const int MaximumReplyDepth = 64;

        /// <summary> Builds the exact bytes an author signs and a reader verifies. </summary>
        /// <param name="commentId"> The comment's id. </param>
        /// <param name="postId"> Post the comment replies to. </param>
        /// <param name="authorAddress"> Address of the author. </param>
        /// <param name="text"> The comment's text. </param>
        /// <param name="createdAtUnixMs"> Publication time. </param>
        /// <param name="parentCommentId"> Comment being answered, empty for a reply to the post; always written, so nobody can strip it and turn an answer into a remark of its own. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTranscript(
            string commentId,
            string postId,
            string authorAddress,
            string text,
            long createdAtUnixMs,
            string parentCommentId)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(CommentSignatureDomain);
            transcript.WriteText(commentId);
            transcript.WriteText(postId);
            transcript.WriteText(authorAddress);
            transcript.WriteText(text);
            transcript.WriteInt64(createdAtUnixMs);
            transcript.WriteText(parentCommentId);
            return transcript.ToArray();
        }

    }
}
