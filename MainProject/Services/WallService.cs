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
        /// <returns> The stored post, or null when the post was not publishable. </returns>
        public static async Task<PostData?> PublishAsync(PrivateIdentity author, string text, IReadOnlyList<MediaAttachment>? attachments = null)
        {
            string trimmed = text.Trim();

            // A post has to say something: either words or media. Text alone is capped; media alone is fine.
            bool hasMedia = attachments is { Count: > 0 };
            if (trimmed.Length > PostData.MaximumTextLength) return null;
            if (trimmed.Length == 0 && !hasMedia) return null;
            if (attachments is { Count: > MediaAttachment.MaximumPerPost }) return null;

            string postId = Base32.Encode(RandomSource.Next(PostIdBytes));
            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            IReadOnlyList<MediaAttachment> media = attachments ?? [];

            byte[] transcript = BuildTranscript(postId, author.Public.Address, trimmed, createdAt, string.Empty, media);

            PostData post = new()
            {
                PostId = postId,
                AuthorAddress = author.Public.Address,
                Text = trimmed,
                CreatedAtUnixMs = createdAt,
                Attachments = media,
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
                    post.PostId, post.AuthorAddress, post.Text, post.CreatedAtUnixMs, post.Topic, post.Attachments);

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

        /// <summary> Random bytes behind a post id — enough that two posts never collide. </summary>
        const int PostIdBytes = 12;

        /// <summary> Largest number of likes read back for one post. </summary>
        const int MaximumLikesPerPost = 200;

        /// <summary> Builds the exact bytes an author signs and a reader verifies. </summary>
        /// <param name="postId"> The post's id. </param>
        /// <param name="authorAddress"> Address of the author. </param>
        /// <param name="text"> The post's text. </param>
        /// <param name="createdAtUnixMs"> Publication time. </param>
        /// <param name="topic"> The post's topic, empty while there are no categories. </param>
        /// <param name="attachments"> Media hanging off the post; covered by the signature so nobody can swap a picture under it. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTranscript(
            string postId,
            string authorAddress,
            string text,
            long createdAtUnixMs,
            string topic,
            IReadOnlyList<MediaAttachment> attachments)
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

            return transcript.ToArray();
        }
    }
}
