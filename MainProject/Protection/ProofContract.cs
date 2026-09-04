namespace ChaySocial.MainProject.Protection
{
    /// <summary>
    /// What the proof-of-work costs, per kind of write. Measured on a desktop at roughly 24 ms per attempt, and a
    /// browser runs a few times slower, so these are chosen for what a person actually waits: an account is a
    /// once-per-lifetime cost worth seconds, while a post's proof is small and prepared in the background before
    /// the writer has finished typing.
    /// </summary>
    public static class ProofDifficulty
    {
        /// <summary> Creating an account. Roughly 32 attempts, which lands at a few seconds in a browser. </summary>
        public const int Account = 5;

        /// <summary> Publishing a post, comment or message. Roughly 8 attempts, prepared while the writer is still typing. </summary>
        public const int Write = 3;
    }

    /// <summary> Routes the proof-of-work endpoints answer on, shared so client and server cannot drift apart. </summary>
    public static class ProofRoutes
    {
        /// <summary> Prefix every proof route sits under. </summary>
        public const string Base = "/api/proof";

        /// <summary> Asks for a new challenge; takes the difficulty as a query value. </summary>
        public const string Challenge = Base + "/challenge";

        /// <summary> Name of the query value carrying the requested difficulty. </summary>
        public const string DifficultyQueryName = "difficulty";

        /// <summary> Header a write request carries its answer in. </summary>
        public const string SolutionHeader = "X-Chay-Proof";

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
}
