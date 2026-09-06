using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary> One number under a post, and what it was actually made of. </summary>
    /// <param name="ReadCount"> How many records the store handed back before any of them were checked. </param>
    /// <param name="VerifiedCount"> How many of them carried a signature that checked out. </param>
    /// <param name="CarriesSignatures"> False for a kind of record that has no signature at all to check. </param>
    /// <param name="FailuresLeaveTheCount"> True when a record that fails is actually left out of the number the post shows. </param>
    public readonly record struct CountedLine(
        int ReadCount,
        int VerifiedCount,
        bool CarriesSignatures,
        bool FailuresLeaveTheCount = false)
    {
        /// <summary> How many records were read whose signature did not hold; zero for a kind nobody signs. </summary>
        public int UnverifiedCount => CarriesSignatures ? Math.Max(ReadCount - VerifiedCount, 0) : 0;

        /// <summary> A line for a number that had nothing behind it to read. </summary>
        public static readonly CountedLine Nothing = new(0, 0, false);
    }

    /// <summary> Everything the numbers under one post were counted from. </summary>
    /// <param name="Chays"> The likes. </param>
    /// <param name="PassedOn"> The reposts. </param>
    /// <param name="Replies"> The comments. </param>
    /// <param name="Answers"> The poll votes, empty when the post is not asking anything. </param>
    public sealed record PostCountReceipt(
        CountedLine Chays,
        CountedLine PassedOn,
        CountedLine Replies,
        CountedLine Answers);

    /// <summary>
    /// Counts one post's numbers again, out loud: how many records were read, how many of them carried a
    /// signature that held, and which of them carry no signature at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every number under a post looks the same weight on screen and they are not. An answer to a question is
    /// signed by the account that gave it. A repost is signed by the account that carried it. A chay is not signed
    /// by anybody — the record says who liked what, and nothing in it proves they did. That difference is the
    /// reader's to know, not the app's to smooth over.
    /// </para>
    /// <para>
    /// The unverified count is the interesting one, and so is what happens to it. The tally already throws away
    /// answers whose signature does not check out, quietly. A reply or a repost that fails is counted anyway — the
    /// badge on a card is a count of records, not of proofs — and this receipt is the only place a reader finds
    /// that out. Both facts are said on the line rather than smoothed into one word.
    /// </para>
    /// </remarks>
    public static class CountReceipt
    {
        /// <summary> Replies read while making a receipt, matching what the thread itself reads. </summary>
        const int RepliesCounted = CommentService.ThreadPageSize;

        /// <summary> Profiles checked at once, so a post with many reposters does not open one request per record. </summary>
        const int ProfilesReadAtOnce = 8;

        /// <summary> Reads one post's numbers and what each of them was counted from. </summary>
        /// <param name="post"> The post. </param>
        /// <param name="readerAddress"> The reader, which the tally needs to know what they answered. </param>
        /// <returns> The receipt. </returns>
        public static async Task<PostCountReceipt> ReadAsync(PostData post, string readerAddress)
        {
            Task<CountedLine> chaysRead = ReadChaysAsync(post);
            Task<CountedLine> passedOnRead = ReadPassedOnAsync(post);
            Task<CountedLine> repliesRead = ReadRepliesAsync(post);
            Task<CountedLine> answersRead = ReadAnswersAsync(post, readerAddress);

            await Task.WhenAll(chaysRead, passedOnRead, repliesRead, answersRead);

            return new PostCountReceipt(
                await chaysRead,
                await passedOnRead,
                await repliesRead,
                await answersRead);
        }

        /// <summary> Counts the likes, which carry no signature to check. </summary>
        /// <param name="post"> The post. </param>
        /// <returns> The line for the chay count. </returns>
        static async Task<CountedLine> ReadChaysAsync(PostData post)
        {
            IReadOnlyList<string> likers = await WallService.ReadLikersAsync(post.PostId);

            return new CountedLine(likers.Count, 0, CarriesSignatures: false);
        }

        /// <summary> Counts the reposts and checks each one against the profile it names. </summary>
        /// <param name="post"> The post. </param>
        /// <returns> The line for the passed-on count. </returns>
        static async Task<CountedLine> ReadPassedOnAsync(PostData post)
        {
            IReadOnlyList<RepostData> records = await WallService.ReadRepostRecordsAsync(post.PostId);
            if (records.Count == 0) return new CountedLine(0, 0, CarriesSignatures: true);

            IReadOnlyDictionary<string, ProfileData?> profiles =
                await ReadProfilesAsync([.. records.Select(record => record.ReposterAddress)]);

            int verified = records.Count(record =>
                WallService.VerifyReposter(record, profiles.GetValueOrDefault(record.ReposterAddress)));

            // The number on the card counts records, not proofs: a repost whose signature fails is counted there
            // all the same, which is exactly the thing this receipt exists to say out loud.
            return new CountedLine(records.Count, verified, CarriesSignatures: true, FailuresLeaveTheCount: false);
        }

        /// <summary> Counts the replies and checks each one against the profile it names. </summary>
        /// <param name="post"> The post. </param>
        /// <returns> The line for the reply count. </returns>
        static async Task<CountedLine> ReadRepliesAsync(PostData post)
        {
            IReadOnlyList<CommentData> replies = await CommentService.ReadForPostAsync(post.PostId, RepliesCounted);
            if (replies.Count == 0) return new CountedLine(0, 0, CarriesSignatures: true);

            IReadOnlyDictionary<string, ProfileData?> profiles =
                await ReadProfilesAsync([.. replies.Select(reply => reply.AuthorAddress)]);

            int verified = replies.Count(reply =>
                CommentService.VerifyAuthorship(reply, profiles.GetValueOrDefault(reply.AuthorAddress)));

            // Same as the reposts: a reply whose signature fails is still drawn and still counted.
            return new CountedLine(replies.Count, verified, CarriesSignatures: true, FailuresLeaveTheCount: false);
        }

        /// <summary> Counts the answers to a question, or nothing when the post is not asking one. </summary>
        /// <param name="post"> The post. </param>
        /// <param name="readerAddress"> The reader. </param>
        /// <returns> The line for the answer count. </returns>
        static async Task<CountedLine> ReadAnswersAsync(PostData post, string readerAddress)
        {
            if (!post.IsAsking) return CountedLine.Nothing;

            PollTally tally = await PollService.ReadTallyAsync(post, readerAddress);

            // The one number in the app that really does throw away what it cannot verify.
            return new CountedLine(tally.ReadCount, tally.Total, CarriesSignatures: true, FailuresLeaveTheCount: true);
        }

        /// <summary>
        /// Reads one profile per distinct address, in batches, so five records from one account cost one read.
        /// </summary>
        /// <param name="addresses"> Every address named by the records being checked. </param>
        /// <returns> Each profile keyed by address; a value is null when that account published none. </returns>
        static async Task<IReadOnlyDictionary<string, ProfileData?>> ReadProfilesAsync(IReadOnlyList<string> addresses)
        {
            string[] distinct = [.. addresses.Distinct(StringComparer.Ordinal)];
            Dictionary<string, ProfileData?> byAddress = new(distinct.Length, StringComparer.Ordinal);

            for (int start = 0; start < distinct.Length; start += ProfilesReadAtOnce)
            {
                string[] batch = [.. distinct.Skip(start).Take(ProfilesReadAtOnce)];
                ProfileData?[] read = await Task.WhenAll(
                    batch.Select(address => AppServices.Documents.ReadAsync(new DocumentId<ProfileData>(address))));

                for (int index = 0; index < batch.Length; index++)
                {
                    byAddress[batch[index]] = read[index];
                }
            }

            return byAddress;
        }
    }
}
