using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Answering a question somebody asked in a post, and counting the answers.
    /// </summary>
    /// <remarks>
    /// A tally is a claim about what a group of people said, and everywhere else on the internet that claim is
    /// simply asserted by whoever runs the server. Here every answer carries its own signature and the count is
    /// recomputed on the reader's own machine, dropping anything that does not verify — so the number is not the
    /// server's word, it is something the reader worked out.
    /// </remarks>
    public static class PollService
    {
        /// <summary> Separates this signature's meaning from every other signature the app produces. </summary>
        static readonly byte[] PollVoteSignatureDomain = "ChaySocial/PollVote/v1"u8.ToArray();

        /// <summary> Most answers read back for one question. </summary>
        const int MostVotesPerPoll = 2000;

        /// <summary>
        /// Records one account's answer, replacing whatever it answered before.
        /// </summary>
        /// <param name="post"> The post being answered. </param>
        /// <param name="voter"> The unlocked account answering. </param>
        /// <param name="choiceIndex"> Which of the post's choices was picked, counted from zero. </param>
        /// <returns> True when the answer was stored. </returns>
        public static async Task<bool> CastVoteAsync(PostData post, PrivateIdentity voter, int choiceIndex)
        {
            if (!post.IsAsking) return false;
            if (choiceIndex < 0 || choiceIndex >= post.PollChoices.Count) return false;
            if (HasClosed(post)) return false;

            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            byte[] transcript = BuildVoteTranscript(post.PostId, voter.Public.Address, choiceIndex, createdAt);

            // The id is the post and the voter together, so changing your mind overwrites rather than counting twice.
            await AppServices.Documents.WriteAsync(PollVoteData.IdFor(post.PostId, voter.Public.Address), new PollVoteData
            {
                PostId = post.PostId,
                VoterAddress = voter.Public.Address,
                ChoiceIndex = choiceIndex,
                CreatedAtUnixMs = createdAt,
                Signature = Convert.ToBase64String(voter.Sign(transcript))
            });

            MainEvents.Trigger(MainEvents.Names.WallChanged, post.PostId);
            return true;
        }

        /// <summary>
        /// Counts the answers to one question, keeping only those that prove who gave them.
        /// </summary>
        /// <param name="post"> The post being counted. </param>
        /// <param name="readerAddress"> The reader's own address, so they can be shown what they answered. </param>
        /// <returns> The tally. </returns>
        public static async Task<PollTally> ReadTallyAsync(PostData post, string readerAddress)
        {
            if (!post.IsAsking) return PollTally.Empty;

            DocumentQuery<PollVoteData> query = new DocumentQuery<PollVoteData>()
                .WithMatch(PollVoteData.PostField, post.PostId)
                .WithLimit(MostVotesPerPoll);

            IReadOnlyList<PollVoteData> votes = (await AppServices.Documents.QueryAsync(query)).Documents;

            int[] counts = new int[post.PollChoices.Count];
            int total = 0;
            int mine = PollTally.NotAnswered;

            foreach (PollVoteData vote in votes)
            {
                if (vote.ChoiceIndex < 0 || vote.ChoiceIndex >= counts.Length) continue;

                // Read fresh rather than trusted: a vote whose signature does not check out against the profile its
                // address publishes is somebody's invention, and inventions must not move the number.
                ProfileData? voterProfile = await AppServices.Documents.ReadAsync(new DocumentId<ProfileData>(vote.VoterAddress));
                if (!VerifyVoter(vote, voterProfile)) continue;

                counts[vote.ChoiceIndex]++;
                total++;

                if (vote.VoterAddress == readerAddress) mine = vote.ChoiceIndex;
            }

            return new PollTally(counts, total, mine, HasClosed(post), votes.Count);
        }

        /// <summary>
        /// Checks that an answer really was given by the account it names, using the signing key that account
        /// publishes. Its address commits to that key, so the chain from address to answer cannot be broken by
        /// whoever holds the documents.
        /// </summary>
        /// <param name="vote"> The answer as it was stored. </param>
        /// <param name="voterProfile"> The profile the voter's address publishes, or null when there is none. </param>
        /// <returns> True when the signature verifies. </returns>
        public static bool VerifyVoter(PollVoteData vote, ProfileData? voterProfile)
        {
            if (voterProfile is null || voterProfile.SigningPublicKey.Length == 0) return false;

            try
            {
                PublicIdentity voter = new(
                    vote.VoterAddress,
                    Convert.FromBase64String(voterProfile.SigningPublicKey),
                    Convert.FromBase64String(voterProfile.EncryptionPublicKey));

                if (!AppCryptography.Addresses.Matches(vote.VoterAddress, voter.SigningPublicKey, voter.EncryptionPublicKey)) return false;

                byte[] transcript = BuildVoteTranscript(vote.PostId, vote.VoterAddress, vote.ChoiceIndex, vote.CreatedAtUnixMs);
                return AppCryptography.Identities.Verify(transcript, Convert.FromBase64String(vote.Signature), voter);
            }
            catch (FormatException error)
            {
                Log($"Vote on '{vote.PostId}' carries malformed base64.\n{error}", LogLevel.Warning);
                return false;
            }
        }

        /// <summary> True when the asking has closed and no further answers count. </summary>
        /// <param name="post"> The post being asked. </param>
        /// <returns> True when it is closed. </returns>
        public static bool HasClosed(PostData post)
            => post.PollClosesAtUnixMs > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= post.PollClosesAtUnixMs;

        /// <summary> Builds the exact bytes a voter signs and a reader verifies. </summary>
        /// <param name="postId"> The post being answered. </param>
        /// <param name="voterAddress"> Address of the account answering. </param>
        /// <param name="choiceIndex"> Which choice was picked. </param>
        /// <param name="createdAtUnixMs"> When the answer was given. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildVoteTranscript(string postId, string voterAddress, int choiceIndex, long createdAtUnixMs)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(PollVoteSignatureDomain);
            transcript.WriteText(postId);
            transcript.WriteText(voterAddress);
            transcript.WriteInt64(choiceIndex);
            transcript.WriteInt64(createdAtUnixMs);
            return transcript.ToArray();
        }
    }

    /// <summary> What the answers to one question add up to, after the ones that do not verify have been dropped. </summary>
    /// <param name="Counts"> How many answers each choice got, in the post's own order. </param>
    /// <param name="Total"> How many answers counted in all. </param>
    /// <param name="ReaderChoice"> Which choice this reader picked, or <see cref="NotAnswered"/> when they have not. </param>
    /// <param name="IsClosed"> True when the asking has closed. </param>
    /// <param name="ReadCount"> How many answer records were read before any of them were checked. </param>
    public readonly record struct PollTally(
        IReadOnlyList<int> Counts,
        int Total,
        int ReaderChoice,
        bool IsClosed,
        int ReadCount = 0)
    {
        /// <summary> How many of the records read were thrown away because their signature did not check out. </summary>
        public int DroppedCount => Math.Max(ReadCount - Total, 0);

        /// <summary> Stands for a reader who has not answered. </summary>
        public const int NotAnswered = -1;

        /// <summary> What all of the answers are worth on the scale shares are reported in. </summary>
        const int WholeShareAsPercent = 100;

        /// <summary> A question nobody has answered. </summary>
        public static readonly PollTally Empty = new([], 0, NotAnswered, false);

        /// <summary> True when this reader has already answered. </summary>
        public bool HasAnswered => ReaderChoice != NotAnswered;

        /// <summary> True when the choices should be shown as results rather than as buttons. </summary>
        public bool ShowsResults => HasAnswered || IsClosed;

        /// <summary> What share of the answers one choice took, as a percentage of the whole. </summary>
        /// <param name="choiceIndex"> Which choice. </param>
        /// <returns> Its share, rounded, or zero when nothing has been answered yet. </returns>
        public int ShareOf(int choiceIndex)
        {
            if (Total == 0 || choiceIndex < 0 || choiceIndex >= Counts.Count) return 0;

            return (int)Math.Round(Counts[choiceIndex] * (double)WholeShareAsPercent / Total);
        }
    }
}
