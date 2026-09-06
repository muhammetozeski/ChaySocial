namespace ChaySocial.MainProject.Text
{
    /// <summary>
    /// How long a long piece takes to read, worked out from the piece itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here is stored, counted or sent. The measurement is made on the device that is about to draw the
    /// piece, from the body its writer signed, so no field is added to a post and nowhere is it written down who
    /// looked at what for how long.
    /// </para>
    /// <para>
    /// <c>StyleFingerprint</c> counts words too, and is deliberately not used: that count strips punctuation and
    /// exists to measure how somebody writes. This one counts what is on the page.
    /// </para>
    /// </remarks>
    public static class ReadingLength
    {
        /// <summary>
        /// Words a reader gets through in a minute. A round figure for adult silent reading rather than a
        /// measurement of any particular person, which is why every phrase this class produces says "about".
        /// </summary>
        public const int WordsReadEachMinute = 200;

        /// <summary> Shortest a piece is ever said to be: under a minute still costs somebody a minute to sit down to. </summary>
        public const int ShortestReadMinutes = 1;

        /// <summary> How a one-minute read is said, where the plural would read wrong. </summary>
        const string SingleMinutePhrase = "about a minute";

        /// <summary> How every longer read is said; the placeholder takes the number of minutes. </summary>
        const string ManyMinutesPhraseFormat = "about {0} minutes";

        /// <summary> Counts the words in a long piece. </summary>
        /// <param name="longBody"> The body as its writer typed it. </param>
        /// <returns> How many words are on the page. </returns>
        /// <remarks>
        /// Fenced code counts as well. Those lines are on the page like every other line, and skipping them would
        /// tell a reader a piece is shorter than what they are about to scroll through.
        /// </remarks>
        public static int WordsIn(string longBody)
        {
            int words = 0;

            foreach (ProseBlock block in WrittenProse.Read(longBody))
            {
                words += block.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            }

            return words;
        }

        /// <summary> Turns a word count into the minutes it is said to take. </summary>
        /// <param name="words"> How many words are on the page. </param>
        /// <returns> The minutes, never below <see cref="ShortestReadMinutes"/>. </returns>
        public static int MinutesFor(int words)
            => Math.Max(
                ShortestReadMinutes,
                (int)Math.Round(words / (double)WordsReadEachMinute, MidpointRounding.AwayFromZero));

        /// <summary> How a number of minutes is said to a reader. </summary>
        /// <param name="minutes"> The minutes a piece takes. </param>
        /// <returns> The phrase to draw. </returns>
        public static string Describe(int minutes)
            => minutes <= ShortestReadMinutes
                ? SingleMinutePhrase
                : string.Format(ManyMinutesPhraseFormat, minutes);

        /// <summary> How long a piece takes to read, said in the words a reader sees. </summary>
        /// <param name="longBody"> The body as its writer typed it. </param>
        /// <returns> The phrase to draw. </returns>
        public static string Describe(string longBody) => Describe(MinutesFor(WordsIn(longBody)));
    }
}
