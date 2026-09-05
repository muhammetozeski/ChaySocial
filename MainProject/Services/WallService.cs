using System.Globalization;
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
            // Group posts are read a page at a time along with everything else and dropped here rather than being
            // excluded by the query, because the store matches on equality and "written nowhere in particular" is
            // an empty value some stores will not match on. Reading extra covers what falls away.
            DocumentQuery<PostData> query = new DocumentQuery<PostData>()
                .WithSort(PostData.CreatedAtField, descending: true)
                .WithLimit(limit * GroupPostReadMultiplier);

            return [.. (await AppServices.Documents.QueryAsync(query)).Documents.Where(post => !post.IsInGroup).Take(limit)];
        }

        /// <summary> Reads one group's wall, which is the only place its posts appear. </summary>
        /// <param name="groupAddress"> The group whose posts are wanted. </param>
        /// <param name="limit"> Largest number of posts to return. </param>
        /// <returns> That group's posts, newest first. </returns>
        public static async Task<IReadOnlyList<PostData>> ReadGroupPostsAsync(string groupAddress, int limit = WallPageSize)
        {
            if (groupAddress.Length == 0 || limit <= 0) return [];

            DocumentQuery<PostData> query = new DocumentQuery<PostData>()
                .WithMatch(PostData.GroupField, groupAddress)
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
            // What somebody said inside a group belongs to that group, so it stays off their public wall.
            DocumentQuery<PostData> query = new DocumentQuery<PostData>()
                .WithMatch(PostData.AuthorField, authorAddress)
                .WithSort(PostData.CreatedAtField, descending: true)
                .WithLimit(limit * GroupPostReadMultiplier);

            return [.. (await AppServices.Documents.QueryAsync(query)).Documents.Where(post => !post.IsInGroup).Take(limit)];
        }

        /// <summary> Signs a post as the given account and stores it. </summary>
        /// <param name="author"> The unlocked account writing the post. </param>
        /// <param name="text"> What to publish; trimmed, and refused when empty or over <see cref="PostData.MaximumTextLength"/>. </param>
        /// <param name="attachments"> Media already uploaded for this post, or null for a post that is only text. </param>
        /// <param name="quotedPostId"> Post this one quotes, or empty when it quotes nothing. </param>
        /// <param name="groupAddress"> Group to write it in, or empty to write it on the wall. </param>
        /// <param name="pollChoices"> Answers to offer, or null when the post is not asking anything. </param>
        /// <param name="pollClosesAtUnixMs"> When the asking closes, or zero to leave it open. </param>
        /// <returns> The stored post, or null when the post was not publishable. </returns>
        public static async Task<PostData?> PublishAsync(
            PrivateIdentity author,
            string text,
            IReadOnlyList<MediaAttachment>? attachments = null,
            string quotedPostId = "",
            string groupAddress = "",
            IReadOnlyList<string>? pollChoices = null,
            long pollClosesAtUnixMs = 0)
        {
            string trimmed = text.Trim();

            // Blank answers are dropped rather than refused: a composer that offers four boxes should not punish
            // somebody for filling in two of them.
            IReadOnlyList<string> choices = pollChoices is null
                ? []
                : [.. pollChoices.Select(choice => choice.Trim()).Where(choice => choice.Length > 0)];

            // A post has to carry something: words, media, somebody else's post it is speaking about, or a question.
            bool hasMedia = attachments is { Count: > 0 };
            bool hasPoll = choices.Count > 0;
            if (trimmed.Length > PostData.MaximumTextLength) return null;
            if (trimmed.Length == 0 && !hasMedia && quotedPostId.Length == 0 && !hasPoll) return null;
            if (attachments is { Count: > MediaAttachment.MaximumPerPost }) return null;

            // One answer is not a question, and a choice long enough to be a post of its own stops being a label.
            if (hasPoll && choices.Count < PostData.LeastPollChoiceCount) return null;
            if (choices.Count > PostData.MaximumPollChoiceCount) return null;
            if (choices.Any(choice => choice.Length > PostData.MaximumPollChoiceLength)) return null;

            string postId = Base32.Encode(RandomSource.Next(PostIdBytes));
            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            IReadOnlyList<MediaAttachment> media = attachments ?? [];

            // Settled before signing, not after: a closing time signed here but dropped from the stored record would
            // leave a post whose own signature does not verify.
            long closesAt = hasPoll ? pollClosesAtUnixMs : 0;

            byte[] transcript = BuildTranscript(
                postId, author.Public.Address, trimmed, createdAt, string.Empty, media, quotedPostId, groupAddress,
                choices, closesAt);

            PostData post = new()
            {
                PostId = postId,
                AuthorAddress = author.Public.Address,
                Text = trimmed,
                CreatedAtUnixMs = createdAt,
                Attachments = media,
                QuotedPostId = quotedPostId,
                GroupAddress = groupAddress,
                PollChoices = choices,
                PollClosesAtUnixMs = closesAt,
                Signature = Convert.ToBase64String(author.Sign(transcript))
            };

            await AppServices.Documents.WriteAsync(post.Id, post);

            // A subject named inside a group belongs to that group's wall, not to a listing the whole app reads.
            if (!post.IsInGroup) await IndexSubjectsAsync(post);

            await NotificationService.NotifyMentionedAsync(trimmed, author.Public.Address, postId, trimmed);

            MainEvents.Trigger(post.IsInGroup ? MainEvents.Names.GroupsChanged : MainEvents.Names.WallChanged, groupAddress);
            return post;
        }

        /// <summary>
        /// Notes which subjects a post named, so a subject's page can be read without searching every post's text.
        /// A failure here loses a post from one listing, never the post itself, which is why it is logged rather
        /// than thrown: the words are already stored and safe.
        /// </summary>
        /// <param name="post"> The post that was just published. </param>
        /// <returns> A task that completes once every subject it names has been noted. </returns>
        static async Task IndexSubjectsAsync(PostData post)
        {
            foreach (string subject in WrittenText.SubjectsIn(post.Text))
            {
                try
                {
                    await AppServices.Documents.WriteAsync(
                        SubjectMentionData.IdFor(subject, post.PostId),
                        new SubjectMentionData
                        {
                            Subject = subject,
                            PostId = post.PostId,
                            CreatedAtUnixMs = post.CreatedAtUnixMs
                        });
                }
                catch (Exception error)
                {
                    Log($"Post '{post.PostId}' could not be listed under '{subject}'.\n{error}", LogLevel.Warning);
                }
            }
        }

        /// <summary>
        /// Reads the posts written under one subject. Every post is checked against its own text before it is
        /// returned, so an index entry nobody wrote cannot put a post in front of a subject it never named.
        /// </summary>
        /// <param name="subject"> Subject to read, in any casing. </param>
        /// <param name="limit"> Largest number of posts to return. </param>
        /// <returns> Posts naming that subject, newest first. </returns>
        public static async Task<IReadOnlyList<PostData>> ReadSubjectAsync(string subject, int limit = WallPageSize)
        {
            string wanted = WrittenText.NormaliseSubject(subject.Trim());
            if (wanted.Length == 0 || limit <= 0) return [];

            DocumentQuery<SubjectMentionData> query = new DocumentQuery<SubjectMentionData>()
                .WithMatch(SubjectMentionData.SubjectField, wanted)
                .WithSort(SubjectMentionData.CreatedAtField, descending: true)
                .WithLimit(limit);

            IReadOnlyList<SubjectMentionData> mentions = (await AppServices.Documents.QueryAsync(query)).Documents;
            if (mentions.Count == 0) return [];

            PostData?[] posts = await Task.WhenAll(mentions.Select(mention => ReadAsync(mention.PostId)));

            return
            [
                .. posts
                    .Where(post => post is not null && WrittenText.SubjectsIn(post.Text).Contains(wanted))
                    .Select(post => post!)
                    .OrderByDescending(post => post.CreatedAtUnixMs)
            ];
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

            // So do the subject listings. A reader checks a post's own text before drawing it under a subject, so a
            // note left behind would show nothing — but it would still be a row on the disk pointing at nothing.
            foreach (string subject in WrittenText.SubjectsIn(post.Text))
            {
                await AppServices.Documents.DeleteAsync(SubjectMentionData.IdFor(subject, post.PostId));
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
                    post.PostId, post.AuthorAddress, post.Text, post.CreatedAtUnixMs, post.Topic,
                    post.Attachments, post.QuotedPostId, post.GroupAddress, post.PollChoices, post.PollClosesAtUnixMs);

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

        /// <summary>
        /// How many times the requested page a wall reads before group posts are dropped from it. Reading extra is
        /// what keeps a page full when somebody the reader follows has been busy inside a group.
        /// </summary>
        const int GroupPostReadMultiplier = 3;

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
        /// <param name="groupAddress"> Group the post was written in; covered so nobody can move a post into a group, or out of one. </param>
        /// <param name="pollChoices"> Answers the post offers; covered so nobody can add, remove or reword a choice under a signature. </param>
        /// <param name="pollClosesAtUnixMs"> When the asking closes; covered too, or the closing time could be moved under a valid signature. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTranscript(
            string postId,
            string authorAddress,
            string text,
            long createdAtUnixMs,
            string topic,
            IReadOnlyList<MediaAttachment> attachments,
            string quotedPostId,
            string groupAddress,
            IReadOnlyList<string> pollChoices,
            long pollClosesAtUnixMs)
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

            // Named rather than positional, so the next field a post grows leaves every signature written before it
            // untouched. See TranscriptWriter.WriteNamedText.
            transcript.WriteNamedText(nameof(PostData.QuotedPostId), quotedPostId);
            transcript.WriteNamedText(nameof(PostData.GroupAddress), groupAddress);

            // Named for the same reason, and text rather than a number: WriteInt64 always writes its eight bytes,
            // so writing a closing time of zero would change the transcript of every post already signed.
            foreach (string choice in pollChoices) transcript.WriteNamedText(nameof(PostData.PollChoices), choice);
            transcript.WriteNamedText(
                nameof(PostData.PollClosesAtUnixMs),
                pollClosesAtUnixMs == 0 ? string.Empty : pollClosesAtUnixMs.ToString(CultureInfo.InvariantCulture));

            return transcript.ToArray();
        }
    }
}
