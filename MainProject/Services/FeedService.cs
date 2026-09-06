using ChaySocial.MainProject.DataModels;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// One line of a feed: a post, plus how it got there. A post written by somebody the reader follows and a post
    /// passed on by them are the same post drawn the same way — what differs is whose wall carried it and when,
    /// which is what a feed is ordered by.
    /// </summary>
    /// <param name="Post"> The post to draw. </param>
    /// <param name="ReposterAddress"> Address of the account that passed it on, or empty when it arrived on its own. </param>
    /// <param name="SortedAtUnixMs"> When it reached the feed: the repost's time, or the post's own. </param>
    /// <param name="CameFromOutside"> True when the reader's own dial let this line in from an account they do not follow. </param>
    public readonly record struct FeedEntry(
        PostData Post,
        string ReposterAddress,
        long SortedAtUnixMs,
        bool CameFromOutside = false)
    {
        /// <summary> True when this line reached the feed through somebody else's wall. </summary>
        public bool IsRepost => ReposterAddress.Length > 0;

        /// <summary> A post that arrived on its own. </summary>
        /// <param name="post"> The post. </param>
        /// <returns> The feed line for it. </returns>
        public static FeedEntry ForPost(PostData post) => new(post, string.Empty, post.CreatedAtUnixMs);

        /// <summary> A post that somebody passed on. </summary>
        /// <param name="post"> The original post. </param>
        /// <param name="repost"> The record that carried it. </param>
        /// <returns> The feed line for it, timed by the passing on rather than the writing. </returns>
        public static FeedEntry ForRepost(PostData post, RepostData repost)
            => new(post, repost.ReposterAddress, repost.CreatedAtUnixMs);

        /// <summary> A post from outside the reader's own people, let in by their own dial. </summary>
        /// <param name="post"> The post. </param>
        /// <returns> The feed line for it, marked so the receipt above it can say where it came from. </returns>
        public static FeedEntry ForStranger(PostData post)
            => new(post, string.Empty, post.CreatedAtUnixMs, CameFromOutside: true);
    }

    /// <summary>
    /// The numbers a post card draws under a post. Read once per post while a page loads, so a repaint never goes
    /// back to the store for a count it already has.
    /// </summary>
    /// <param name="LikeCount"> How many accounts liked the post. </param>
    /// <param name="IsLikedByViewer"> True when the reader is one of those likers, which fills the heart. </param>
    /// <param name="CommentCount"> How many comments the post carries. </param>
    /// <param name="RepostCount"> How many accounts carried the post onto their own wall. </param>
    /// <param name="IsRepostedByViewer"> True when the reader is one of those, which lights the arrows. </param>
    public readonly record struct PostEngagement(
        int LikeCount,
        bool IsLikedByViewer,
        int CommentCount,
        int RepostCount,
        bool IsRepostedByViewer);

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

        /// <summary> Reposts read from each followed account, alongside their own posts. </summary>
        const int RepostsReadPerFollowedAccount = 20;

        /// <summary>
        /// Followed accounts the feed is built from. Named here rather than left to
        /// <see cref="FollowService.FollowPageSize"/>, which sizes a page of a list on screen and is far smaller
        /// than what a feed should draw on.
        /// </summary>
        const int FollowedAccountsPerFeed = 200;

        /// <summary> Most followed subjects a feed gathers from in one pass. </summary>
        const int FollowedSubjectsPerFeed = 50;

        /// <summary> How many posts are read for each followed subject. </summary>
        const int PostsReadPerFollowedSubject = 20;

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
        public static async Task<IReadOnlyList<FeedEntry>> ReadFollowingFeedAsync(string viewerAddress, int limit = FeedPageSize)
        {
            if (string.IsNullOrEmpty(viewerAddress) || limit <= 0) return [];

            // Both are read before anything gives up: a reader who follows no accounts but does follow a subject
            // has a feed, and returning early on the account list alone would leave it permanently empty.
            IReadOnlyList<string> following = await FollowService.ReadFollowingAsync(viewerAddress, FollowedAccountsPerFeed);
            IReadOnlyList<string> subjects = await SubjectFollowService.ReadFollowedSubjectsAsync(viewerAddress, FollowedSubjectsPerFeed);

            // With the dial off these two are the end of it. With the dial on they are not: a reader who follows
            // nobody yet is exactly who a page of strangers is for, and giving up here would leave the tab empty
            // for the one person the setting was turned on by.
            bool lettingStrangersIn = StrangerShare.IsOn;
            if (following.Count == 0 && subjects.Count == 0 && !lettingStrangersIn) return [];

            HashSet<string> hidden = await ModerationService.ReadShutOutAddressesAsync(viewerAddress);
            string[] authors = [.. following.Distinct().Where(address => !hidden.Contains(address))];
            if (authors.Length == 0 && subjects.Count == 0 && !lettingStrangersIn) return [];

            Task<List<PostData>> postsRead = ReadPostsByAuthorsAsync(authors);
            Task<List<RepostData>> repostsRead = ReadRepostsByAccountsAsync(authors);
            Task<List<PostData>> subjectPostsRead = ReadPostsBySubjectsAsync(subjects, hidden);
            await Task.WhenAll(postsRead, repostsRead, subjectPostsRead);

            IEnumerable<FeedEntry> written = (await postsRead).Select(FeedEntry.ForPost);
            IEnumerable<FeedEntry> named = (await subjectPostsRead).Select(FeedEntry.ForPost);
            IEnumerable<FeedEntry> passedOn = await ResolveRepostsAsync(await repostsRead, hidden);

            // A post that arrives both because its author is followed and because it names a followed subject is
            // drawn once: NewestFirst already keys on the post and whoever passed it on.
            IReadOnlyList<FeedEntry> chosen = NewestFirst([.. written, .. named, .. passedOn], limit);

            return lettingStrangersIn
                ? await LetStrangersInAsync(viewerAddress, chosen, authors, limit)
                : chosen;
        }

        /// <summary>
        /// Adds lines from outside the reader's own people to the page, in the proportion their dial asks for.
        /// </summary>
        /// <param name="viewerAddress"> The reader, whose blocks the wall read already applies. </param>
        /// <param name="chosen"> The feed as their own follows and subjects made it, newest first. </param>
        /// <param name="authors"> Accounts they follow, so nobody already followed is offered as a stranger. </param>
        /// <param name="limit"> Largest number of lines to return. </param>
        /// <returns> Both kinds of line together, marked, and never more than was asked for. </returns>
        /// <remarks>
        /// This decides how many come from outside, not where they sit. Laying them out is the ordering's job,
        /// because the ordering runs afterwards and would undo any placement made here — a reader who asked for
        /// fewest-chays-first would have got every stranger swept into one clump at the top of the page.
        /// </remarks>
        static async Task<IReadOnlyList<FeedEntry>> LetStrangersInAsync(
            string viewerAddress,
            IReadOnlyList<FeedEntry> chosen,
            IReadOnlyCollection<string> authors,
            int limit)
        {
            int wanted = StrangerShare.StrangersOnAPageOf(limit, StrangerShare.Level);
            if (wanted <= 0) return chosen;

            // Room for as many as the dial asks for, and for as many more as the reader's own people leave empty:
            // somebody who follows nobody should get a full page rather than a third of one.
            int room = Math.Max(wanted, limit - chosen.Count);

            // The wall read already leaves out both directions of a block, so nothing here is filtered for that a
            // second time.
            IReadOnlyList<FeedEntry> outside = await ReadDiscoverFeedAsync(viewerAddress, limit);

            HashSet<string> alreadyFollowed = [.. authors, viewerAddress];
            HashSet<string> alreadyShown = [.. chosen.Select(entry => entry.Post.PostId)];

            FeedEntry[] strangers =
            [
                .. outside
                    .Where(entry => !alreadyFollowed.Contains(entry.Post.AuthorAddress))
                    .Where(entry => alreadyShown.Add(entry.Post.PostId))
                    .Take(room)
                    .Select(entry => FeedEntry.ForStranger(entry.Post))
            ];

            if (strangers.Length == 0) return chosen;

            // The reader's own lines give way to make room, newest kept, so the page stays the length asked for.
            IEnumerable<FeedEntry> kept = chosen.Take(Math.Max(limit - strangers.Length, 0));

            return [.. kept, .. strangers];
        }

        /// <summary> Reads the newest posts from the whole app, for a reader who is looking beyond the accounts they follow. </summary>
        /// <param name="viewerAddress"> Address of the reader whose blocks apply; empty when nobody is signed in, and then nothing is hidden. </param>
        /// <param name="limit"> Largest number of posts to return. </param>
        /// <returns> Posts from every account, newest first, with blocked accounts on either side left out. </returns>
        public static async Task<IReadOnlyList<FeedEntry>> ReadDiscoverFeedAsync(string viewerAddress, int limit = FeedPageSize)
        {
            if (limit <= 0) return [];

            HashSet<string> hidden = await ModerationService.ReadShutOutAddressesAsync(viewerAddress);
            int readSize = hidden.Count == 0 ? limit : limit * DiscoverReadMultiplier;

            Task<IReadOnlyList<PostData>> wallRead = WallService.ReadWallAsync(readSize);
            Task<IReadOnlyList<RepostData>> repostsRead = WallService.ReadRecentRepostsAsync(readSize);
            await Task.WhenAll(wallRead, repostsRead);

            IEnumerable<FeedEntry> written = (await wallRead)
                .Where(post => !hidden.Contains(post.AuthorAddress))
                .Select(FeedEntry.ForPost);

            IEnumerable<FeedEntry> passedOn = await ResolveRepostsAsync(
                (await repostsRead).Where(repost => !hidden.Contains(repost.ReposterAddress)), hidden);

            return NewestFirst([.. written, .. passedOn], limit);
        }

        /// <summary>
        /// Reads one account's own wall: what they wrote and what they passed on, in the order the two happened.
        /// This is a wall rather than a feed, so nothing is filtered — a reader who opens a profile is asking to
        /// see that account, and the page above decides what to say about a blocked one.
        /// </summary>
        /// <param name="accountAddress"> Address of the account whose wall to read. </param>
        /// <param name="limit"> Largest number of lines to return. </param>
        /// <returns> That account's wall, newest first. </returns>
        public static async Task<IReadOnlyList<FeedEntry>> ReadAccountWallAsync(string accountAddress, int limit = FeedPageSize)
        {
            if (string.IsNullOrEmpty(accountAddress) || limit <= 0) return [];

            Task<IReadOnlyList<PostData>> postsRead = WallService.ReadAuthorPostsAsync(accountAddress, limit);
            Task<IReadOnlyList<RepostData>> repostsRead = WallService.ReadAccountRepostsAsync(accountAddress, limit);
            await Task.WhenAll(postsRead, repostsRead);

            IEnumerable<FeedEntry> written = (await postsRead).Select(FeedEntry.ForPost);
            IEnumerable<FeedEntry> passedOn = await ResolveRepostsAsync(await repostsRead, []);

            return NewestFirst([.. written, .. passedOn], limit);
        }

        /// <summary> Reads the counts every post in a list needs, all posts at once. </summary>
        /// <param name="posts"> The posts about to be drawn. </param>
        /// <param name="viewerAddress"> Address of the reader, so their own like and repost light up. </param>
        /// <returns> The counts keyed by post id. </returns>
        public static async Task<Dictionary<string, PostEngagement>> ReadEngagementsAsync(
            IReadOnlyList<PostData> posts,
            string viewerAddress)
        {
            // Read once for the page rather than once per card: a page of thirty pays two extra queries for this
            // instead of sixty.
            HashSet<string> shutOut = await ModerationService.ReadShutOutAddressesAsync(viewerAddress);

            PostEngagement[] measured = await Task.WhenAll(
                posts.Select(post => ReadEngagementAsync(post, viewerAddress, shutOut)));

            Dictionary<string, PostEngagement> byPostId = new(posts.Count);
            for (int index = 0; index < posts.Count; index++)
            {
                byPostId[posts[index].PostId] = measured[index];
            }

            return byPostId;
        }

        /// <summary> Reads one post's likers, reposters and comment count, all at once because none needs the others. </summary>
        /// <param name="post"> The post to measure. </param>
        /// <param name="viewerAddress"> Address of the reader, looked for among the likers and the reposters. </param>
        /// <param name="shutOut"> Accounts the reader has blocked or been blocked by; read here when a caller has none to hand. </param>
        /// <returns> The numbers that post's card draws. </returns>
        public static async Task<PostEngagement> ReadEngagementAsync(
            PostData post,
            string viewerAddress,
            IReadOnlySet<string>? shutOut = null)
        {
            shutOut ??= await ModerationService.ReadShutOutAddressesAsync(viewerAddress);

            Task<IReadOnlyList<string>> likersRead = WallService.ReadLikersAsync(post.PostId);
            Task<IReadOnlyList<string>> repostersRead = WallService.ReadRepostersAsync(post.PostId);
            Task<int> commentCountRead = CommentService.CountForPostAsync(post, shutOut);
            await Task.WhenAll(likersRead, repostersRead, commentCountRead);

            IReadOnlyList<string> likers = await likersRead;
            IReadOnlyList<string> reposters = await repostersRead;

            return new PostEngagement(
                likers.Count,
                likers.Contains(viewerAddress),
                await commentCountRead,
                reposters.Count,
                reposters.Contains(viewerAddress));
        }

        /// <summary>
        /// Reads each author's own posts and pours them into one list. The store has no "written by any of these"
        /// operator, so one read per author is what there is; they run in batches rather than one after another.
        /// </summary>
        /// <param name="authors"> Addresses whose posts to read. </param>
        /// <returns> Every post read, in no particular order. </returns>
        /// <summary>
        /// Reads the newest posts naming each followed subject and pours them into one list, in batches for the
        /// same reason the authors are read in batches.
        /// </summary>
        /// <param name="subjects"> Subjects the reader follows, in their stored form. </param>
        /// <param name="hidden"> Addresses this reader has shut out; their posts are dropped here too. </param>
        /// <returns> Every post read, in no particular order. </returns>
        /// <remarks>
        /// Blocking somebody has to hold whichever way a post arrives. Reaching a reader through a subject they
        /// follow would otherwise be a way around a block, which would make blocking worth nothing.
        /// </remarks>
        static async Task<List<PostData>> ReadPostsBySubjectsAsync(IReadOnlyList<string> subjects, HashSet<string> hidden)
        {
            List<PostData> collected = [];

            for (int firstInBatch = 0; firstInBatch < subjects.Count; firstInBatch += AuthorsReadAtOnce)
            {
                IEnumerable<string> batch = subjects.Skip(firstInBatch).Take(AuthorsReadAtOnce);

                IReadOnlyList<PostData>[] pages = await Task.WhenAll(
                    batch.Select(subject => WallService.ReadSubjectAsync(subject, PostsReadPerFollowedSubject)));

                foreach (IReadOnlyList<PostData> page in pages)
                    collected.AddRange(page.Where(post => !hidden.Contains(post.AuthorAddress)));
            }

            return collected;
        }

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

        /// <summary>
        /// Reads each account's own reposts and pours them into one list, in batches for the same reason the posts
        /// are read in batches.
        /// </summary>
        /// <param name="accounts"> Addresses whose reposts to read. </param>
        /// <returns> Every repost read, in no particular order. </returns>
        static async Task<List<RepostData>> ReadRepostsByAccountsAsync(IReadOnlyList<string> accounts)
        {
            List<RepostData> collected = [];

            for (int firstInBatch = 0; firstInBatch < accounts.Count; firstInBatch += AuthorsReadAtOnce)
            {
                IEnumerable<string> batch = accounts.Skip(firstInBatch).Take(AuthorsReadAtOnce);

                IReadOnlyList<RepostData>[] pages = await Task.WhenAll(
                    batch.Select(account => WallService.ReadAccountRepostsAsync(account, RepostsReadPerFollowedAccount)));

                foreach (IReadOnlyList<RepostData> page in pages) collected.AddRange(page);
            }

            return collected;
        }

        /// <summary>
        /// Turns reposts into feed lines by reading the posts they point at. A repost whose original is gone simply
        /// produces no line — the post was deleted, and a feed should not resurrect it — and so does one whose
        /// original author the reader has blocked, since passing a post on must not carry it past a block.
        /// </summary>
        /// <param name="reposts"> The reposts to resolve. </param>
        /// <param name="hidden"> Addresses whose posts this reader must not see. </param>
        /// <returns> One line per repost that still has a post behind it. </returns>
        static async Task<List<FeedEntry>> ResolveRepostsAsync(IEnumerable<RepostData> reposts, HashSet<string> hidden)
        {
            RepostData[] wanted = [.. reposts];
            if (wanted.Length == 0) return [];

            // Several accounts passing the same post on share one read of it.
            string[] postIds = [.. wanted.Select(repost => repost.PostId).Distinct()];
            PostData?[] originals = await Task.WhenAll(postIds.Select(WallService.ReadAsync));

            Dictionary<string, PostData> byPostId = new(postIds.Length);
            foreach (PostData? original in originals)
            {
                if (original is not null && !hidden.Contains(original.AuthorAddress)) byPostId[original.PostId] = original;
            }

            List<FeedEntry> lines = new(wanted.Length);
            foreach (RepostData repost in wanted)
            {
                if (byPostId.TryGetValue(repost.PostId, out PostData? post)) lines.Add(FeedEntry.ForRepost(post, repost));
            }

            return lines;
        }

        /// <summary>
        /// Orders lines newest first, drops any that arrived twice, and cuts the result to one page. Two accounts
        /// passing the same post on are two lines, so the key is the pair rather than the post alone.
        /// </summary>
        /// <param name="entries"> Lines gathered from one or more reads. </param>
        /// <param name="limit"> Largest number of lines to keep. </param>
        /// <returns> The page to show. </returns>
        static IReadOnlyList<FeedEntry> NewestFirst(IEnumerable<FeedEntry> entries, int limit)
            =>
            [
                .. entries
                    .DistinctBy(entry => (entry.Post.PostId, entry.ReposterAddress))
                    .OrderByDescending(entry => entry.SortedAtUnixMs)
                    .Take(limit)
            ];
    }
}
