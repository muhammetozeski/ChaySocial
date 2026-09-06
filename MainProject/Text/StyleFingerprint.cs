using System.Globalization;
using System.Text;

namespace ChaySocial.MainProject.Text
{
    /// <summary>
    /// What one body of writing measures out to on axes that survive a change of subject: how long the sentences
    /// run, how long the words are, how much punctuation and how many emoji sit among them, how often a sentence
    /// opens in capitals, and how much of the writing each of a few very common words takes up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every axis is a fraction between zero and one, because a comparison between axes measured in different
    /// units is not a comparison at all: words-per-sentence runs to forty while emoji-per-word runs to one, and
    /// left as they are the first would decide every answer on its own. Each raw measurement is divided by a named
    /// ceiling — the point past which the axis stops telling two writers apart — and clamped there.
    /// </para>
    /// <para>
    /// Nothing here is stored and nothing leaves the device. The posts a fingerprint is folded from are documents
    /// the server already hands to anybody who asks; the arithmetic on top of them is the reader's own.
    /// </para>
    /// </remarks>
    /// <param name="Axes"> The measured fractions, in a fixed order; empty when there was no writing to measure. </param>
    /// <param name="WordsMeasured"> How many words went into it, so a caller can tell a habit from a coincidence. </param>
    public readonly record struct StyleFingerprint(IReadOnlyList<double> Axes, int WordsMeasured)
    {
        /// <summary> Axes measured for every piece of writing, before the common words are counted one by one. </summary>
        const int PlainAxisCount = 5;

        /// <summary> Longest sentence the measure still tells apart, in words; everything longer reads as one long sentence. </summary>
        const double LongestSentenceCounted = 40.0;

        /// <summary> Longest word the measure still tells apart, in characters. </summary>
        const double LongestWordCounted = 12.0;

        /// <summary> Most punctuation marks per word the measure tells apart; past one mark a word it is all the same to it. </summary>
        const double MostPunctuationPerWordCounted = 1.0;

        /// <summary> Most emoji per word the measure tells apart. </summary>
        const double MostEmojiPerWordCounted = 1.0;

        /// <summary> Largest share one common word can hold before the axis stops telling shares apart. </summary>
        const double LargestShareOfOneWordCounted = 0.2;

        /// <summary> Sentence-closing marks; a run of them still closes one sentence. </summary>
        static readonly char[] SentenceEndMarks = ['.', '!', '?'];

        /// <summary> The line feed, which closes a sentence the way a full stop does. </summary>
        const char LineFeed = '\n';

        /// <summary> The carriage return, for writing that arrived from a machine that uses it. </summary>
        const char CarriageReturn = '\r';

        /// <summary>
        /// The characters that join a word rather than ending it, so "don't" is one word and not two. Both shapes
        /// of apostrophe are here: a phone keyboard types the curly one without being asked, and reading only the
        /// straight one would cut a word in half for everybody writing on a phone — which is most people.
        /// </summary>
        static readonly int[] WordJoiningApostrophes = ['\'', '’'];

        /// <summary> The fingerprint of nothing, which is what an account with no writing behind it has. </summary>
        public static readonly StyleFingerprint Unwritten = new([], 0);

        /// <summary> True when there was writing to measure. </summary>
        public bool IsWritten => Axes.Count > 0;

        /// <summary> Measures one body of writing. </summary>
        /// <param name="text"> The writing; blank text measures to <see cref="Unwritten"/>. </param>
        /// <returns> Its fingerprint. </returns>
        public static StyleFingerprint Of(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Unwritten;

            IReadOnlyList<MeasuredWord> words = WordsIn(text);
            if (words.Count == 0) return Unwritten;

            int punctuation = 0;
            int letters = 0;

            foreach (Rune rune in text.EnumerateRunes())
            {
                if (Rune.IsPunctuation(rune)) punctuation++;
            }

            int emoji = CountEmoji(text);

            foreach (MeasuredWord word in words) letters += word.CharacterCount;

            double capitalisedOpenings = ReadOpenings(text, out int sentences);

            double[] axes = new double[PlainAxisCount + StyleWords.Commonest.Count];
            axes[0] = Fraction(words.Count / (double)sentences, LongestSentenceCounted);
            axes[1] = Fraction(letters / (double)words.Count, LongestWordCounted);
            axes[2] = Fraction(punctuation / (double)words.Count, MostPunctuationPerWordCounted);
            axes[3] = Fraction(emoji / (double)words.Count, MostEmojiPerWordCounted);
            axes[4] = capitalisedOpenings;

            Dictionary<string, int> used = CountWords(words);

            for (int index = 0; index < StyleWords.Commonest.Count; index++)
            {
                int times = used.GetValueOrDefault(StyleWords.Commonest[index]);
                axes[PlainAxisCount + index] = Fraction(times / (double)words.Count, LargestShareOfOneWordCounted);
            }

            return new StyleFingerprint(axes, words.Count);
        }

        /// <summary>
        /// How alike two fingerprints read: one when every axis lands in the same place, zero when every axis is at
        /// opposite ends. It is the plain average of how far apart the axes are, so no axis outranks another and
        /// somebody who does not trust the number can redo it with the axes in front of them.
        /// </summary>
        /// <param name="left"> One fingerprint. </param>
        /// <param name="right"> The other. </param>
        /// <returns> Their closeness, or null when there is nothing to compare. </returns>
        /// <remarks>
        /// <para>
        /// Null rather than zero when a side has no writing behind it. Zero is a real answer — it means these two
        /// are as far apart as this measure goes — and handing back the furthest possible answer for a comparison
        /// that could not be made is how a caller ends up treating an account with no posts as the least like
        /// everybody, which is the opposite of what it is.
        /// </para>
        /// <para>
        /// An axis where both sides sit at zero is left out of the average. Neither piece of writing used that word
        /// at all, so the axis says nothing about either of them; counted, it would report writing in a language
        /// the word list does not cover as more alike than it is, simply for sharing an absence.
        /// </para>
        /// </remarks>
        public static double? Closeness(StyleFingerprint left, StyleFingerprint right)
        {
            if (!left.IsWritten || !right.IsWritten) return null;
            if (left.Axes.Count != right.Axes.Count) return null;

            double apart = 0.0;
            int counted = 0;

            for (int index = 0; index < left.Axes.Count; index++)
            {
                if (left.Axes[index] == 0.0 && right.Axes[index] == 0.0) continue;

                apart += Math.Abs(left.Axes[index] - right.Axes[index]);
                counted++;
            }

            return counted == 0 ? null : 1.0 - (apart / counted);
        }

        /// <summary> Turns a raw measurement into the fraction of its ceiling that an axis holds. </summary>
        /// <param name="measured"> The raw measurement. </param>
        /// <param name="ceiling"> The point past which this axis stops telling writers apart. </param>
        /// <returns> A value between zero and one. </returns>
        static double Fraction(double measured, double ceiling) => Math.Clamp(measured / ceiling, 0.0, 1.0);

        /// <summary>
        /// Walks the sentences, counting how many there are and how many opened with a capital. Sentences are
        /// counted by their openings rather than by their closing marks, so writing that never reaches a full stop
        /// is one sentence rather than none.
        /// </summary>
        /// <param name="text"> The writing. </param>
        /// <param name="sentences"> Set to how many sentences opened, never below one. </param>
        /// <returns> The share of sentences that opened with a capital letter. </returns>
        /// <remarks>
        /// A line break closes a sentence as surely as a full stop does. Plenty of people write a whole line and
        /// never punctuate the end of it, and — more to the point here — a body folded out of forty posts is
        /// joined by blank lines rather than by full stops. Counting only the marks made that body one enormous
        /// sentence and pinned its length axis to the ceiling for every account alike.
        /// </remarks>
        static double ReadOpenings(string text, out int sentences)
        {
            int opened = 0;
            int capitalised = 0;
            bool waitingForOpening = true;

            foreach (Rune rune in text.EnumerateRunes())
            {
                if (waitingForOpening && Rune.IsLetterOrDigit(rune))
                {
                    opened++;
                    if (Rune.IsUpper(rune)) capitalised++;
                    waitingForOpening = false;
                    continue;
                }

                if (ClosesSentence(rune)) waitingForOpening = true;
            }

            sentences = Math.Max(opened, 1);
            return opened == 0 ? 0.0 : capitalised / (double)opened;
        }

        /// <summary> True for a character that ends a sentence: one of the closing marks, or a line break. </summary>
        /// <param name="rune"> The character. </param>
        /// <returns> Whether the next word starts a new sentence. </returns>
        static bool ClosesSentence(Rune rune)
        {
            if (!rune.IsBmp) return false;

            char character = (char)rune.Value;

            return Array.IndexOf(SentenceEndMarks, character) >= 0 || character is LineFeed or CarriageReturn;
        }

        /// <summary>
        /// Cuts writing into words: runs of letters and digits, held together by an apostrophe. A run with no
        /// letter or digit in it at all is not a word — an apostrophe standing on its own is punctuation, and
        /// counted as a word it would drag the average word length down to one.
        /// </summary>
        /// <param name="text"> The writing. </param>
        /// <returns> Its words, in order, each with the number of characters a reader would count in it. </returns>
        static IReadOnlyList<MeasuredWord> WordsIn(string text)
        {
            List<MeasuredWord> words = [];
            StringBuilder current = new();
            int letters = 0;
            int characters = 0;

            foreach (Rune rune in text.EnumerateRunes())
            {
                if (Rune.IsLetterOrDigit(rune) || Array.IndexOf(WordJoiningApostrophes, rune.Value) >= 0)
                {
                    current.Append(rune.ToString());
                    characters++;
                    if (Rune.IsLetterOrDigit(rune)) letters++;
                    continue;
                }

                Close(words, current, letters, characters);
                letters = 0;
                characters = 0;
            }

            Close(words, current, letters, characters);

            return words;
        }

        /// <summary> Keeps the run being built as a word, when it is one, and empties the builder either way. </summary>
        /// <param name="words"> The words so far. </param>
        /// <param name="current"> The run being built. </param>
        /// <param name="letters"> How many letters or digits it holds. </param>
        /// <param name="characters"> How many characters a reader would count in it. </param>
        static void Close(List<MeasuredWord> words, StringBuilder current, int letters, int characters)
        {
            if (letters > 0) words.Add(new MeasuredWord(current.ToString(), characters));

            current.Clear();
        }

        /// <summary> Counts how often each word was used, ignoring case so "The" and "the" are one habit. </summary>
        /// <param name="words"> The words. </param>
        /// <returns> Each word and how often it appeared. </returns>
        static Dictionary<string, int> CountWords(IReadOnlyList<MeasuredWord> words)
        {
            Dictionary<string, int> counted = new(StringComparer.OrdinalIgnoreCase);

            foreach (MeasuredWord word in words)
            {
                counted[word.Text] = counted.GetValueOrDefault(word.Text) + 1;
            }

            return counted;
        }

        /// <summary>
        /// Counts the emoji in a piece of writing — emoji, not the characters they are built out of.
        /// </summary>
        /// <param name="text"> The writing. </param>
        /// <returns> How many emoji a reader would count. </returns>
        /// <remarks>
        /// A family emoji is five people joined by four invisible characters, and a flag is two letters that are
        /// not letters; counting characters made one of each into six and two. Walking the text in the units a
        /// reader sees — what Unicode calls grapheme clusters — counts each of them once.
        /// </remarks>
        static int CountEmoji(string text)
        {
            int emoji = 0;
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);

            while (elements.MoveNext())
            {
                string element = (string)elements.Current;
                if (element.Length == 0) continue;

                if (Rune.TryGetRuneAt(element, 0, out Rune first)
                    && Rune.GetUnicodeCategory(first) == UnicodeCategory.OtherSymbol)
                {
                    emoji++;
                }
            }

            return emoji;
        }

        /// <summary> One word, and how many characters a reader would count in it. </summary>
        /// <param name="Text"> The word itself. </param>
        /// <param name="CharacterCount"> Its length in characters rather than in storage units, so an emoji-free
        /// word and one written in a script stored as pairs are measured the same way. </param>
        readonly record struct MeasuredWord(string Text, int CharacterCount);
    }
}
