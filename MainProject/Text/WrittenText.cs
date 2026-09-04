using ChaySocial.MainProject.Cryptography;

namespace ChaySocial.MainProject.Text
{
    /// <summary> What one piece of a written line turns out to be. </summary>
    public enum WrittenPieceKind
    {
        /// <summary> Ordinary words, drawn as they were typed. </summary>
        Words,

        /// <summary> An account somebody named, drawn as a link to that account. </summary>
        Account,

        /// <summary> A subject somebody named, drawn as a link to everything written under it. </summary>
        Subject
    }

    /// <summary> One piece of a written line: either the words themselves, or something the writer named. </summary>
    /// <param name="Kind"> What this piece is. </param>
    /// <param name="Text"> Exactly the characters the writer typed, mark included, so the pieces rejoin into the original. </param>
    /// <param name="Value"> The address or the subject behind it, empty for ordinary words. </param>
    public readonly record struct WrittenPiece(WrittenPieceKind Kind, string Text, string Value);

    /// <summary>
    /// Reads the marks people put in what they write: <c>@address</c> for an account and <c>#subject</c> for a
    /// topic. Everything that carries text — a post, a comment, an answer, a private message — is read by this one
    /// class, so a mention means the same thing wherever it is written and nothing has to be stored twice.
    /// </summary>
    /// <remarks>
    /// Nothing here touches the stored text. A post keeps exactly what its author typed, and the marks are found
    /// again each time it is drawn: a name that turns out to belong to nobody stays readable as words, and an
    /// account that publishes a profile later starts drawing as a link without anything being rewritten.
    /// </remarks>
    public static class WrittenText
    {
        /// <summary> Character that opens a named account. </summary>
        public const char AccountMark = '@';

        /// <summary> Character that opens a named subject. </summary>
        public const char SubjectMark = '#';

        /// <summary> Shortest an address can be after its prefix before it is taken seriously as one. </summary>
        const int ShortestAddressBody = 16;

        /// <summary> Longest subject accepted; past this it is somebody leaning on the key rather than naming a topic. </summary>
        public const int LongestSubject = 50;

        /// <summary>
        /// Splits a line into its pieces. The pieces always rejoin into exactly the text handed in, so drawing them
        /// one after another shows what the writer wrote and nothing else.
        /// </summary>
        /// <param name="text"> The line as it was written. </param>
        /// <returns> Its pieces in order; a line with no marks in it comes back as a single piece of words. </returns>
        public static IReadOnlyList<WrittenPiece> Read(string text)
        {
            if (string.IsNullOrEmpty(text)) return [];

            List<WrittenPiece> pieces = [];
            int wordsStart = 0;
            int index = 0;

            while (index < text.Length)
            {
                char mark = text[index];

                if ((mark != AccountMark && mark != SubjectMark) || !OpensAName(text, index))
                {
                    index++;
                    continue;
                }

                int end = mark == AccountMark ? EndOfAddress(text, index + 1) : EndOfSubject(text, index + 1);

                if (end < 0)
                {
                    index++;
                    continue;
                }

                if (index > wordsStart) pieces.Add(new WrittenPiece(WrittenPieceKind.Words, text[wordsStart..index], string.Empty));

                string named = text[index..end];
                pieces.Add(new WrittenPiece(
                    mark == AccountMark ? WrittenPieceKind.Account : WrittenPieceKind.Subject,
                    named,
                    named[1..]));

                index = end;
                wordsStart = end;
            }

            if (wordsStart < text.Length) pieces.Add(new WrittenPiece(WrittenPieceKind.Words, text[wordsStart..], string.Empty));

            return pieces;
        }

        /// <summary> Every account named in a line, each once, in the order they were named. </summary>
        /// <param name="text"> The line as it was written. </param>
        /// <returns> The addresses named in it. </returns>
        public static IReadOnlyList<string> AccountsIn(string text)
            => [.. Read(text).Where(piece => piece.Kind == WrittenPieceKind.Account).Select(piece => piece.Value).Distinct(StringComparer.Ordinal)];

        /// <summary> Every subject named in a line, each once, lowercased so <c>#Tea</c> and <c>#tea</c> are one subject. </summary>
        /// <param name="text"> The line as it was written. </param>
        /// <returns> The subjects named in it. </returns>
        public static IReadOnlyList<string> SubjectsIn(string text)
            => [.. Read(text)
                .Where(piece => piece.Kind == WrittenPieceKind.Subject)
                .Select(piece => NormaliseSubject(piece.Value))
                .Distinct(StringComparer.Ordinal)];

        /// <summary> The form a subject is stored and looked up under: lowercase, so naming it differently still finds it. </summary>
        /// <param name="subject"> The subject as it was written, without its mark. </param>
        /// <returns> Its stored form. </returns>
        public static string NormaliseSubject(string subject) => subject.ToLowerInvariant();

        /// <summary>
        /// True when a mark at this position opens a name rather than sitting inside a word. A mark has to follow
        /// whitespace or start the line — otherwise an address written in an email, or a colour written as #a1b2c3
        /// inside a longer token, would be read as a name.
        /// </summary>
        /// <param name="text"> The line being read. </param>
        /// <param name="index"> Position of the mark. </param>
        /// <returns> True when this mark can open a name. </returns>
        static bool OpensAName(string text, int index) => index == 0 || char.IsWhiteSpace(text[index - 1]);

        /// <summary>
        /// Finds where an address ends, or reports that what follows the mark is not one. Only the app's own
        /// address alphabet is accepted, so a mark followed by ordinary words stays ordinary words.
        /// </summary>
        /// <param name="text"> The line being read. </param>
        /// <param name="start"> Position just after the mark. </param>
        /// <returns> The position after the address, or -1 when there is no address here. </returns>
        static int EndOfAddress(string text, int start)
        {
            if (!text.AsSpan(start).StartsWith(AppCryptography.AddressPrefix, StringComparison.Ordinal)) return -1;

            int index = start + AppCryptography.AddressPrefix.Length;
            while (index < text.Length && Base32.IsDigit(text[index])) index++;

            return index - start - AppCryptography.AddressPrefix.Length >= ShortestAddressBody ? index : -1;
        }

        /// <summary>
        /// Finds where a subject ends, or reports that what follows the mark is not one. Letters, digits and
        /// underscores make a subject; anything else closes it, and a mark followed by nothing usable is just a
        /// character somebody typed.
        /// </summary>
        /// <param name="text"> The line being read. </param>
        /// <param name="start"> Position just after the mark. </param>
        /// <returns> The position after the subject, or -1 when there is no subject here. </returns>
        static int EndOfSubject(string text, int start)
        {
            int index = start;
            while (index < text.Length && index - start < LongestSubject && IsSubjectCharacter(text[index])) index++;

            return index > start ? index : -1;
        }

        /// <summary> True for a character a subject may be made of. </summary>
        /// <param name="character"> Character to judge. </param>
        /// <returns> True when it belongs inside a subject. </returns>
        static bool IsSubjectCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
    }
}
