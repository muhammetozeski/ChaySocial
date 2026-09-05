using System.Globalization;
using System.Text;

namespace ChaySocial.MainProject.Moderation
{
    /// <summary>
    /// A line of writing reduced to the shape a reader actually sees, so that two lines a person reads the same way
    /// are judged the same way. Everything a writer can do to a word without changing how it reads — accents, look
    /// alike letters from another alphabet, digits standing in for letters, invisible characters wedged between the
    /// letters, a letter held down too long — is undone here.
    /// </summary>
    /// <param name="Words">
    /// The line in its plain form: lowercase, letters and digits only, one space between words. Whole words can be
    /// looked up in this without a short word matching the middle of a longer one.
    /// </param>
    /// <param name="Squeezed">
    /// The same line with the spaces taken out. A word broken up on purpose — <c>f u c k</c>, <c>f.u.c.k</c> — is
    /// whole again here, at the price of words running into their neighbours, so this form is worth less as evidence.
    /// </param>
    public readonly record struct NormalisedText(string Words, string Squeezed)
    {
        /// <summary> Longest run of one letter that ordinary spelling produces; anything longer is emphasis or evasion. </summary>
        /// <remarks> English doubles letters and stops there: <c>ll</c>, <c>ss</c>, <c>ee</c>. A third is a person leaning on the key. </remarks>
        const int LongestNaturalLetterRun = 2;

        /// <summary> Nothing at all, for a line with no readable characters in it. </summary>
        public static readonly NormalisedText Empty = new(string.Empty, string.Empty);

        /// <summary> True when the line held nothing a reader could read. </summary>
        public bool IsEmpty => Words.Length == 0;

        /// <summary>
        /// Reduces a line to its readable shape.
        /// </summary>
        /// <param name="text"> The line exactly as it was written. </param>
        /// <returns> Its normalised forms; <see cref="Empty"/> when nothing readable is left. </returns>
        /// <remarks>
        /// The stored text is never touched. This runs on a copy, at the moment a line is judged, so a post keeps
        /// exactly the characters its author typed and its signature keeps verifying.
        /// </remarks>
        public static NormalisedText Of(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Empty;

            // Read straight from what was typed. There is no Unicode decomposition step because string.Normalize is
            // not available in the browser, which is where this has to run; Confusables folds accents itself, and a
            // combining mark that arrives on its own is dropped below as the invisible character it is.
            StringBuilder words = new(text.Length);
            char previousKept = '\0';
            int runLength = 0;
            bool pendingSeparator = false;

            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];

                if (IsInvisible(character)) continue;

                char letter = Confusables.ToPlainLetter(character);

                // A digit or a symbol only stands in for a letter when it sits against one. On its own it is a
                // number or punctuation, and rewriting "2026" into "2o26" would make a date look like a word.
                if (Confusables.IsStandIn(letter) && TouchesALetter(text, index)) letter = Confusables.StandInLetter(letter);

                if (!char.IsLetterOrDigit(letter))
                {
                    // Punctuation and spaces both end a word. Which one it was does not matter to a reader, and
                    // treating them alike is what makes "f.u.c.k" and "f u c k" reduce to the same thing.
                    if (words.Length > 0) pendingSeparator = true;
                    previousKept = '\0';
                    runLength = 0;
                    continue;
                }

                letter = char.ToLowerInvariant(letter);

                if (letter == previousKept)
                {
                    runLength++;

                    // The moment a run outgrows what spelling produces, the whole run was emphasis rather than
                    // letters: the doubling already written is taken back so that "iiii" ends up as one "i", while
                    // the "ll" in "hello" — a run that never gets this far — is left exactly as it was typed.
                    if (runLength == LongestNaturalLetterRun + 1) words.Length--;
                    if (runLength > LongestNaturalLetterRun) continue;
                }
                else
                {
                    previousKept = letter;
                    runLength = 1;
                }

                if (pendingSeparator)
                {
                    words.Append(' ');
                    pendingSeparator = false;
                }

                words.Append(letter);
            }

            if (words.Length == 0) return Empty;

            string plain = words.ToString();
            return new NormalisedText(plain, plain.Replace(" ", string.Empty));
        }

        /// <summary>
        /// True for a character that takes up no room on screen: the accents left behind by decomposition, and the
        /// zero width and formatting characters people paste between letters to break a word up without showing it.
        /// </summary>
        /// <param name="character"> Character to judge. </param>
        /// <returns> True when a reader would never see it. </returns>
        static bool IsInvisible(char character)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);

            return category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format
                || character == Confusables.SoftHyphen
                || character == Confusables.ZeroWidthSpace;
        }

        /// <summary> True when the character at this position has a letter beside it. </summary>
        /// <param name="text"> The line being read. </param>
        /// <param name="index"> Position of the character. </param>
        /// <returns> True when a letter stands next to it on either side. </returns>
        /// <remarks>
        /// Invisible characters are stepped over rather than counted as neighbours. Wedging a zero width space
        /// between a stand-in digit and the letter it belongs to is precisely how somebody would keep the digit from
        /// being read as part of the word.
        /// </remarks>
        static bool TouchesALetter(string text, int index) => HasLetterTowards(text, index, -1) || HasLetterTowards(text, index, 1);

        /// <summary> True when the first visible character in one direction is a letter. </summary>
        /// <param name="text"> The line being read. </param>
        /// <param name="index"> Position to start from. </param>
        /// <param name="step"> Which way to walk: -1 for backwards, 1 for forwards. </param>
        /// <returns> True when a letter is the next thing a reader would see that way. </returns>
        static bool HasLetterTowards(string text, int index, int step)
        {
            for (int position = index + step; position >= 0 && position < text.Length; position += step)
            {
                if (IsInvisible(text[position])) continue;
                return char.IsLetter(Confusables.ToPlainLetter(text[position]));
            }

            return false;
        }
    }
}
