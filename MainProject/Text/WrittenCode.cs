namespace ChaySocial.MainProject.Text
{
    /// <summary> What one piece of a line of code turns out to be. </summary>
    public enum CodeTokenKind
    {
        /// <summary> Everything that is not one of the others: punctuation, spacing, ordinary names. </summary>
        Plain,

        /// <summary> A word the language reserves for itself. </summary>
        Keyword,

        /// <summary> A quoted run. </summary>
        Text,

        /// <summary> A number. </summary>
        Number,

        /// <summary> A remark the writer left for a reader rather than for the machine. </summary>
        Comment
    }

    /// <summary> One piece of a line of code, and what it turned out to be. </summary>
    /// <param name="Kind"> What this piece is. </param>
    /// <param name="Text"> Exactly the characters it covers, so the pieces rejoin into the line. </param>
    public readonly record struct CodeToken(CodeTokenKind Kind, string Text);

    /// <summary>
    /// Reads one line of code into the pieces a reader's eye separates it into. It is a colouring, not a parser:
    /// it knows a handful of reserved words, what quotes look like and what a comment opens with, and it is wrong
    /// about anything cleverer than that.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately line by line, with nothing carried from one line to the next. A snippet that arrives off the
    /// network is somebody else's claim, and a reader that carried state could be handed one unclosed quote and
    /// paint the rest of a piece as a string. The cost is that a run spanning two lines is coloured as two runs,
    /// which is a smaller price than a piece that reads wrong from a stranger's typo.
    /// </para>
    /// <para>
    /// The pieces always rejoin into exactly the line handed in, so what is drawn is what was written.
    /// </para>
    /// </remarks>
    public static class WrittenCode
    {
        /// <summary> The language label for C#, as a writer would type it after a fence. </summary>
        public const string CSharpLanguage = "csharp";

        /// <summary> The language label for CSS. </summary>
        public const string CssLanguage = "css";

        /// <summary> The language label for SQL. </summary>
        public const string SqlLanguage = "sql";

        /// <summary> What opens a comment that runs to the end of the line in most of these languages. </summary>
        const string LineCommentMark = "//";

        /// <summary> What opens one in SQL. </summary>
        const string SqlCommentMark = "--";

        /// <summary> What opens a block comment; it is read to the end of the line and no further. </summary>
        const string BlockCommentMark = "/*";

        /// <summary> The character that joins a name rather than ending it. </summary>
        const char NameJoiningUnderscore = '_';

        /// <summary> The character a decimal point is written with. </summary>
        const char DecimalPoint = '.';

        /// <summary> The two characters a quoted run may be opened and closed with. </summary>
        static readonly char[] QuoteMarks = ['"', '\''];

        /// <summary> Words C# reserves, the ones a reader of this app's own code would meet. </summary>
        static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
        {
            "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char", "class",
            "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
            "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "get", "goto", "if",
            "implicit", "in", "init", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
            "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
            "readonly", "record", "ref", "return", "sealed", "set", "short", "sizeof", "stackalloc", "static",
            "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
            "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "when", "where", "while", "yield"
        };

        /// <summary> Words CSS uses as its own, rather than as a value somebody chose. </summary>
        static readonly HashSet<string> CssKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "absolute", "auto", "block", "border", "bottom", "color", "column", "display", "fixed", "flex", "grid",
            "height", "hidden", "inherit", "important", "left", "margin", "none", "padding", "position", "relative",
            "right", "row", "solid", "top", "transparent", "width"
        };

        /// <summary> Words SQL reserves. </summary>
        static readonly HashSet<string> SqlKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "and", "as", "asc", "between", "by", "case", "create", "delete", "desc", "distinct", "drop", "else",
            "end", "exists", "from", "group", "having", "in", "index", "inner", "insert", "into", "is", "join",
            "left", "like", "limit", "not", "null", "on", "or", "order", "outer", "select", "set", "table", "then",
            "union", "update", "values", "when", "where"
        };

        /// <summary>
        /// Splits one line into its pieces.
        /// </summary>
        /// <param name="line"> The line, exactly as it was written. </param>
        /// <param name="language"> The label the fence carried; an unknown one colours nothing. </param>
        /// <returns> The pieces in order; they rejoin into the line handed in. </returns>
        public static IReadOnlyList<CodeToken> Read(string line, string language)
        {
            if (line.Length == 0) return [];

            HashSet<string>? keywords = KeywordsOf(language);
            if (keywords is null) return [new CodeToken(CodeTokenKind.Plain, line)];

            string commentMark = language.Equals(SqlLanguage, StringComparison.OrdinalIgnoreCase)
                ? SqlCommentMark
                : LineCommentMark;

            List<CodeToken> tokens = [];
            int plainStart = 0;
            int index = 0;

            while (index < line.Length)
            {
                if (OpensComment(line, index, commentMark))
                {
                    Close(tokens, line, plainStart, index);
                    tokens.Add(new CodeToken(CodeTokenKind.Comment, line[index..]));
                    return tokens;
                }

                if (Array.IndexOf(QuoteMarks, line[index]) >= 0)
                {
                    Close(tokens, line, plainStart, index);

                    int end = EndOfQuoted(line, index);
                    tokens.Add(new CodeToken(CodeTokenKind.Text, line[index..end]));

                    index = end;
                    plainStart = end;
                    continue;
                }

                if (char.IsAsciiDigit(line[index]) && !IsInsideName(line, index))
                {
                    Close(tokens, line, plainStart, index);

                    int end = EndOfNumber(line, index);
                    tokens.Add(new CodeToken(CodeTokenKind.Number, line[index..end]));

                    index = end;
                    plainStart = end;
                    continue;
                }

                if (OpensName(line, index))
                {
                    int end = EndOfName(line, index);
                    string word = line[index..end];

                    if (keywords.Contains(word))
                    {
                        Close(tokens, line, plainStart, index);
                        tokens.Add(new CodeToken(CodeTokenKind.Keyword, word));
                        plainStart = end;
                    }

                    index = end;
                    continue;
                }

                index++;
            }

            Close(tokens, line, plainStart, line.Length);

            return tokens;
        }

        /// <summary> The reserved words of one language, or null when this app colours nothing for it. </summary>
        /// <param name="language"> The label the fence carried. </param>
        /// <returns> Its words, or null. </returns>
        static HashSet<string>? KeywordsOf(string language) => language.ToLowerInvariant() switch
        {
            CSharpLanguage => CSharpKeywords,
            CssLanguage => CssKeywords,
            SqlLanguage => SqlKeywords,
            _ => null
        };

        /// <summary> Adds everything between two positions as one plain piece, and nothing when they meet. </summary>
        /// <param name="tokens"> The pieces so far. </param>
        /// <param name="line"> The line being read. </param>
        /// <param name="start"> Where the plain run began. </param>
        /// <param name="end"> Where it ends. </param>
        static void Close(List<CodeToken> tokens, string line, int start, int end)
        {
            if (end > start) tokens.Add(new CodeToken(CodeTokenKind.Plain, line[start..end]));
        }

        /// <summary> True when a comment opens at this position. </summary>
        /// <param name="line"> The line being read. </param>
        /// <param name="index"> Position to test. </param>
        /// <param name="commentMark"> What opens a comment in this language. </param>
        /// <returns> True when the rest of the line is a comment. </returns>
        static bool OpensComment(string line, int index, string commentMark)
            => line.AsSpan(index).StartsWith(commentMark, StringComparison.Ordinal)
               || line.AsSpan(index).StartsWith(BlockCommentMark, StringComparison.Ordinal);

        /// <summary> Finds where a quoted run ends, or the end of the line when it was never closed. </summary>
        /// <param name="line"> The line being read. </param>
        /// <param name="start"> Position of the opening quote. </param>
        /// <returns> The position just after the run. </returns>
        static int EndOfQuoted(string line, int start)
        {
            char quote = line[start];

            for (int index = start + 1; index < line.Length; index++)
            {
                if (line[index] == quote) return index + 1;
            }

            return line.Length;
        }

        /// <summary> Finds where a number ends. </summary>
        /// <param name="line"> The line being read. </param>
        /// <param name="start"> Position of its first digit. </param>
        /// <returns> The position just after it. </returns>
        static int EndOfNumber(string line, int start)
        {
            int index = start;
            while (index < line.Length && (char.IsAsciiDigit(line[index]) || line[index] == DecimalPoint)) index++;

            return index;
        }

        /// <summary> True when a name may open at this position. </summary>
        /// <param name="line"> The line being read. </param>
        /// <param name="index"> Position to test. </param>
        /// <returns> True when a word starts here. </returns>
        static bool OpensName(string line, int index)
            => (char.IsLetter(line[index]) || line[index] == NameJoiningUnderscore) && !IsInsideName(line, index);

        /// <summary> True when the character before this position is part of the same word. </summary>
        /// <param name="line"> The line being read. </param>
        /// <param name="index"> Position to test. </param>
        /// <returns> True when this position is not the start of a word. </returns>
        static bool IsInsideName(string line, int index)
            => index > 0 && (char.IsLetterOrDigit(line[index - 1]) || line[index - 1] == NameJoiningUnderscore);

        /// <summary> Finds where a word ends. </summary>
        /// <param name="line"> The line being read. </param>
        /// <param name="start"> Position of its first character. </param>
        /// <returns> The position just after it. </returns>
        static int EndOfName(string line, int start)
        {
            int index = start;
            while (index < line.Length && (char.IsLetterOrDigit(line[index]) || line[index] == NameJoiningUnderscore)) index++;

            return index;
        }
    }
}
