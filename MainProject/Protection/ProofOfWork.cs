using ChaySocial.MainProject.Cryptography;

namespace ChaySocial.MainProject.Protection
{
    /// <summary> A piece of work the server hands out, which a client must complete before it is allowed to write. </summary>
    /// <param name="ChallengeId"> Identifies this challenge so the server can retire it after one use. </param>
    /// <param name="Salt"> Base64 random bytes that make every challenge a fresh problem, so no answer can be reused or precomputed. </param>
    /// <param name="DifficultyBits"> How many leading zero bits the hash must have. Each extra bit doubles the expected work. </param>
    /// <param name="ExpiresAtUnixMs"> After this moment the server stops accepting answers to it. </param>
    public sealed record ProofChallenge(string ChallengeId, string Salt, int DifficultyBits, long ExpiresAtUnixMs);

    /// <summary> A completed challenge: the counter value whose hash met the difficulty. </summary>
    /// <param name="ChallengeId"> Challenge this answers. </param>
    /// <param name="Nonce"> Counter value the client found. </param>
    public readonly record struct ProofSolution(string ChallengeId, long Nonce);

    /// <summary>
    /// Makes writing cost measurable computer time without asking anyone who they are. A client hashes a counter
    /// against the challenge until the result starts with enough zero bits; the server checks that in a single
    /// hash. Solving is deliberately memory-hard (Argon2id), so a bot farm cannot buy its way out with parallel
    /// hardware nearly as cheaply as with a plain hash, while a person creating one account waits a moment.
    /// It authenticates nobody and identifies nobody, which is why it fits an app where one human may hold many
    /// accounts and none of them are registered.
    /// </summary>
    public static class ProofOfWork
    {
        /// <summary>
        /// Memory each attempt allocates. Measured rather than guessed: at 8 MiB a browser took about 1.6 seconds
        /// per attempt — roughly sixty times a desktop build — which put a single account at minutes of waiting.
        /// 1 MiB keeps an attempt in the fraction of a second a person will sit through while still costing an
        /// attacker real memory bandwidth per account, which a plain hash would not.
        /// </summary>
        public const int AttemptMemoryKibibytes = 1024;

        /// <summary> Passes over that memory per attempt. </summary>
        public const int AttemptIterations = 1;

        /// <summary> Lanes per attempt. </summary>
        public const int AttemptParallelism = 1;

        /// <summary> Bytes of hash produced per attempt; only the leading bits are examined. </summary>
        const int HashSize = 32;

        /// <summary> Separates this hash from every other Argon2id use in the app. </summary>
        static readonly byte[] HashContext = "ChaySocial/ProofOfWork/v1"u8.ToArray();

        /// <summary> The derivation every attempt runs, built once because its parameters never change. </summary>
        static readonly Argon2idKeyDerivation Derivation =
            new(AttemptMemoryKibibytes, AttemptIterations, AttemptParallelism);

        /// <summary>
        /// Searches for a counter whose hash meets the challenge's difficulty. Expected attempts double with each
        /// difficulty bit, so the caller sees progress and can cancel.
        /// </summary>
        /// <param name="challenge"> The challenge to answer. </param>
        /// <param name="onAttempt"> Called with the attempt count as the search runs, for a progress display. </param>
        /// <param name="cancellationToken"> Abandons the search. </param>
        /// <returns> The answer the server will accept. </returns>
        public static ProofSolution Solve(ProofChallenge challenge, Action<long>? onAttempt = null, CancellationToken cancellationToken = default)
        {
            byte[] salt = Convert.FromBase64String(challenge.Salt);

            for (long nonce = 0; ; nonce++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (MeetsDifficulty(Hash(salt, nonce), challenge.DifficultyBits))
                {
                    return new ProofSolution(challenge.ChallengeId, nonce);
                }

                if (nonce % ProgressReportInterval == 0) onAttempt?.Invoke(nonce);
            }
        }

        /// <summary>
        /// Same search as <see cref="Solve"/>, but yielding between attempts so the screen keeps drawing. A browser
        /// runs this on the one thread that also renders, and <c>Task.Run</c> buys no parallelism there, so a
        /// straight loop would freeze the page for the whole search.
        /// </summary>
        /// <param name="challenge"> The challenge to answer. </param>
        /// <param name="onAttempt"> Called with the attempt count as the search runs, for a progress display. </param>
        /// <param name="cancellationToken"> Abandons the search. </param>
        /// <returns> The answer the server will accept. </returns>
        public static async Task<ProofSolution> SolveAsync(ProofChallenge challenge, Action<long>? onAttempt = null, CancellationToken cancellationToken = default)
        {
            byte[] salt = Convert.FromBase64String(challenge.Salt);

            for (long nonce = 0; ; nonce++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (MeetsDifficulty(Hash(salt, nonce), challenge.DifficultyBits))
                {
                    return new ProofSolution(challenge.ChallengeId, nonce);
                }

                onAttempt?.Invoke(nonce + 1);
                await Task.Yield();
            }
        }

        /// <summary> Checks an answer with a single hash, which is what keeps the server cheap to defend. </summary>
        /// <param name="challenge"> The challenge that was handed out. </param>
        /// <param name="solution"> The answer a client sent back. </param>
        /// <returns> True when the counter really produces a hash meeting the difficulty. </returns>
        public static bool Verify(ProofChallenge challenge, ProofSolution solution)
        {
            if (solution.ChallengeId != challenge.ChallengeId || solution.Nonce < 0) return false;

            try
            {
                return MeetsDifficulty(Hash(Convert.FromBase64String(challenge.Salt), solution.Nonce), challenge.DifficultyBits);
            }
            catch (FormatException)
            {
                // A malformed salt can only come from a corrupted or forged challenge; refuse rather than throw.
                return false;
            }
        }

        /// <summary> How often the search reports progress, so the display updates without slowing the search. </summary>
        const int ProgressReportInterval = 8;

        /// <summary> Hashes one counter value against a challenge's salt. </summary>
        /// <param name="salt"> The challenge's random bytes. </param>
        /// <param name="nonce"> Counter value being tried. </param>
        /// <returns> The attempt's hash. </returns>
        static byte[] Hash(byte[] salt, long nonce)
            => Derivation.Derive(BitConverter.GetBytes(nonce), salt, HashContext, HashSize);

        /// <summary> Tests whether a hash starts with the required number of zero bits. </summary>
        /// <param name="hash"> The attempt's hash. </param>
        /// <param name="difficultyBits"> Zero bits required. </param>
        /// <returns> True when the attempt meets the difficulty. </returns>
        static bool MeetsDifficulty(byte[] hash, int difficultyBits)
        {
            int wholeBytes = difficultyBits / 8;
            for (int index = 0; index < wholeBytes; index++)
            {
                if (hash[index] != 0) return false;
            }

            int remainingBits = difficultyBits % 8;
            return remainingBits == 0 || (hash[wholeBytes] >> (8 - remainingBits)) == 0;
        }
    }
}
