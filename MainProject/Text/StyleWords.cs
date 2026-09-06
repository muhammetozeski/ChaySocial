namespace ChaySocial.MainProject.Text
{
    /// <summary>
    /// The few words a fingerprint counts one at a time. They are the ones nobody chooses on purpose: a writer
    /// picks their subject, but the rate at which "the" and "but" fall out of them is a habit, and a habit follows
    /// somebody from one account to another.
    /// </summary>
    /// <remarks>
    /// This list is English, and it is the one part of a fingerprint that is. Writing in another language leaves
    /// every one of these axes at zero on both sides of a comparison, which makes two such texts look more alike
    /// than they are; the axes that measure sentence length, word length, punctuation, capitals and emoji carry
    /// the reading in that case. The list is deliberately short — a longer one would drown the axes that work in
    /// every language.
    /// </remarks>
    public static class StyleWords
    {
        /// <summary> The words counted separately, lowercase; matching ignores case. </summary>
        public static readonly IReadOnlyList<string> Commonest =
        [
            "the",
            "and",
            "to",
            "of",
            "a",
            "in",
            "it",
            "is",
            "that",
            "you",
            "for",
            "but"
        ];
    }
}
