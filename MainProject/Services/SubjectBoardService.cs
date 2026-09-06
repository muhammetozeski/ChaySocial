using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary> How the square is ordered, which is the reader's choice and nobody else's. </summary>
    public enum SubjectBoardOrder
    {
        /// <summary> The most named first. </summary>
        MostNamed,

        /// <summary> The least named first, so a subject two people write under quietly still gets a door. </summary>
        LeastNamed,

        /// <summary> Whatever was named most recently. </summary>
        NewestFirst,

        /// <summary> Whatever has been going on longest inside the window that was read. </summary>
        LongestRunning
    }

    /// <summary> Where one subject stands, built from the arithmetic the reader is shown. </summary>
    /// <param name="Subject"> The subject, in the form it is stored under. </param>
    /// <param name="MentionCount"> How many times it was named inside the window that was read. </param>
    /// <param name="NewestAtUnixMs"> When it was last named in that window. </param>
    /// <param name="OldestAtUnixMs"> When it was first named in that window. </param>
    public readonly record struct SubjectStanding(string Subject, int MentionCount, long NewestAtUnixMs, long OldestAtUnixMs)
    {
        /// <summary> How long this subject has been going inside the window that was read. </summary>
        public long RunningForMs => NewestAtUnixMs - OldestAtUnixMs;
    }

    /// <summary>
    /// The public square: which subjects exist and how they stand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Following a subject shipped, and then nothing in the app told a reader which subjects there were — the only
    /// way to reach one was to happen to see a mark inside a post that happened to be on screen. This is the door.
    /// </para>
    /// <para>
    /// The arithmetic is one number a reader can check: how many index rows name a subject inside the window that
    /// was read. Nothing is weighted, nothing is boosted, and the window is stated on the page rather than hidden,
    /// which is what keeps every count on it true.
    /// </para>
    /// </remarks>
    public static class SubjectBoardService
    {
        /// <summary>
        /// Index rows read in one pass. One bounded read of the newest mentions, grouped on this device — the same
        /// shape a search uses, and for the same reason: a store cannot be asked to count for us without being told
        /// what everybody is interested in.
        /// </summary>
        public const int SubjectMentionsScanned = 500;

        /// <summary> Reads the square. </summary>
        /// <param name="order"> How the reader asked for it to be ordered. </param>
        /// <param name="limit"> Largest number of subjects to return. </param>
        /// <returns> Where each subject stands, in the reader's order. </returns>
        public static async Task<IReadOnlyList<SubjectStanding>> ReadAsync(SubjectBoardOrder order, int limit)
        {
            if (limit <= 0) return [];

            DocumentQuery<SubjectMentionData> query = new DocumentQuery<SubjectMentionData>()
                .WithSort(SubjectMentionData.CreatedAtField, descending: true)
                .WithLimit(SubjectMentionsScanned);

            IReadOnlyList<SubjectMentionData> mentions = (await AppServices.Documents.QueryAsync(query)).Documents;
            if (mentions.Count == 0) return [];

            List<SubjectStanding> standings =
            [
                .. mentions
                    .GroupBy(mention => mention.Subject, StringComparer.Ordinal)
                    .Select(named => new SubjectStanding(
                        named.Key,
                        named.Count(),
                        named.Max(mention => mention.CreatedAtUnixMs),
                        named.Min(mention => mention.CreatedAtUnixMs)))
            ];

            return [.. Order(standings, order).Take(limit)];
        }

        /// <summary> Puts the standings in the reader's order. </summary>
        /// <param name="standings"> Every subject found in the window. </param>
        /// <param name="order"> The order asked for. </param>
        /// <returns> The same standings, ordered. </returns>
        /// <remarks>
        /// Every order breaks its ties on the newest mention, so a page of subjects that are level on the number
        /// being sorted still reads as a live square rather than as an arbitrary list.
        /// </remarks>
        static IEnumerable<SubjectStanding> Order(List<SubjectStanding> standings, SubjectBoardOrder order) => order switch
        {
            SubjectBoardOrder.LeastNamed => standings
                .OrderBy(standing => standing.MentionCount)
                .ThenByDescending(standing => standing.NewestAtUnixMs),

            SubjectBoardOrder.NewestFirst => standings.OrderByDescending(standing => standing.NewestAtUnixMs),

            SubjectBoardOrder.LongestRunning => standings
                .OrderByDescending(standing => standing.RunningForMs)
                .ThenByDescending(standing => standing.NewestAtUnixMs),

            _ => standings
                .OrderByDescending(standing => standing.MentionCount)
                .ThenByDescending(standing => standing.NewestAtUnixMs)
        };
    }
}
