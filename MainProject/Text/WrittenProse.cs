namespace ChaySocial.MainProject.Text
{
    /// <summary> What one block of a long piece turns out to be. </summary>
    public enum ProseBlockKind
    {
        /// <summary> Ordinary prose. </summary>
        Paragraph,

        /// <summary> A line the writer set above what follows it. </summary>
        Heading,

        /// <summary> Somebody else's words, set apart from the writer's own. </summary>
        Quote,

        /// <summary> One item of a list. </summary>
        Bullet,

        /// <summary> A run of lines the writer fenced off as code, kept exactly as typed. </summary>
        Code
    }

    /// <summary> One block of a long piece: what it is, and the line to draw. </summary>
    /// <param name="Kind"> What this block is. </param>
    /// <param name="Text"> The line without the characters that named its kind, or a fenced run joined by newlines. </param>
    /// <param name="Language"> What a fenced run was labelled, empty for every other kind and for an unlabelled fence. </param>
    public readonly record struct ProseBlock(ProseBlockKind Kind, string Text, string Language = "");

    /// <summary>
    /// Reads the shape of a long piece of writing: headings, quotes, lists and the paragraphs between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here touches what was stored. The body a writer signed is kept exactly as they typed it and read
    /// again every time it is drawn — the same arrangement <see cref="WrittenText"/> uses for names and subjects,
    /// and for the same reason: a piece whose stored form is rewritten is no longer the piece that was signed.
    /// </para>
    /// <para>
    /// A block carries its line without the two characters that named its kind, because those are punctuation for
    /// the reader's benefit rather than words the writer meant to be read out. Everything else is left alone, so
    /// each block still goes through the one line reader the whole app shares.
    /// </para>
    /// <para>
    /// A heading is written with a hash and a space. That cannot collide with a subject: a subject is the hash and
    /// the letters that follow it with no space between, so <c>#tea</c> stays a subject wherever it appears.
    /// </para>
    /// </remarks>
    public static class WrittenProse
    {
        /// <summary> Opens a heading, together with the space after it. </summary>
        public const string HeadingMark = "# ";

        /// <summary> Opens a quoted line. </summary>
        public const string QuoteMark = "> ";

        /// <summary> Opens a list item. </summary>
        public const string BulletMark = "- ";

        /// <summary> A second way to open a list item, for anybody who writes lists with stars. </summary>
        public const string StarBulletMark = "* ";

        /// <summary>
        /// Opens and closes a run of lines that are code rather than prose. What follows it on the opening line is
        /// the language, which is a label the writer chose and nothing this app checks.
        /// </summary>
        public const string FenceMark = "```";

        /// <summary> What joins the lines of a fenced run back together, whatever the writer's machine typed. </summary>
        public const string CodeLineEnding = "\n";

        /// <summary> The line endings a body may arrive with, whichever machine it was typed on. </summary>
        static readonly string[] LineEndings = ["\r\n", "\n", "\r"];

        /// <summary>
        /// Splits a body into its blocks. Blank lines separate blocks and are not blocks themselves, so a piece
        /// spaced out generously draws the same as one spaced out sparingly.
        /// </summary>
        /// <param name="body"> The long body as it was written. </param>
        /// <returns> Its blocks in order; an empty body comes back with none. </returns>
        /// <remarks>
        /// Inside a fence the two habits that make prose read evenly — trimming each line and dropping the blank
        /// ones — are exactly what would ruin code, so both are suspended there and the lines are kept as typed.
        /// </remarks>
        public static IReadOnlyList<ProseBlock> Read(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return [];

            List<ProseBlock> blocks = [];
            List<string>? fenced = null;
            string language = string.Empty;

            foreach (string line in body.Split(LineEndings, StringSplitOptions.None))
            {
                string trimmed = line.Trim();

                if (fenced is not null)
                {
                    if (trimmed.StartsWith(FenceMark, StringComparison.Ordinal))
                    {
                        blocks.Add(Fenced(fenced, language));
                        fenced = null;
                        language = string.Empty;
                        continue;
                    }

                    fenced.Add(line);
                    continue;
                }

                if (trimmed.StartsWith(FenceMark, StringComparison.Ordinal))
                {
                    fenced = [];
                    language = trimmed[FenceMark.Length..].Trim();
                    continue;
                }

                if (trimmed.Length == 0) continue;

                blocks.Add(ReadOne(trimmed));
            }

            // A fence somebody opened and never closed still holds writing they meant to be read as code. Dropping
            // it would lose the end of a piece over one missing line.
            if (fenced is not null) blocks.Add(Fenced(fenced, language));

            return blocks;
        }

        /// <summary> Makes one code block out of the lines collected inside a fence. </summary>
        /// <param name="lines"> The lines, exactly as they were typed. </param>
        /// <param name="language"> What the fence was labelled, empty when it carried no label. </param>
        /// <returns> The block. </returns>
        static ProseBlock Fenced(List<string> lines, string language)
            => new(ProseBlockKind.Code, string.Join(CodeLineEnding, lines), language);

        /// <summary> Decides what one line is, from the characters it opens with. </summary>
        /// <param name="line"> The line, already trimmed. </param>
        /// <returns> The block it makes. </returns>
        static ProseBlock ReadOne(string line)
        {
            if (line.StartsWith(HeadingMark, StringComparison.Ordinal)) return new ProseBlock(ProseBlockKind.Heading, line[HeadingMark.Length..]);
            if (line.StartsWith(QuoteMark, StringComparison.Ordinal)) return new ProseBlock(ProseBlockKind.Quote, line[QuoteMark.Length..]);
            if (line.StartsWith(BulletMark, StringComparison.Ordinal)) return new ProseBlock(ProseBlockKind.Bullet, line[BulletMark.Length..]);
            if (line.StartsWith(StarBulletMark, StringComparison.Ordinal)) return new ProseBlock(ProseBlockKind.Bullet, line[StarBulletMark.Length..]);

            return new ProseBlock(ProseBlockKind.Paragraph, line);
        }
    }
}
