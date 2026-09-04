using ChaySocial.MainProject.DataModels;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Builds the two lists of posts a reader is shown: the following feed, made only of the accounts they chose to
    /// follow, and the discover feed, which is the wall everybody shares. Both are assembled on the reader's own
    /// device — the store is asked for ordinary pages of posts and this class decides which of them survive — so an
    /// account the reader blocked, and an account that blocked the reader, stay out of sight without the server
    /// having to be trusted to honour either block.
    /// </summary>
    public static class FeedService
    {
        /// <summary> Posts handed back in one page of either feed. </summary>
        public const int FeedPageSize = 30;

        /// <summary> Posts read from each followed account before the merged list is cut down to the page size. </summary>
        const int PostsReadPerFollowedAccount = 20;

        /// <summary>
        /// Followed accounts the feed is built from. Named here rather than left to
        /// <see cref="FollowService.FollowPageSize"/>, which sizes a page of a list on screen and is far smaller
        /// than what a feed should draw on.
        /// </summary>
        const int FollowedAccountsPerFeed = 200;

        /// <summary>
        /// Followed accounts whose posts are read at the same time. Every followed account is read, but in batches
        /// of this size, so following hundreds of people does not open hundreds of requests at once.
        /// </summary>
        const int AuthorsReadAtOnce = 8;

        /// <summary>
        /// How many times the requested page size the discover feed reads from the wall. The store cannot express
        /// "written by none of these accounts", so blocked authors are dropped after reading; reading this much
        /// extra keeps the page full when several of the posts read fall away.
        /// </summary>
        const int DiscoverReadMultiplier = 3;

        /// <summary>
        /// Reads the newest posts written by the accounts one reader follows, merged into a single newest-first list.
        /// Following nobody gives an empty list — what to show in its place is the page's decision, not this service's.
        /// </summary>
        /// <param name="viewerAddress"> Address of the reader whose follow list and blocks apply; empty when nobody is signed in. </param>
        /// <param name="limit"> Largest number of posts to return. </param>
        /// <returns> Posts by followed accounts, newest first, with blocked accounts on either side left out. </returns>
        public static async Task<IReadOnlyList<PostData>> ReadFollowingFeedAsync(string viewerAddress, int limit = FeedPageSize)
        {
            if (string.IsNullOrEmpty(viewerAddress) || limit <= 0) return [];

            IReadOnlyList<string> following = await FollowService.ReadFollowingAsync(viewerAddress, FollowedAccountsPerFeed);
            if (following.Count == 0) return [];

            HashSet<string> hidden = await ReadHiddenAddressesAsync(viewerAddress);
            string[] authors = [.. following.Distinct().Where(address => !hidden.Contains(address))];
            if (authors.Length == 0) return [];

            return NewestFirst(await ReadPostsByAuthorsAsync(authors), limit);
        }

        /// <summary> Reads the newest posts from the whole app, for a reader who is looking beyond the accounts they follow. </summary>
        /// <param name="viewerAddress"> Address of the reader whose blocks apply; empty when nobody is signed in, and then nothing is hidden. </param>
        /// <param name="limit"> Largest number of posts to return. </param>
        /// <returns> Posts from every account, newest first, with blocked accounts on either side left out. </returns>
        public static async Task<IReadOnlyList<PostData>> ReadDiscoverFeedAsync(string viewerAddress, int limit = FeedPageSize)
        {
            if (limit <= 0) return [];

            HashSet<string> hidden = await ReadHiddenAddressesAsync(viewerAddress);
            IReadOnlyList<PostData> wall = await WallService.ReadWallAsync(
                hidden.Count == 0 ? limit : limit * DiscoverReadMultiplier);

            return NewestFirst(wall.Where(post => !hidden.Contains(post.AuthorAddress)), limit);
        }

        /// <summary>
        /// Collects the addresses whose posts this reader must not see: the accounts they blocked, and the accounts
        /// that blocked them. Both directions are read together because either one alone hides only half of a
        /// falling-out.
        /// </summary>
        /// <param name="viewerAddress"> Address of the reader, or empty when nobody is signed in. </param>
        /// <returns> The addresses to filter out, empty when nobody is signed in. </returns>
        static async Task<HashSet<string>> ReadHiddenAddressesAsync(string viewerAddress)
        {
            if (string.IsNullOrEmpty(viewerAddress)) return [];

            Task<IReadOnlyList<string>> blocked = ModerationService.ReadBlockedAddressesAsync(viewerAddress);
            Task<IReadOnlyList<string>> blockedBy = ModerationService.ReadBlockedByAddressesAsync(viewerAddress);
            await Task.WhenAll(blocked, blockedBy);

            return [.. await blocked, .. await blockedBy];
        }

        /// <summary>
        /// Reads each author's own posts and pours them into one list. The store has no "written by any of these"
        /// operator, so one read per author is what there is; they run in batches rather than one after another.
        /// </summary>
        /// <param name="authors"> Addresses whose posts to read. </param>
        /// <returns> Every post read, in no particular order. </returns>
        static async Task<List<PostData>> ReadPostsByAuthorsAsync(IReadOnlyList<string> authors)
        {
            List<PostData> collected = [];

            for (int firstInBatch = 0; firstInBatch < authors.Count; firstInBatch += AuthorsReadAtOnce)
            {
                IEnumerable<string> batch = authors.Skip(firstInBatch).Take(AuthorsReadAtOnce);

                IReadOnlyList<PostData>[] pages = await Task.WhenAll(
                    batch.Select(author => WallService.ReadAuthorPostsAsync(author, PostsReadPerFollowedAccount)));

                foreach (IReadOnlyList<PostData> page in pages) collected.AddRange(page);
            }

            return collected;
        }

        /// <summary> Orders posts newest first, drops any post that arrived twice, and cuts the result to one page. </summary>
        /// <param name="posts"> Posts gathered from one or more reads. </param>
        /// <param name="limit"> Largest number of posts to keep. </param>
        /// <returns> The page to show. </returns>
        static IReadOnlyList<PostData> NewestFirst(IEnumerable<PostData> posts, int limit)
            => [.. posts.DistinctBy(post => post.PostId).OrderByDescending(post => post.CreatedAtUnixMs).Take(limit)];
    }
}
