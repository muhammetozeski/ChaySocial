namespace ChaySocial.MainProject.Protection
{
    /// <summary>
    /// What the proof-of-work costs. There is exactly one cost in this app and it is paid once: the permit that
    /// lets an account write. Reading, opening an account, following, and pouring somebody a chay are all free and
    /// immediate, because charging a person's phone for every tap punishes the person, not the spammer.
    /// </summary>
    public static class ProofDifficulty
    {
        /// <summary>
        /// The one-off cost of the permit to write. Deliberately minutes rather than seconds: a person pays it once
        /// in their life and can walk away while it runs, whereas a farm wanting a thousand posting accounts pays
        /// it a thousand times over.
        /// </summary>
        /// <remarks>
        /// Measured, not guessed. A desktop browser gets through roughly 1 to 3 attempts a second at
        /// <see cref="ProofOfWork.AttemptMemoryKibibytes"/>, so nine bits — 512 expected attempts — lands at about
        /// three minutes there and proportionally longer on a slow phone. Each extra bit doubles it: eleven bits,
        /// which looked reasonable on paper, measured out at half an hour. How long any one person waits also
        /// varies by luck, because the search stops at the first attempt that works rather than after a fixed
        /// number of them.
        /// </remarks>
        public const int WritingPermit = 9;
    }

    /// <summary> Routes the proof-of-work endpoints answer on, shared so client and server cannot drift apart. </summary>
    public static class ProofRoutes
    {
        /// <summary> Prefix every proof route sits under. </summary>
        public const string Base = "/api/proof";

        /// <summary> Asks for a new challenge; takes the difficulty as a query value. </summary>
        public const string Challenge = Base + "/challenge";

        /// <summary> Where a permit is claimed and where one is looked up. </summary>
        public const string Permit = Base + "/permit";

        /// <summary> Builds the route that reports whether one account may write. </summary>
        /// <param name="address"> Account to ask about. </param>
        /// <returns> The route to request. </returns>
        public static string PermitFor(string address) => $"{Permit}/{address}";

        /// <summary> Name of the query value carrying the requested difficulty. </summary>
        public const string DifficultyQueryName = "difficulty";

        /// <summary> Header a write request carries its answer in. </summary>
        public const string SolutionHeader = "X-Chay-Proof";

        /// <summary> Header a write request names the writing account in, so the server can look up its permit. </summary>
        public const string AccountHeader = "X-Chay-Account";

        /// <summary> Builds the header value from an answer. </summary>
        /// <param name="solution"> The completed challenge. </param>
        /// <returns> The header value, which is the challenge id and the counter joined by a separator. </returns>
        public static string FormatSolution(ProofSolution solution) => $"{solution.ChallengeId}{SolutionSeparator}{solution.Nonce}";

        /// <summary> Reads an answer back out of a header value. </summary>
        /// <param name="headerValue"> Value the client sent. </param>
        /// <param name="solution"> Receives the answer, or the default when the value was malformed. </param>
        /// <returns> True when the value was a well-formed answer. </returns>
        public static bool TryParseSolution(string? headerValue, out ProofSolution solution)
        {
            solution = default;
            if (string.IsNullOrEmpty(headerValue)) return false;

            int separatorIndex = headerValue.LastIndexOf(SolutionSeparator);
            if (separatorIndex <= 0) return false;

            if (!long.TryParse(headerValue[(separatorIndex + 1)..], out long nonce)) return false;

            solution = new ProofSolution(headerValue[..separatorIndex], nonce);
            return true;
        }

        /// <summary> Character between the challenge id and the counter in a header value. </summary>
        const char SolutionSeparator = '.';
    }

    /// <summary> What a client sends to claim a writing permit. </summary>
    /// <param name="Address"> Account the permit is for. </param>
    /// <param name="ChallengeId"> Challenge that was answered. </param>
    /// <param name="Nonce"> Counter value that answered it. </param>
    public sealed record PermitClaim(string Address, string ChallengeId, long Nonce);

    /// <summary> What the server answers when asked whether an account may write. </summary>
    /// <param name="Granted"> True when this account has already paid for its permit. </param>
    public sealed record PermitState(bool Granted);
}
