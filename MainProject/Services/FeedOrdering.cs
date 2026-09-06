using System.Globalization;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Puts a page of feed lines in the order the reader asked for, and says out loud why each line landed where it
    /// did. Pure arithmetic on lists already in memory: nothing here reads the store, and nothing here is sent
    /// anywhere.
    /// </summary>
    /// <remarks>
    /// It orders the page that was drawn, not the whole platform — the store hands back its own newest-first page
    /// first, and this reorders that. The strip above the feed says so, and saying so is what keeps every receipt
    /// under it true.
    /// </remarks>
    public static class FeedOrdering
    {
        /// <summary> Ordinal endings, indexed by the last digit of a position. </summary>
        static readonly string[] OrdinalEndings = ["th", "st", "nd", "rd", "th", "th", "th", "th", "th", "th"];

        /// <summary> Positions whose ending is "th" whatever their last digit is: eleventh, twelfth, thirteenth. </summary>
        const int TeensStart = 11;

        /// <summary> Last of those positions. </summary>
        const int TeensEnd = 13;

        /// <summary> Base the last digit and the teens are read out of. </summary>
        const int Ten = 10;

        /// <summary> How many of them a hundred holds, for spotting the teens in any hundred. </summary>
        const int Hundred = 100;

        /// <summary>
        /// Puts a page of feed lines in one order, and lays the lines from outside the reader's own people at the
        /// spacing their dial asks for.
        /// </summary>
        /// <param name="entries"> The page as it came out of the store. </param>
        /// <param name="engagements"> The counts under each post, keyed by post id. </param>
        /// <param name="order"> The order the reader chose. </param>
        /// <param name="shuffleSeed"> The seed the shuffled order deals from. </param>
        /// <returns> The same lines, in the reader's order and at the reader's spacing. </returns>
        /// <remarks>
        /// The spacing is applied here rather than where the strangers were read, because this runs afterwards:
        /// ordering a mixed page by anything at all sweeps every stranger into one clump, which is neither the
        /// order nor the mix anybody asked for. Each stream is ordered on its own and then the two are laid
        /// together, so both choices hold at once.
        /// </remarks>
        public static IReadOnlyList<FeedEntry> Apply(
            IReadOnlyList<FeedEntry> entries,
            IReadOnlyDictionary<string, PostEngagement> engagements,
            FeedOrder order,
            int shuffleSeed)
        {
            int gap = StrangerShare.LinesFollowedBetweenStrangers(StrangerShare.Level);
            if (gap <= 0) return InOneOrder(entries, engagements, order, shuffleSeed);

            FeedEntry[] chosen = [.. entries.Where(entry => !entry.CameFromOutside)];
            FeedEntry[] outside = [.. entries.Where(entry => entry.CameFromOutside)];

            if (chosen.Length == 0 || outside.Length == 0) return InOneOrder(entries, engagements, order, shuffleSeed);

            return LayTogether(
                InOneOrder(chosen, engagements, order, shuffleSeed),
                InOneOrder(outside, engagements, order, shuffleSeed),
                gap);
        }

        /// <summary>
        /// Lays two ordered streams together: so many of the reader's own, then one from outside, and so on until
        /// both run out.
        /// </summary>
        /// <param name="chosen"> The reader's own lines, in their order. </param>
        /// <param name="outside"> The lines from outside, in the same order. </param>
        /// <param name="gap"> How many of the reader's own are laid out between two from outside. </param>
        /// <returns> Every line from both, in one page. </returns>
        static IReadOnlyList<FeedEntry> LayTogether(
            IReadOnlyList<FeedEntry> chosen,
            IReadOnlyList<FeedEntry> outside,
            int gap)
        {
            List<FeedEntry> laid = new(chosen.Count + outside.Count);
            int nextChosen = 0;
            int nextOutside = 0;

            while (nextChosen < chosen.Count || nextOutside < outside.Count)
            {
                for (int placed = 0; placed < gap && nextChosen < chosen.Count; placed++)
                {
                    laid.Add(chosen[nextChosen++]);
                }

                if (nextOutside < outside.Count) laid.Add(outside[nextOutside++]);

                // Neither stream moved, which means one is empty and the other is out of turns: pour the rest.
                if (nextChosen >= chosen.Count && nextOutside < outside.Count)
                {
                    while (nextOutside < outside.Count) laid.Add(outside[nextOutside++]);
                }
            }

            return laid;
        }

        /// <summary> Puts one stream of lines in one order. </summary>
        /// <param name="entries"> The lines. </param>
        /// <param name="engagements"> The counts under each post, keyed by post id. </param>
        /// <param name="order"> The order the reader chose. </param>
        /// <param name="shuffleSeed"> The seed the shuffled order deals from. </param>
        /// <returns> The same lines, ordered. </returns>
        static IReadOnlyList<FeedEntry> InOneOrder(
            IReadOnlyList<FeedEntry> entries,
            IReadOnlyDictionary<string, PostEngagement> engagements,
            FeedOrder order,
            int shuffleSeed) => order switch
            {
                FeedOrder.Oldest => [.. entries.OrderBy(entry => entry.SortedAtUnixMs)],

                // Ties are broken by the newest, so the many posts nobody has answered yet still read as a feed
                // rather than as an archive opened at the wrong end.
                FeedOrder.FewestChaysFirst =>
                [
                    .. entries
                        .OrderBy(entry => AnswersTo(entry, engagements))
                        .ThenByDescending(entry => entry.SortedAtUnixMs)
                ],

                FeedOrder.OneEachTurn => TakeTurns(entries),

                FeedOrder.Shuffled => Shuffle(entries, shuffleSeed),

                _ => [.. entries.OrderByDescending(entry => entry.SortedAtUnixMs)]
            };

        /// <summary> Writes the one line that says why a post is where it is. </summary>
        /// <param name="entry"> The line being drawn. </param>
        /// <param name="engagement"> That post's counts. </param>
        /// <param name="order"> The order in force. </param>
        /// <param name="position"> Where it landed, counting from one. </param>
        /// <returns> A short English sentence built from the numbers that actually decided it. </returns>
        public static string Explain(FeedEntry entry, PostEngagement engagement, FeedOrder order, int position)
        {
            string age = RelativeTimeFormatter.Format(entry.SortedAtUnixMs);
            string reason = order switch
            {
                FeedOrder.Newest => $"newest first — written {age}",
                FeedOrder.Oldest => $"oldest first — written {age}",
                FeedOrder.FewestChaysFirst =>
                    $"fewest chays first — {Count(engagement.LikeCount, "chay")}, "
                    + $"{Count(engagement.CommentCount, "reply", "replies")}, written {age}",
                FeedOrder.OneEachTurn => $"one each, in turn — written {age}",
                FeedOrder.Shuffled => $"shuffled — written {age}",
                _ => age
            };

            // Said on the line itself rather than in a legend somewhere, because the whole point of the dial is
            // that a reader can tell which lines it put there.
            return entry.CameFromOutside
                ? $"{Ordinal(position)}: {reason} — {FromOutsideNote}"
                : $"{Ordinal(position)}: {reason}";
        }

        /// <summary>
        /// What a line let in by the dial says about itself. The placeholder takes the setting that put it there,
        /// so the reader is told which of their own choices to change if they would rather it had not.
        /// </summary>
        const string FromOutsideNoteFormat = "you don't follow them; your dial is set to {0}";

        /// <summary> That note with the setting currently in force written into it. </summary>
        static string FromOutsideNote
            => string.Format(FromOutsideNoteFormat, StrangerShare.Describe(StrangerShare.Level).ToLowerInvariant());

        /// <summary> How much a post has been answered, which is what the fewest-first order sorts on. </summary>
        /// <param name="entry"> The line being weighed. </param>
        /// <param name="engagements"> The counts, keyed by post id. </param>
        /// <returns> Chays and replies added together. </returns>
        static int AnswersTo(FeedEntry entry, IReadOnlyDictionary<string, PostEngagement> engagements)
        {
            PostEngagement engagement = engagements.GetValueOrDefault(entry.Post.PostId);
            return engagement.LikeCount + engagement.CommentCount;
        }

        /// <summary>
        /// Deals one post per account before anybody gets a second, keeping each account's own posts in the order
        /// they arrived in. One account writing all afternoon therefore reaches the reader once before anybody
        /// else's first post has to wait.
        /// </summary>
        /// <param name="entries"> The page as it came out of the store. </param>
        /// <returns> The same lines, dealt round by round. </returns>
        static List<FeedEntry> TakeTurns(IReadOnlyList<FeedEntry> entries)
        {
            List<List<FeedEntry>> byAuthor = [];
            Dictionary<string, int> whereAuthorSits = [];

            foreach (FeedEntry entry in entries)
            {
                if (!whereAuthorSits.TryGetValue(entry.Post.AuthorAddress, out int index))
                {
                    index = byAuthor.Count;
                    whereAuthorSits[entry.Post.AuthorAddress] = index;
                    byAuthor.Add([]);
                }

                byAuthor[index].Add(entry);
            }

            List<FeedEntry> dealt = new(entries.Count);
            for (int round = 0; dealt.Count < entries.Count; round++)
            {
                foreach (List<FeedEntry> written in byAuthor)
                {
                    if (round < written.Count) dealt.Add(written[round]);
                }
            }

            return dealt;
        }

        /// <summary> Shuffles a page from a seed, so the same seed always gives the same page. </summary>
        /// <param name="entries"> The page as it came out of the store. </param>
        /// <param name="shuffleSeed"> The seed to deal from. </param>
        /// <returns> The same lines, shuffled. </returns>
        static List<FeedEntry> Shuffle(IReadOnlyList<FeedEntry> entries, int shuffleSeed)
        {
            List<FeedEntry> shuffled = [.. entries];
            Random deal = new(shuffleSeed);

            for (int index = shuffled.Count - 1; index > 0; index--)
            {
                int swapWith = deal.Next(index + 1);
                (shuffled[index], shuffled[swapWith]) = (shuffled[swapWith], shuffled[index]);
            }

            return shuffled;
        }

        /// <summary> Writes a count with its noun, singular or plural. </summary>
        /// <param name="count"> How many. </param>
        /// <param name="singular"> The noun for one. </param>
        /// <param name="plural"> The noun for any other number; the singular plus an s when none is given. </param>
        /// <returns> The count and its noun. </returns>
        static string Count(int count, string singular, string? plural = null)
            => $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural ?? singular + "s")}";

        /// <summary> Writes a position as 1st, 2nd, 3rd, 4th and so on. </summary>
        /// <param name="position"> Where the line landed, counting from one. </param>
        /// <returns> The position with its ending. </returns>
        static string Ordinal(int position)
        {
            int inThisHundred = position % Hundred;
            string ending = inThisHundred is >= TeensStart and <= TeensEnd
                ? OrdinalEndings[0]
                : OrdinalEndings[position % Ten];

            return $"{position.ToString(CultureInfo.InvariantCulture)}{ending}";
        }
    }
}
