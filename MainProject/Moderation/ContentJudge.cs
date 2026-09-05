using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Moderation
{
    /// <summary>
    /// Reads a line and says what it is. Everything here runs on the device holding the text — the reader's while a
    /// post is being drawn, the writer's while it is being typed — and nothing it produces is sent anywhere. The
    /// server gains no ability it did not already have, which is the whole reason this is allowed to exist in an
    /// application whose premise is that no one is in charge of what anybody says.
    /// </summary>
    public static class ContentJudge
    {
        /// <summary> Score at which a line stops being ordinary. </summary>
        const int CoarseScore = 1;

        /// <summary> Score at which a line is aimed at somebody and meant to wound. </summary>
        const int AbusiveScore = 4;

        /// <summary> Score at which a line reaches the band covered even for a reader who never opens Settings. </summary>
        const int ExtremeScore = 9;

        /// <summary>
        /// What being aimed at a person is worth. The same word is one thing said about the weather and another said
        /// to somebody's face, and a filter that cannot tell them apart annoys everybody to catch nobody.
        /// </summary>
        const int DirectedAtSomebodyMultiplier = 2;

        /// <summary>
        /// Shortest term worth hunting for in the squeezed form. Short terms turn up inside longer words once the
        /// spaces are gone, so only terms long enough to be unlikely by accident are looked for there.
        /// </summary>
        const int ShortestSqueezedTerm = 4;

        /// <summary> How many kinds of harm exist, so one running total can be kept for each. </summary>
        static readonly int CategoryCount = Enum.GetValues<ContentCategory>().Length;

        /// <summary> The marks of a line pointed at a person, kept as a set so the lookup costs nothing. </summary>
        static readonly HashSet<string> SecondPersonMarks = new(ContentLexicon.SecondPersonMarks, StringComparer.Ordinal);

        /// <summary>
        /// Judges a line exactly as it was written.
        /// </summary>
        /// <param name="text"> The line as its author typed it. </param>
        /// <returns> What the line was read as; <see cref="ContentVerdict.Clean"/> when nothing was found. </returns>
        public static ContentVerdict Judge(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return ContentVerdict.Clean;

            NormalisedText line = NormalisedText.Of(text);
            if (line.IsEmpty) return ContentVerdict.Clean;

            HashSet<string> spokenWords = new(line.Words.Split(' '), StringComparer.Ordinal);

            // Padded on both sides so a phrase is found at the start and the end of a line as readily as in the
            // middle, and so a phrase never matches across a word boundary it does not own.
            string padded = $" {line.Words} ";

            int[] weightByCategory = new int[CategoryCount];

            foreach (ContentTerm term in ContentLexicon.SingleWords)
                if (spokenWords.Contains(term.Term) || WasSmuggled(line, term))
                    weightByCategory[(int)term.Category] += term.Weight;

            foreach (ContentTerm term in ContentLexicon.Phrases)
                if (padded.Contains($" {term.Term} ", StringComparison.Ordinal) || WasSmuggled(line, term))
                    weightByCategory[(int)term.Category] += term.Weight;

            int total = 0;
            int heaviest = 0;
            ContentCategory category = ContentCategory.None;

            for (int index = 0; index < weightByCategory.Length; index++)
            {
                total += weightByCategory[index];
                if (weightByCategory[index] <= heaviest) continue;

                heaviest = weightByCategory[index];
                category = (ContentCategory)index;
            }

            if (total == 0) return ContentVerdict.Clean;

            bool directed = spokenWords.Overlaps(SecondPersonMarks) || WrittenText.AccountsIn(text).Count > 0;
            if (directed) total *= DirectedAtSomebodyMultiplier;

            return new ContentVerdict(category, BandFor(total), total, directed);
        }

        /// <summary>
        /// True when a term only shows up once the line is squeezed back together, and the line carries the marks of
        /// having been taken apart on purpose.
        /// </summary>
        /// <param name="line"> The normalised line. </param>
        /// <param name="term"> The term being looked for. </param>
        /// <returns> True when the term was hidden inside broken-up writing. </returns>
        /// <remarks>
        /// The squeezed form is only consulted for writing that was actually broken up. In ordinary writing it glues
        /// neighbouring words together and invents things nobody wrote — it is what turns "the rapist" into
        /// "therapist" — so consulting it unconditionally would accuse innocent lines.
        /// </remarks>
        static bool WasSmuggled(NormalisedText line, ContentTerm term)
        {
            if (!line.WasBrokenUp) return false;

            string joined = term.Term.Replace(" ", string.Empty);
            return joined.Length >= ShortestSqueezedTerm && line.Squeezed.Contains(joined, StringComparison.Ordinal);
        }

        /// <summary> The band a score falls in. </summary>
        /// <param name="score"> The weight gathered from the line. </param>
        /// <returns> How far the line goes. </returns>
        static ContentBand BandFor(int score) => score switch
        {
            >= ExtremeScore => ContentBand.Extreme,
            >= AbusiveScore => ContentBand.Abusive,
            >= CoarseScore => ContentBand.Coarse,
            _ => ContentBand.Nothing
        };
    }
}
