namespace ChaySocial.MainProject.Moderation
{
    /// <summary>
    /// Characters that read as one letter but are stored as another, and the letter each of them reads as. Somebody
    /// hiding a word from a filter reaches for these first: an accent, a Cyrillic letter that draws like an <c>a</c>,
    /// a zero in place of an <c>o</c>, a dollar sign in place of an <c>s</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every lookalike is written as its code point rather than as the character itself, and this file is pure ASCII
    /// because of it. A Cyrillic and a Latin <c>a</c> are the same picture on screen: a table written with the
    /// characters themselves would look like it repeated one letter forty times, no reviewer could tell the two
    /// apart, and a single well-meaning tidy-up would quietly empty half of it.
    /// </para>
    /// <para>
    /// The accent folding is done by hand rather than by <c>string.Normalize</c> because that method is not supported
    /// in the browser, and this application's whole point is that it runs in the browser. Doing it here also makes
    /// the result identical on every platform instead of depending on which globalization data the host shipped.
    /// </para>
    /// </remarks>
    public static class Confusables
    {
        /// <summary> A hyphen that only shows if the line has to wrap; invisible otherwise, so it splits words silently. </summary>
        public const char SoftHyphen = (char)0x00AD;

        /// <summary> A space of no width; pasted between letters it breaks a word up without showing anything. </summary>
        public const char ZeroWidthSpace = (char)0x200B;

        /// <summary> First character of the Latin-1 accented block this folds. </summary>
        const char FirstAccentedLatin1 = (char)0x00C0;

        /// <summary> First character of the Latin Extended-A block this folds. </summary>
        const char FirstLatinExtendedA = (char)0x0100;

        /// <summary> One past the last character of the Latin Extended-A block this folds. </summary>
        const char AfterLatinExtendedA = (char)0x0180;

        /// <summary> First character of the fullwidth forms, which draw as ASCII but are stored far away from it. </summary>
        const char FirstFullwidthForm = (char)0xFF01;

        /// <summary> One past the last fullwidth form. </summary>
        const char AfterFullwidthForm = (char)0xFF5F;

        /// <summary> Distance from a fullwidth form to the ASCII character it draws as. </summary>
        const int FullwidthToAsciiDistance = 0xFEE0;

        /// <summary> Stands in the folding tables for a character that is not a letter and must be left alone. </summary>
        const char NotALetter = ' ';

        /// <summary>
        /// The plain letter for each character from <see cref="FirstAccentedLatin1"/> upward, in code point order.
        /// The two spaces are the multiplication and division signs, which sit inside the block but are not letters.
        /// </summary>
        const string AccentedLatin1Folded = "aaaaaaaceeeeiiiidnooooo ouuuuybsaaaaaaaceeeeiiiidnooooo ouuuuyby";

        /// <summary> The plain letter for each character from <see cref="FirstLatinExtendedA"/> upward, in code point order. </summary>
        const string LatinExtendedAFolded =
            "aaaaaa" + "cccccccc" + "dddd" + "eeeeeeeeee" + "gggggggg" + "hhhh" +
            "iiiiiiiiiiii" + "jj" + "kkk" + "llllllllll" + "nnnnnnnnn" + "oooooooo" +
            "rrrrrr" + "ssssssss" + "tttttt" + "uuuuuuuuuuuu" + "ww" + "yyy" + "zzzzzz" + "s";

        /// <summary>
        /// The plain Latin letter a character reads as, or the character itself when it is not a lookalike.
        /// </summary>
        /// <param name="character"> Character as it was stored. </param>
        /// <returns> The letter a reader sees. </returns>
        public static char ToPlainLetter(char character)
        {
            if (character < FirstAccentedLatin1) return character;

            if (character >= FirstFullwidthForm && character < AfterFullwidthForm)
                return (char)(character - FullwidthToAsciiDistance);

            if (character < FirstLatinExtendedA)
            {
                char folded = AccentedLatin1Folded[character - FirstAccentedLatin1];
                return folded == NotALetter ? character : folded;
            }

            if (character < AfterLatinExtendedA) return LatinExtendedAFolded[character - FirstLatinExtendedA];

            return FromOtherAlphabet(character);
        }

        /// <summary> True for a digit or symbol people use in place of a letter. </summary>
        /// <param name="character"> Character to judge. </param>
        /// <returns> True when it has a letter it commonly stands for. </returns>
        public static bool IsStandIn(char character) => StandInLetter(character) != character;

        /// <summary>
        /// The letter a digit or symbol stands for, or the character itself when it stands for nothing.
        /// </summary>
        /// <param name="character"> Character as it was stored. </param>
        /// <returns> The letter it is being used as. </returns>
        /// <remarks>
        /// The caller decides when to apply this. A digit on its own is a number; only a digit pressed against a
        /// letter is somebody spelling a word with it.
        /// </remarks>
        public static char StandInLetter(char character) => character switch
        {
            '0' => 'o',
            '1' or '!' or '|' => 'i',
            '3' => 'e',
            '4' or '@' => 'a',
            '5' or '$' => 's',
            '6' or '9' => 'g',
            '7' or '+' => 't',
            '8' => 'b',
            _ => character
        };

        /// <summary>
        /// The Latin letter a Cyrillic or Greek character draws as. These are the alphabets whose letters happen to
        /// be drawn with the same strokes as Latin ones, which is what makes them useful for hiding a word.
        /// </summary>
        /// <param name="character"> Character as it was stored. </param>
        /// <returns> The letter a reader sees, or the character itself. </returns>
        static char FromOtherAlphabet(char character) => character switch
        {
            (char)0x0430 or (char)0x0410 or (char)0x03B1 or (char)0x0391 => 'a',   // Cyrillic a, Greek alpha
            (char)0x0432 or (char)0x0412 or (char)0x03B2 => 'b',                   // Cyrillic ve, Greek beta
            (char)0x0441 or (char)0x0421 => 'c',                                   // Cyrillic es
            (char)0x0501 => 'd',                                                   // Cyrillic komi de
            (char)0x0435 or (char)0x0415 or (char)0x03B5 or (char)0x0395 => 'e',   // Cyrillic ie, Greek epsilon
            (char)0x043D or (char)0x041D or (char)0x04BB => 'h',                   // Cyrillic en, Cyrillic shha
            (char)0x0456 or (char)0x0406 or (char)0x03B9 or (char)0x0399 => 'i',   // Cyrillic i, Greek iota
            (char)0x0458 => 'j',                                                   // Cyrillic je
            (char)0x043A or (char)0x041A or (char)0x03BA or (char)0x039A => 'k',   // Cyrillic ka, Greek kappa
            (char)0x043C or (char)0x041C or (char)0x03BC or (char)0x039C => 'm',   // Cyrillic em, Greek mu
            (char)0x039D => 'n',                                                   // Greek capital nu
            (char)0x043E or (char)0x041E or (char)0x03BF or (char)0x039F => 'o',   // Cyrillic o, Greek omicron
            (char)0x0440 or (char)0x0420 or (char)0x03C1 or (char)0x03A1 => 'p',   // Cyrillic er, Greek rho
            (char)0x0455 or (char)0x0405 or (char)0x03C3 => 's',                   // Cyrillic dze, Greek sigma
            (char)0x0442 or (char)0x0422 or (char)0x03C4 or (char)0x03A4 => 't',   // Cyrillic te, Greek tau
            (char)0x03C5 => 'u',                                                   // Greek upsilon
            (char)0x03BD => 'v',                                                   // Greek small nu
            (char)0x0445 or (char)0x0425 or (char)0x03C7 or (char)0x03A7 => 'x',   // Cyrillic ha, Greek chi
            (char)0x0443 or (char)0x0423 => 'y',                                   // Cyrillic u

            _ => character
        };
    }
}
