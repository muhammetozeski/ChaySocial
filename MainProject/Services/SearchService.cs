using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary> The people and the posts one search term turned up, handed back together so a search screen fills both of its sections from a single call. </summary>
    /// <param name="People"> Matching profiles, best match first. </param>
    /// <param name="Posts"> Matching posts, newest first. </param>
    public sealed record SearchResults(IReadOnlyList<ProfileData> People, IReadOnlyList<PostData> Posts)
    {
        /// <summary> The answer for a term that matched nothing, and for a blank term. </summary>
        public static readonly SearchResults Empty = new([], []);

        /// <summary> True when neither section has anything to draw. </summary>
        public bool IsEmpty => People.Count == 0 && Posts.Count == 0;

        /// <summary> How many results there are across both sections. </summary>
        public int TotalCount => People.Count + Posts.Count;
    }

    /// <summary>
    /// Finding accounts and posts by typing part of a name, an address or a sentence. The document store matches whole
    /// field values and has no "contains" operator, so searching here means reading one bounded page out of a
    /// collection and comparing text on this device. That keeps every search to a single, predictable read, and it
    /// means results come from the newest slice of the app rather than from all of history.
    /// </summary>
    public static class SearchService
    {
        /// <summary> Results one search hands back per section when the caller does not ask for a different number. </summary>
        public const int DefaultResultLimit = 20;

        /// <summary>
        /// Profiles pulled into memory before the term is compared against them. This is a bounded scan: an account
        /// outside this slice is only found when the term is its exact address, which is looked up directly.
        /// </summary>
        const int ProfilesScannedPerSearch = 400;

        /// <summary>
        /// Posts pulled into memory, newest first, before the term is compared against them. This is a bounded scan:
        /// a post older than this slice is not searched.
        /// </summary>
        const int PostsScannedPerSearch = 400;

        /// <summary> Rank given to the account whose address was typed out in full; it sorts above everything else. </summary>
        const int ExactAddressRank = 0;

        /// <summary> Rank given to a profile whose display name begins with the term. </summary>
        const int NameStartsWithRank = 1;

        /// <summary> Rank given to a profile that matched somewhere in the middle of its name or address. </summary>
        const int ContainsAnywhereRank = 2;

        /// <summary>
        /// Finds accounts whose display name or address contains the term, ignoring letter case. Reads one bounded
        /// page of profiles — at most <see cref="ProfilesScannedPerSearch"/> — and compares them on this device;
        /// separately, a term that is an account's full address reads that account directly, so it is returned even
        /// when its display name has nothing to do with what was typed and even when it falls outside the scanned page.
        /// </summary>
        /// <param name="term"> What was typed. Blank or whitespace returns nothing without reading the store. </param>
        /// <param name="limit"> Largest number of accounts to return; zero or less returns nothing. </param>
        /// <returns> Matching profiles: the exact-address account first, then names that start with the term, then the rest. </returns>
        public static async Task<IReadOnlyList<ProfileData>> SearchPeopleAsync(string term, int limit = DefaultResultLimit)
        {
            string needle = Normalize(term);
            if (needle.Length == 0 || limit <= 0) return [];

            ProfileData? addressed = await AppServices.Documents.ReadAsync(new DocumentId<ProfileData>(needle));

            DocumentQuery<ProfileData> scan = new DocumentQuery<ProfileData>()
                .WithLimit(ProfilesScannedPerSearch);

            IReadOnlyList<ProfileData> scanned = (await AppServices.Documents.QueryAsync(scan)).Documents;

            List<ProfileData> found = [];
            if (addressed is not null) found.Add(addressed);

            foreach (ProfileData profile in scanned)
            {
                bool alreadyAdded = addressed is not null
                    && string.Equals(profile.Address, addressed.Address, StringComparison.Ordinal);

                if (alreadyAdded || !MatchesPerson(profile, needle)) continue;

                found.Add(profile);
            }

            return
            [
                .. found
                    .OrderBy(profile => RankPerson(profile, needle))
                    .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Take(limit)
            ];
        }

        /// <summary>
        /// Finds posts whose text contains the term, ignoring letter case. Reads one bounded page of the newest posts
        /// — at most <see cref="PostsScannedPerSearch"/> — and compares them on this device, so the search covers the
        /// recent wall rather than every post ever written.
        /// </summary>
        /// <param name="term"> What was typed. Blank or whitespace returns nothing without reading the store. </param>
        /// <param name="limit"> Largest number of posts to return; zero or less returns nothing. </param>
        /// <returns> Matching posts, newest first. </returns>
        public static async Task<IReadOnlyList<PostData>> SearchPostsAsync(string term, int limit = DefaultResultLimit)
        {
            string needle = Normalize(term);
            if (needle.Length == 0 || limit <= 0) return [];

            DocumentQuery<PostData> scan = new DocumentQuery<PostData>()
                .WithSort(PostData.CreatedAtField, descending: true)
                .WithLimit(PostsScannedPerSearch);

            IReadOnlyList<PostData> scanned = (await AppServices.Documents.QueryAsync(scan)).Documents;

            List<PostData> found = new(Math.Min(limit, scanned.Count));

            foreach (PostData post in scanned)
            {
                if (!post.Text.Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;

                found.Add(post);
                if (found.Count == limit) break;
            }

            return found;
        }

        /// <summary>
        /// Runs both searches for one term and hands back both sections together, so a search screen makes one call
        /// instead of two. Each section is a bounded scan of its own collection, described on the method it comes from.
        /// </summary>
        /// <param name="term"> What was typed. Blank or whitespace returns <see cref="SearchResults.Empty"/> without reading the store. </param>
        /// <param name="limit"> Largest number of results per section; zero or less returns <see cref="SearchResults.Empty"/>. </param>
        /// <returns> The matching people and posts. </returns>
        public static async Task<SearchResults> SearchAsync(string term, int limit = DefaultResultLimit)
        {
            string needle = Normalize(term);
            if (needle.Length == 0 || limit <= 0) return SearchResults.Empty;

            Task<IReadOnlyList<ProfileData>> people = SearchPeopleAsync(needle, limit);
            Task<IReadOnlyList<PostData>> posts = SearchPostsAsync(needle, limit);

            await Task.WhenAll(people, posts);
            return new SearchResults(await people, await posts);
        }

        /// <summary> Trims the term and turns a null or all-whitespace one into the empty string the callers test for. </summary>
        /// <param name="term"> What was typed, possibly null. </param>
        /// <returns> The trimmed term, or empty when there is nothing to search for. </returns>
        static string Normalize(string term) => string.IsNullOrWhiteSpace(term) ? string.Empty : term.Trim();

        /// <summary> Tests whether a profile's name or address carries the term anywhere inside it. </summary>
        /// <param name="profile"> Profile being tested. </param>
        /// <param name="term"> Trimmed, non-empty search term. </param>
        /// <returns> True when the profile should appear in the results. </returns>
        static bool MatchesPerson(ProfileData profile, string term)
            => profile.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
               || profile.Address.Contains(term, StringComparison.OrdinalIgnoreCase);

        /// <summary> Scores how well a profile answers the term, so the closest match is drawn at the top. </summary>
        /// <param name="profile"> Profile being scored. </param>
        /// <param name="term"> Trimmed, non-empty search term. </param>
        /// <returns> <see cref="ExactAddressRank"/>, <see cref="NameStartsWithRank"/> or <see cref="ContainsAnywhereRank"/>, lowest sorting first. </returns>
        static int RankPerson(ProfileData profile, string term) => true switch
        {
            _ when string.Equals(profile.Address, term, StringComparison.OrdinalIgnoreCase) => ExactAddressRank,
            _ when profile.DisplayName.StartsWith(term, StringComparison.OrdinalIgnoreCase) => NameStartsWithRank,
            _ => ContainsAnywhereRank
        };
    }
}
