using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Publishing posts and reading the wall. Every post is signed on the author's device before it is stored, so a
    /// reader can tell a real post from one the server invented, and the author's own key — not an account row on a
    /// server — is what proves authorship.
    /// </summary>
    public static class WallService
    {
        /// <summary> Separates this signature's meaning from every other signature the app produces. </summary>
        static readonly byte[] PostSignatureDomain = "ChaySocial/Post/v1"u8.ToArray();

        /// <summary> Posts fetched in one page of the wall. </summary>
        public const int WallPageSize = 30;

        /// <summary> Reads the newest posts across the whole app. </summary>
        /// <param name="limit"> Largest number of posts to return. </param>
        /// <returns> Posts, newest first. </returns>
        public static async Task<IReadOnlyList<PostData>> ReadWallAsync(int limit = WallPageSize)
        {
            DocumentQuery<PostData> query = new DocumentQuery<PostData>()
                .WithSort(PostData.CreatedAtField, descending: true)
                .WithLimit(limit);

            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary> Reads one post by id, for drawing the original inside a quote. </summary>
        /// <param name="postId"> Id of the post to read. </param>
        /// <returns> The post, or null when it no longer exists. </returns>
        public static Task<PostData?> ReadAsync(string postId)
            => postId.Length == 0 ? Task.FromResult<PostData?>(null) : AppServices.Documents.ReadAsync(new DocumentId<PostData>(postId));

        /// <summary> Reads the newest posts written by one account. </summary>
        /// <param name="authorAddress"> Address of the author. </param>
        /// <param name="limit"> Largest number of posts to return. </param>
        /// <returns> That account's posts, newest first. </returns>
        public static async Task<IReadOnlyList<PostData>> ReadAuthorPostsAsync(string authorAddress, int limit = WallPageSize)
        {
            DocumentQuery<PostData> query = new DocumentQuery<PostData>()
                .WithMatch(PostData.AuthorField, authorAddress)
                .WithSort(PostData.CreatedAtField, descending: true)
                .WithLimit(limit);

            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary> Signs a post as the given account and stores it. </summary>
        /// <param name="author"> The unlocked account writing the post. </param>
        /// <param name="text"> What to publish; trimmed, and refused when empty or over <see cref="PostData.MaximumTextLength"/>. </param>
        /// <param name="attachments"> Media already uploaded for this post, or null for a post that is only text. </param>
        /// <param name="quotedPostId"> Post this one quotes, or empty when it quotes nothing. </param>
        /// <returns> The stored post, or null when the post was not publishable. </returns>
        public static async Task<PostData?> PublishAsync(
            PrivateIdentity author,
            string text,
            IReadOnlyList<MediaAttachment>? attachments = null,
            string quotedPostId = "")
        {
            string trimmed = text.Trim();

            // A post has to carry something: words, media, or somebody else's post it is speaking about.
            bool hasMedia = attachments is { Count: > 0 };
            if (trimmed.Length > PostData.MaximumTextLength) return null;
            if (trimmed.Length == 0 && !hasMedia && quotedPostId.Length == 0) return null;
            if (attachments is { Count: > MediaAttachment.MaximumPerPost }) return null;

            string postId = Base32.Encode(RandomSource.Next(PostIdBytes));
            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            IReadOnlyList<MediaAttachment> media = attachments ?? [];

            byte[] transcript = BuildTranscript(postId, author.Public.Address, trimmed, createdAt, string.Empty, media, quotedPostId);

            PostData post = new()
            {
                PostId = postId,
                AuthorAddress = author.Public.Address,
                Text = trimmed,
                CreatedAtUnixMs = createdAt,
                Attachments = media,
                QuotedPostId = quotedPostId,
                Signature = Convert.ToBase64String(author.Sign(transcript))
            };

            await AppServices.Documents.WriteAsync(post.Id, post);
            MainEvents.Trigger(MainEvents.Names.WallChanged);
            return post;
        }

        /// <summary> Removes one of the signed-in account's own posts. </summary>
        /// <param name="post"> Post to remove. </param>
        /// <param name="author"> Account asking for the removal; anything else is ignored. </param>
        public static async Task DeleteAsync(PostData post, PublicIdentity author)
        {
            if (post.AuthorAddress != author.Address) return;

            await AppServices.Documents.DeleteAsync(post.Id);

            // The media goes with the post; leaving the blobs behind would fill the disk with bytes nothing points at.
            foreach (MediaAttachment attachment in post.Attachments)
            {
                await MediaService.RemoveAsync(attachment);
            }

            MainEvents.Trigger(MainEvents.Names.WallChanged);
        }

        /// <summary>
        /// Checks that a post really was written by the account it names, using the signing key published in that
        /// account's profile. A post that fails this was altered or forged after it left its author.
        /// </summary>
        /// <param name="post"> Post to check. </param>
        /// <param name="authorProfile"> Profile of the account the post names, or null when it could not be read. </param>
        /// <returns> True when the signature verifies against the author's published key. </returns>
        public static bool VerifyAuthorship(PostData post, ProfileData? authorProfile)
        {
            if (authorProfile is null || authorProfile.Address != post.AuthorAddress) return false;

            try
            {
                PublicIdentity author = new(
                    authorProfile.Address,
                    Convert.FromBase64String(authorProfile.SigningPublicKey),
                    Convert.FromBase64String(authorProfile.EncryptionPublicKey));

                byte[] transcript = BuildTranscript(
                    post.PostId, post.AuthorAddress, post.Text, post.CreatedAtUnixMs, post.Topic, post.Attachments, post.QuotedPostId);

                return AppCryptography.Identities.Verify(transcript, Convert.FromBase64String(post.Signature), author);
            }
            catch (FormatException error)
            {
                Log($"Post '{post.PostId}' carries malformed base64.\n{error}", LogLevel.Warning);
                return false;
            }
        }

        /// <summary> Turns a like on, or off when it was already on. </summary>
        /// <param name="post"> Post being liked. </param>
        /// <param name="liker"> Account doing the liking. </param>
        /// <returns> True when the post ended up liked. </returns>
        public static async Task<bool> ToggleLikeAsync(PostData post, PublicIdentity liker)
        {
            DocumentId<LikeData> likeId = LikeData.IdFor(post.PostId, liker.Address);

            if (await AppServices.Documents.ReadAsync(likeId) is not null)
            {
                await AppServices.Documents.DeleteAsync(likeId);
                MainEvents.Trigger(MainEvents.Names.WallChanged);
                return false;
            }

            await AppServices.Documents.WriteAsync(likeId, new LikeData
            {
                PostId = post.PostId,
                LikerAddress = liker.Address,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            MainEvents.Trigger(MainEvents.Names.WallChanged);
            return true;
        }

        /// <summary> Reads who liked a post. </summary>
        /// <param name="postId"> Post to count. </param>
        /// <returns> Addresses of the accounts that liked it. </returns>
        public static async Task<IReadOnlyList<string>> ReadLikersAsync(string postId)
        {
            DocumentQuery<LikeData> query = new DocumentQuery<LikeData>()
                .WithMatch(LikeData.PostField, postId)
                .WithLimit(MaximumLikesPerPost);

            return [.. (await AppServices.Documents.QueryAsync(query)).Documents.Select(like => like.LikerAddress)];
        }

        /// <summary>
        /// Carries a post onto the reposter's own wall, or takes it back when it is already there. A post carries
        /// its own author's name wherever it goes, so this stores a pointer rather than a copy.
        /// </summary>
        /// <param name="post"> Post being carried over. </param>
        /// <param name="reposter"> The unlocked account carrying it. </param>
        /// <returns> True when the post ended up on the reposter's wall. </returns>
        public static async Task<bool> ToggleRepostAsync(PostData post, PrivateIdentity reposter)
        {
            DocumentId<RepostData> repostId = RepostData.IdFor(post.PostId, reposter.Public.Address);

            if (await AppServices.Documents.ReadAsync(repostId) is not null)
            {
                await AppServices.Documents.DeleteAsync(repostId);
                MainEvents.Trigger(MainEvents.Names.WallChanged);
                return false;
            }

            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            byte[] transcript = BuildRepostTranscript(post.PostId, reposter.Public.Address, createdAt);

            await AppServices.Documents.WriteAsync(repostId, new RepostData
            {
                PostId = post.PostId,
                ReposterAddress = reposter.Public.Address,
                CreatedAtUnixMs = createdAt,
                Signature = Convert.ToBase64String(reposter.Sign(transcript))
            });

            MainEvents.Trigger(MainEvents.Names.WallChanged);
            return true;
        }

        /// <summary> Reads who carried a post onto their own wall. </summary>
        /// <param name="postId"> Post to count. </param>
        /// <returns> Addresses of the accounts that reposted it. </returns>
        public static async Task<IReadOnlyList<string>> ReadRepostersAsync(string postId)
        {
            DocumentQuery<RepostData> query = new DocumentQuery<RepostData>()
                .WithMatch(RepostData.PostField, postId)
                .WithLimit(MaximumRepostsPerPost);

            return [.. (await AppServices.Documents.QueryAsync(query)).Documents.Select(repost => repost.ReposterAddress)];
        }

        /// <summary> Reads the newest reposts across the whole app, the way <see cref="ReadWallAsync"/> reads posts. </summary>
        /// <param name="limit"> Largest number of reposts to return. </param>
        /// <returns> Reposts, newest first. </returns>
        public static async Task<IReadOnlyList<RepostData>> ReadRecentRepostsAsync(int limit = WallPageSize)
        {
            DocumentQuery<RepostData> query = new DocumentQuery<RepostData>()
                .WithSort(RepostData.CreatedAtField, descending: true)
                .WithLimit(limit);

            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary> Reads what one account has carried onto its own wall, newest first. </summary>
        /// <param name="reposterAddress"> Address of the account. </param>
        /// <param name="limit"> Largest number of reposts to return. </param>
        /// <returns> That account's reposts, newest first. </returns>
        public static async Task<IReadOnlyList<RepostData>> ReadAccountRepostsAsync(string reposterAddress, int limit = WallPageSize)
        {
            DocumentQuery<RepostData> query = new DocumentQuery<RepostData>()
                .WithMatch(RepostData.ReposterField, reposterAddress)
                .WithSort(RepostData.CreatedAtField, descending: true)
                .WithLimit(limit);

            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary>
        /// Checks that a repost really was made by the account it names. A repost that fails this was put on that
        /// account's wall by somebody else.
        /// </summary>
        /// <param name="repost"> Repost to check. </param>
        /// <param name="reposterProfile"> Profile of the account the repost names, or null when it could not be read. </param>
        /// <returns> True when the signature verifies against the reposter's published key. </returns>
        public static bool VerifyReposter(RepostData repost, ProfileData? reposterProfile)
        {
            if (reposterProfile is null || reposterProfile.Address != repost.ReposterAddress) return false;

            try
            {
                PublicIdentity reposter = new(
                    reposterProfile.Address,
                    Convert.FromBase64String(reposterProfile.SigningPublicKey),
                    Convert.FromBase64String(reposterProfile.EncryptionPublicKey));

                byte[] transcript = BuildRepostTranscript(repost.PostId, repost.ReposterAddress, repost.CreatedAtUnixMs);
                return AppCryptography.Identities.Verify(transcript, Convert.FromBase64String(repost.Signature), reposter);
            }
            catch (FormatException error)
            {
                Log($"Repost of '{repost.PostId}' carries malformed base64.\n{error}", LogLevel.Warning);
                return false;
            }
        }

        /// <summary> Random bytes behind a post id — enough that two posts never collide. </summary>
        const int PostIdBytes = 12;

        /// <summary> Largest number of reposts read back for one post. </summary>
        const int MaximumRepostsPerPost = 200;

        /// <summary> Separates a repost's signature from every other signature the app produces. </summary>
        static readonly byte[] RepostSignatureDomain = "ChaySocial/Repost/v1"u8.ToArray();

        /// <summary> Builds the exact bytes a reposter signs and a reader verifies. </summary>
        /// <param name="postId"> Post being carried over. </param>
        /// <param name="reposterAddress"> Address of the account carrying it. </param>
        /// <param name="createdAtUnixMs"> When it was carried over. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildRepostTranscript(string postId, string reposterAddress, long createdAtUnixMs)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(RepostSignatureDomain);
            transcript.WriteText(postId);
            transcript.WriteText(reposterAddress);
            transcript.WriteInt64(createdAtUnixMs);
            return transcript.ToArray();
        }

        /// <summary> Largest number of likes read back for one post. </summary>
        const int MaximumLikesPerPost = 200;

        /// <summary> Builds the exact bytes an author signs and a reader verifies. </summary>
        /// <param name="postId"> The post's id. </param>
        /// <param name="authorAddress"> Address of the author. </param>
        /// <param name="text"> The post's text. </param>
        /// <param name="createdAtUnixMs"> Publication time. </param>
        /// <param name="topic"> The post's topic, empty while there are no categories. </param>
        /// <param name="attachments"> Media hanging off the post; covered by the signature so nobody can swap a picture under it. </param>
        /// <param name="quotedPostId"> Post this one quotes; covered too, so nobody can point a quote at something else. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTranscript(
            string postId,
            string authorAddress,
            string text,
            long createdAtUnixMs,
            string topic,
            IReadOnlyList<MediaAttachment> attachments,
            string quotedPostId)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(PostSignatureDomain);
            transcript.WriteText(postId);
            transcript.WriteText(authorAddress);
            transcript.WriteText(text);
            transcript.WriteInt64(createdAtUnixMs);
            transcript.WriteText(topic);

            transcript.WriteInt64(attachments.Count);
            foreach (MediaAttachment attachment in attachments)
            {
                transcript.WriteText(attachment.BlobId);
                transcript.WriteText(attachment.ContentType);
                transcript.WriteText(attachment.Key);
                transcript.WriteText(attachment.Nonce);
                transcript.WriteInt64(attachment.ByteCount);
            }

            transcript.WriteText(quotedPostId);
            return transcript.ToArray();
        }
    }
}
