namespace ChaySocial.MainProject.Moderation
{
    /// <summary> The kind of harm a line carries, when it carries any. </summary>
    public enum ContentCategory
    {
        /// <summary> Nothing worth naming. </summary>
        None,

        /// <summary> Swearing and abuse aimed at a person. </summary>
        Insult,

        /// <summary> An attack on who somebody is rather than on what they said. </summary>
        Slur,

        /// <summary> A threat of harm, including telling somebody to end their life. </summary>
        Threat,

        /// <summary> Explicit sexual content. </summary>
        Sexual
    }

    /// <summary>
    /// How far a line goes. The bands exist so a reader can say where their own line is instead of being handed
    /// somebody else's, and so the default can sit at the top band where almost nobody disagrees.
    /// </summary>
    public enum ContentBand
    {
        /// <summary> Ordinary writing. </summary>
        Nothing,

        /// <summary> Swearing, but not at anyone. A person cursing the weather lands here and is never covered. </summary>
        Coarse,

        /// <summary> Aimed at somebody and meant to wound. </summary>
        Abusive,

        /// <summary> Threats and the heaviest attacks; the one band covered by default. </summary>
        Extreme
    }

    /// <summary>
    /// What a line was judged to be. A verdict is a reading, never a permission: nothing in this application deletes,
    /// blocks or reports on the strength of one. The most it does is draw a curtain the reader can lift.
    /// </summary>
    /// <param name="Category"> The kind of harm that weighed most. </param>
    /// <param name="Band"> How far the line goes. </param>
    /// <param name="Score"> The weight behind the band, kept so the reason can be shown rather than asserted. </param>
    /// <param name="DirectedAtSomebody"> True when the line is aimed at a person rather than said in the air. </param>
    public readonly record struct ContentVerdict(ContentCategory Category, ContentBand Band, int Score, bool DirectedAtSomebody)
    {
        /// <summary> A line with nothing in it worth naming. </summary>
        public static readonly ContentVerdict Clean = new(ContentCategory.None, ContentBand.Nothing, 0, false);

        /// <summary> True when this verdict reaches or passes the band a reader asked to be warned about. </summary>
        /// <param name="covered"> The lowest band that reader wants covered. </param>
        /// <returns> True when the line should be drawn behind a curtain for them. </returns>
        public bool ReachesBand(ContentBand covered) => covered != ContentBand.Nothing && Band >= covered;

        /// <summary> A short English phrase naming what was found, for the curtain to show instead of the words. </summary>
        /// <returns> What a reader is told before deciding whether to look. </returns>
        public string Reason() => Category switch
        {
            ContentCategory.Insult => DirectedAtSomebody ? "insults someone" : "coarse language",
            ContentCategory.Slur => "attacks who someone is",
            ContentCategory.Threat => "threatens harm",
            ContentCategory.Sexual => "sexually explicit",
            _ => "nothing"
        };
    }
}
