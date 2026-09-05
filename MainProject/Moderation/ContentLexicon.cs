namespace ChaySocial.MainProject.Moderation
{
    /// <summary> One thing worth recognising in a line, and what recognising it is worth. </summary>
    /// <param name="Term"> The term in normalised form: lowercase, plain letters, single spaces. </param>
    /// <param name="Category"> The kind of harm it carries. </param>
    /// <param name="Weight"> How much it weighs towards a band. </param>
    public readonly record struct ContentTerm(string Term, ContentCategory Category, int Weight);

    /// <summary>
    /// The terms this application recognises, and the marks that show a line is aimed at a person rather than said
    /// in the air.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This list is deliberately small. It is a seed, not an attempt at completeness: a long list of slurs written
    /// into a public repository ages badly, reads as the thing it is trying to prevent, and gives a false impression
    /// that a filter is finished when the interesting part is everything around the list — folding a word back to how
    /// it reads, noticing whether it was aimed at somebody, and leaving the decision with the reader.
    /// </para>
    /// <para>
    /// <see cref="ContentCategory.Slur"/> ships empty on purpose. Whoever runs a server decides what belongs there,
    /// because that answer differs by language and community and is not one this code should make for them.
    /// </para>
    /// </remarks>
    public static class ContentLexicon
    {
        /// <summary> Weight of a word that is rude but aimed at nothing. </summary>
        const int CoarseWeight = 1;

        /// <summary> Weight of a word whose whole purpose is to wound the person it is aimed at. </summary>
        const int InsultWeight = 3;

        /// <summary> Weight of explicit sexual wording. </summary>
        const int SexualWeight = 3;

        /// <summary> Weight of a threat, the heaviest thing here: one on its own is enough to reach the top band. </summary>
        const int ThreatWeight = 6;

        /// <summary> Terms made of one word, looked up whole so a short one never matches inside a longer word. </summary>
        public static readonly IReadOnlyList<ContentTerm> SingleWords =
        [
            new("fuck", ContentCategory.Insult, CoarseWeight),
            new("fucking", ContentCategory.Insult, CoarseWeight),
            new("shit", ContentCategory.Insult, CoarseWeight),
            new("crap", ContentCategory.Insult, CoarseWeight),
            new("damn", ContentCategory.Insult, CoarseWeight),
            new("piss", ContentCategory.Insult, CoarseWeight),

            new("idiot", ContentCategory.Insult, InsultWeight),
            new("moron", ContentCategory.Insult, InsultWeight),
            new("imbecile", ContentCategory.Insult, InsultWeight),
            new("loser", ContentCategory.Insult, InsultWeight),
            new("pathetic", ContentCategory.Insult, InsultWeight),
            new("worthless", ContentCategory.Insult, InsultWeight),
            new("scum", ContentCategory.Insult, InsultWeight),
            new("bastard", ContentCategory.Insult, InsultWeight),
            new("bitch", ContentCategory.Insult, InsultWeight),
            new("asshole", ContentCategory.Insult, InsultWeight),
            new("dumbass", ContentCategory.Insult, InsultWeight),

            new("porn", ContentCategory.Sexual, SexualWeight),
            new("nudes", ContentCategory.Sexual, SexualWeight),

            new("kys", ContentCategory.Threat, ThreatWeight),
        ];

        /// <summary>
        /// Terms made of several words. A threat is usually a sentence rather than a word, and the words in it are
        /// harmless one at a time — which is exactly why they have to be recognised together.
        /// </summary>
        public static readonly IReadOnlyList<ContentTerm> Phrases =
        [
            new("kill yourself", ContentCategory.Threat, ThreatWeight),
            new("kill you", ContentCategory.Threat, ThreatWeight),
            new("hurt you", ContentCategory.Threat, ThreatWeight),
            new("find you", ContentCategory.Threat, ThreatWeight),
            new("beat you", ContentCategory.Threat, ThreatWeight),
            new("end your life", ContentCategory.Threat, ThreatWeight),
            new("you should die", ContentCategory.Threat, ThreatWeight),
            new("shut up", ContentCategory.Insult, CoarseWeight),
        ];

        /// <summary>
        /// Words that show a line is pointed at a person. The same word is a very different thing said about the
        /// weather and said to somebody's face, and this is the cheapest honest way to tell the two apart.
        /// </summary>
        public static readonly IReadOnlyList<string> SecondPersonMarks =
        [
            "you", "your", "youre", "yourself", "yours", "u", "ur", "urself"
        ];
    }
}
