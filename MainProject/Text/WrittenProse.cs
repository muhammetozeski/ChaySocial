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
        Bullet
    }

    /// <summary> One block of a long piece: what it is, and the line to draw. </summary>
    /// <param name="Kind"> What this block is. </param>
    /// <param name="Text"> The line without the characters that named its kind. </param>
    public readonly record struct ProseBlock(ProseBlockKind Kind, string Text);

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

        /// <summary> The line endings a body may arrive with, whichever machine it was typed on. </summary>
        static readonly string[] LineEndings = ["\r\n", "\n", "\r"];

        /// <summary>
        /// Splits a body into its blocks. Blank lines separate blocks and are not blocks themselves, so a piece
        /// spaced out generously draws the same as one spaced out sparingly.
        /// </summary>
        /// <param name="body"> The long body as it was written. </param>
        /// <returns> Its blocks in order; an empty body comes back with none. </returns>
        public static IReadOnlyList<ProseBlock> Read(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return [];

            List<ProseBlock> blocks = [];

            foreach (string line in body.Split(LineEndings, StringSplitOptions.None))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                blocks.Add(ReadOne(trimmed));
            }

            return blocks;
        }

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
