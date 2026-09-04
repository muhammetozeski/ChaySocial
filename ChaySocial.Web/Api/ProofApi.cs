using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.Protection;
using ChaySocial.MainProject.Text;

namespace ChaySocial.Web.Api
{
    /// <summary>
    /// Hands out challenges and checks the answers. A challenge is good for one write and only for a few minutes,
    /// so an answer cannot be collected in bulk beforehand or replayed afterwards. Checking costs a single hash,
    /// which is what lets the server defend itself far more cheaply than an attacker can attack.
    /// </summary>
    public sealed class ProofChallengeRegistry
    {
        readonly Dictionary<string, ProofChallenge> _open = [];
        readonly Lock _gate = new();

        /// <summary>
        /// How long a challenge stays answerable. Comfortably longer than the work itself takes, because the work
        /// takes minutes and a slow phone takes several of them: a window that expires mid-search would throw away
        /// exactly the effort it was meant to charge for.
        /// </summary>
        static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(60);

        /// <summary> Random bytes behind a challenge id. </summary>
        const int ChallengeIdBytes = 12;

        /// <summary> Random bytes in a challenge's salt. </summary>
        const int ChallengeSaltBytes = 16;

        /// <summary> Open challenges kept at once; past this the oldest expired ones are swept before adding more. </summary>
        const int SweepThreshold = 500;

        /// <summary> Difficulties a client may ask for, so nobody can request a trivial one. </summary>
        static readonly int[] AllowedDifficulties = [ProofDifficulty.WritingPermit];

        /// <summary> Creates a challenge at one of the allowed difficulties. </summary>
        /// <param name="difficultyBits"> Difficulty the client asked for. </param>
        /// <param name="now"> Current time, used for the expiry stamp. </param>
        /// <returns> The challenge to send back, or null when the difficulty is not one this server issues. </returns>
        public ProofChallenge? Issue(int difficultyBits, DateTimeOffset now)
        {
            if (!AllowedDifficulties.Contains(difficultyBits)) return null;

            ProofChallenge challenge = new(
                Base32.Encode(RandomSource.Next(ChallengeIdBytes)),
                Convert.ToBase64String(RandomSource.Next(ChallengeSaltBytes)),
                difficultyBits,
                now.Add(ChallengeLifetime).ToUnixTimeMilliseconds());

            lock (_gate)
            {
                if (_open.Count >= SweepThreshold) SweepExpired(now);
                _open[challenge.ChallengeId] = challenge;
            }

            return challenge;
        }

        /// <summary>
        /// Checks an answer and retires the challenge, so the same answer cannot pay for a second write.
        /// </summary>
        /// <param name="solution"> The answer a client sent. </param>
        /// <param name="minimumDifficultyBits"> Difficulty this particular write requires. </param>
        /// <param name="now"> Current time, used to reject expired challenges. </param>
        /// <returns> True when the answer was valid, unexpired, unused and hard enough for this write. </returns>
        public bool Redeem(ProofSolution solution, int minimumDifficultyBits, DateTimeOffset now)
        {
            ProofChallenge? challenge;
            lock (_gate)
            {
                if (!_open.Remove(solution.ChallengeId, out challenge)) return false;
            }

            if (challenge.ExpiresAtUnixMs < now.ToUnixTimeMilliseconds()) return false;
            if (challenge.DifficultyBits < minimumDifficultyBits) return false;

            return ProofOfWork.Verify(challenge, solution);
        }

        /// <summary> Drops challenges nobody answered in time. </summary>
        /// <param name="now"> Current time. </param>
        void SweepExpired(DateTimeOffset now)
        {
            long cutoff = now.ToUnixTimeMilliseconds();
            foreach (string challengeId in _open.Where(entry => entry.Value.ExpiresAtUnixMs < cutoff).Select(entry => entry.Key).ToArray())
            {
                _open.Remove(challengeId);
            }
        }
    }

    /// <summary> Publishes the challenge endpoint. </summary>
    public static class ProofApi
    {
        /// <summary> Registers the route that issues challenges. </summary>
        /// <param name="app"> Application to register on. </param>
        public static void MapProofApi(this WebApplication app)
        {
            ProofChallengeRegistry registry = app.Services.GetRequiredService<ProofChallengeRegistry>();

            app.MapGet(ProofRoutes.Challenge, (int difficulty) =>
            {
                ProofChallenge? challenge = registry.Issue(difficulty, DateTimeOffset.UtcNow);
                return challenge is null ? Results.BadRequest() : Results.Json(challenge);
            });
        }
    }
}
