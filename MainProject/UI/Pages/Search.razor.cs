using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Services;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// Looking for accounts and posts by typing part of a name, an address or a sentence. The field searches on every
    /// keystroke, so an early keystroke's answer can come back after a later one's. The page therefore remembers the
    /// term the newest search was started for and drops any answer whose term is no longer what is in the box, which
    /// is what stops a slow "ab" from painting itself over the results for "abcd".
    /// </summary>
    public partial class Search
    {
        /// <summary> Everything a post card draws besides the post itself, read once per search and kept together. </summary>
        /// <param name="AuthorProfile"> Profile of the account that wrote the post, or null when it could not be read. </param>
        /// <param name="LikeCount"> How many accounts have liked the post. </param>
        /// <param name="IsLikedByViewer"> True when the signed-in account is one of those accounts. </param>
        /// <param name="CommentCount"> How many replies the post carries. </param>
        sealed record PostDetail(ProfileData? AuthorProfile, int LikeCount, bool IsLikedByViewer, int CommentCount);

        /// <summary>
        /// One finished search, whole. Every section is assembled off-screen and only then swapped in, so the page
        /// never shows one term's people beside another term's posts.
        /// </summary>
        /// <param name="People"> Accounts that matched the term. </param>
        /// <param name="FollowedAddresses"> Accounts the reader follows, which is what fills in each follow button. </param>
        /// <param name="Posts"> Posts that matched the term. </param>
        /// <param name="PostDetails"> The counts for those posts, keyed by post id. </param>
        sealed record SearchSnapshot(
            IReadOnlyList<ProfileData> People,
            IReadOnlyList<string> FollowedAddresses,
            IReadOnlyList<PostData> Posts,
            Dictionary<string, PostDetail> PostDetails);

        /// <summary> Letters that have to be typed before anything is read. One letter matches most of the app and answers nothing. </summary>
        const int MinimumTermLength = 2;

        /// <summary>
        /// Longest term the field accepts. A whole post is the largest thing anyone could be looking for, so the
        /// field takes exactly that much text and no more.
        /// </summary>
        const int MaximumTermLength = PostData.MaximumTextLength;

        /// <summary>
        /// Results asked for per section. Kept under <see cref="SearchService.DefaultResultLimit"/> because this page
        /// searches again on every keystroke and each post it shows costs further reads for its author, its likes and
        /// its replies; a shorter list is what keeps typing responsive.
        /// </summary>
        const int ResultsPerSection = 8;

        /// <summary> Shown in place of the results when a search throws. </summary>
        const string SearchFailureText = "That search didn't come back. Type a letter to try it again.";

        /// <summary> Title of the page, used both in the header and as the document title. </summary>
        const string PageHeading = "Search";

        /// <summary> Emoji beside the page title; the same one the bottom navigation marks this page with. </summary>
        const string HeadingEmoji = NavigationConstants.Search.Icon;

        /// <summary> Emoji sitting inside the field, at the end the caret starts from. </summary>
        const string FieldEmoji = "🔎";

        /// <summary> Placeholder in the field, which also names it for a screen reader. </summary>
        const string FieldPlaceholder = "Names, addresses, posts…";

        /// <summary> Glyph on the button that empties the field. </summary>
        const string ClearEmoji = "✕";

        /// <summary> Tooltip and spoken name of the button that empties the field. </summary>
        const string ClearHint = "Clear the search";

        /// <summary> Marks the chip that replaces the follow button on the reader's own account. </summary>
        const string ViewerChipEmoji = "🫶";

        /// <summary> Text of that chip. </summary>
        const string ViewerChipLabel = "that's you";

        /// <summary> Marks the accounts section. </summary>
        const string PeopleEmoji = "🧑‍🤝‍🧑";

        /// <summary> Heading of the accounts section. </summary>
        const string PeopleSectionName = "People";

        /// <summary> Marks the posts section. </summary>
        const string PostsEmoji = "📝";

        /// <summary> Heading of the posts section. </summary>
        const string PostsSectionName = "Posts";

        /// <summary> Emoji on the placeholder shown before enough has been typed to search. </summary>
        const string PromptEmoji = "🔮";

        /// <summary> Headline of that placeholder. </summary>
        const string PromptHeadline = "Find your people";

        /// <summary> Emoji on the placeholder shown when no account matched. </summary>
        const string NoPeopleEmoji = "🕵️";

        /// <summary> Headline of that placeholder. </summary>
        const string NoPeopleHeadline = "Nobody by that name";

        /// <summary> Supporting line of that placeholder. </summary>
        const string NoPeopleDescription = "Try a shorter word, or paste an account's full address to land on it exactly.";

        /// <summary> Emoji on the placeholder shown when no post matched. </summary>
        const string NoPostsEmoji = "🍃";

        /// <summary> Headline of that placeholder. </summary>
        const string NoPostsHeadline = "No posts matched";

        /// <summary> Supporting line of that placeholder, which says why an old post may be missing. </summary>
        const string NoPostsDescription = "Searching reads the most recent posts, so something written long ago may not turn up here.";

        /// <summary> Emoji on the placeholder shown when a search throws. </summary>
        const string FailureEmoji = "🌧️";

        /// <summary> Headline of that placeholder. </summary>
        const string FailureHeadline = "That didn't go through";

        /// <summary> Diameter of the throbber that sits in the field while a search is in flight, sized to the text beside it. </summary>
        const int FieldSpinnerDiameterPx = AppMeasures.Size.Px20;

        /// <summary> Ring thickness of that throbber, thinned from the app default because the throbber itself is small. </summary>
        const int FieldSpinnerBorderWidthPx = AppMeasures.Border.Medium;

        /// <summary> Stands in for a post whose details have not been read, so a card draws zeroes rather than throwing. </summary>
        static readonly PostDetail MissingDetail = new(null, 0, false, 0);

        /// <summary> What is currently in the field. Rewritten on every keystroke by the binding on the input. </summary>
        string Term = string.Empty;

        /// <summary>
        /// The trimmed term the newest search was started for. A finished search compares its own term against this
        /// one and, when they differ, leaves the page alone — that comparison is the whole race guard.
        /// </summary>
        string ActiveTerm = string.Empty;

        /// <summary> True while a search is reading, which swaps the clear button for a throbber. </summary>
        bool IsSearching;

        /// <summary> Message shown instead of the results after a search threw; null while everything is well. </summary>
        string? SearchFailureMessage;

        /// <summary> Accounts the current term matched. </summary>
        IReadOnlyList<ProfileData> People = [];

        /// <summary> Posts the current term matched. </summary>
        IReadOnlyList<PostData> Posts = [];

        /// <summary> Counts for the matched posts, keyed by post id. </summary>
        Dictionary<string, PostDetail> PostDetails = [];

        /// <summary> Accounts the reader follows, so each row's button opens in the right state. </summary>
        HashSet<string> FollowedAddresses = [];

        /// <summary> Accounts whose follow button is mid-write. Held per address so two rows can be pressed independently. </summary>
        readonly HashSet<string> BusyFollowAddresses = [];

        /// <summary> True once enough has been typed for a search to run. </summary>
        bool IsTermSearchable => Term.Trim().Length >= MinimumTermLength;

        /// <summary> Supporting line of the opening placeholder, which names how much has to be typed. </summary>
        string PromptDescription
            => $"Type at least {MinimumTermLength} letters and matches appear as you go — display names, account addresses and the text of posts.";

        /// <summary>
        /// Completes immediately: there is nothing to show before a term is typed, so opening this page reads nothing.
        /// </summary>
        /// <returns> An already-completed task. </returns>
        protected override Task LoadAsync() => Task.CompletedTask;

        /// <summary>
        /// Runs the search for whatever is in the field. Records the term first, so any search still in flight for an
        /// older term recognises itself as stale on return and leaves the results alone; a term that is too short
        /// empties both sections without reading anything.
        /// </summary>
        /// <returns> A task that completes once the results have been replaced, or discarded as stale. </returns>
        async Task RunSearchAsync()
        {
            string requestedTerm = Term.Trim();
            ActiveTerm = requestedTerm;
            SearchFailureMessage = null;

            if (requestedTerm.Length < MinimumTermLength)
            {
                Clear();
                IsSearching = false;
                return;
            }

            IsSearching = true;

            try
            {
                SearchResults results = await SearchService.SearchAsync(requestedTerm, ResultsPerSection);
                if (!IsStillCurrent(requestedTerm)) return;

                SearchSnapshot snapshot = await BuildSnapshotAsync(results);
                if (!IsStillCurrent(requestedTerm)) return;

                Apply(snapshot);
            }
            catch (Exception error)
            {
                Log($"{nameof(Search)} failed to search for '{requestedTerm}'.\n{error}", LogLevel.Error);

                if (!IsStillCurrent(requestedTerm)) return;

                Clear();
                SearchFailureMessage = SearchFailureText;
            }
            finally
            {
                if (IsStillCurrent(requestedTerm))
                {
                    IsSearching = false;
                    StateHasChanged();
                }
            }
        }

        /// <summary> Empties the field and puts the page back to its opening placeholder. </summary>
        /// <returns> A task that completes once the results have been emptied. </returns>
        async Task ClearTermAsync()
        {
            Term = string.Empty;
            await RunSearchAsync();
        }

        /// <summary>
        /// Tells whether a search that has just finished still belongs on screen, which is true only while its term is
        /// the one the field last asked for.
        /// </summary>
        /// <param name="requestedTerm"> The term that search was started for. </param>
        /// <returns> True when nothing newer has been typed since. </returns>
        bool IsStillCurrent(string requestedTerm) => string.Equals(ActiveTerm, requestedTerm, StringComparison.Ordinal);

        /// <summary> Drops every result, leaving the sections empty. </summary>
        void Clear()
        {
            People = [];
            Posts = [];
            PostDetails = [];
            FollowedAddresses = [];
        }

        /// <summary> Moves a finished snapshot onto the page in one step. </summary>
        /// <param name="snapshot"> The search that is being shown. </param>
        void Apply(SearchSnapshot snapshot)
        {
            People = snapshot.People;
            Posts = snapshot.Posts;
            PostDetails = snapshot.PostDetails;
            FollowedAddresses = [.. snapshot.FollowedAddresses];
        }

        /// <summary>
        /// Reads everything the two sections need around the raw matches: who the reader already follows, and the
        /// author, likes and replies behind each matched post. Both halves are read at the same time.
        /// </summary>
        /// <param name="results"> The people and posts the term matched. </param>
        /// <returns> The complete snapshot to show. </returns>
        async Task<SearchSnapshot> BuildSnapshotAsync(SearchResults results)
        {
            string viewerAddress = SessionService.CurrentAddress;

            Task<IReadOnlyList<string>> following = FollowService.ReadFollowingAsync(viewerAddress, FollowService.MaximumCountedFollows);
            Task<Dictionary<string, PostDetail>> details = ReadPostDetailsAsync(results.Posts, viewerAddress);

            await Task.WhenAll(following, details);

            return new SearchSnapshot(results.People, await following, results.Posts, await details);
        }

        /// <summary>
        /// Reads the author profile, the likers and the reply count behind each matched post. Every read is opened at
        /// once because the list is capped at <see cref="ResultsPerSection"/>, and author profiles are read once per
        /// distinct author rather than once per post.
        /// </summary>
        /// <param name="posts"> Posts the term matched; an empty list reads nothing. </param>
        /// <param name="viewerAddress"> Address of the reader, whose own like turns a card's heart red. </param>
        /// <returns> The counts for those posts, keyed by post id. </returns>
        static async Task<Dictionary<string, PostDetail>> ReadPostDetailsAsync(IReadOnlyList<PostData> posts, string viewerAddress)
        {
            if (posts.Count == 0) return [];

            string[] authorAddresses = [.. posts.Select(post => post.AuthorAddress).Distinct()];

            // Read once for the whole page of results rather than once per card.
            HashSet<string> shutOut = await ModerationService.ReadShutOutAddressesAsync(viewerAddress);

            Task<ProfileData?[]> profiles = Task.WhenAll(authorAddresses.Select(ProfileService.ReadAsync));
            Task<IReadOnlyList<string>[]> likers = Task.WhenAll(posts.Select(post => WallService.ReadLikersAsync(post.PostId)));
            Task<int[]> replyCounts = Task.WhenAll(posts.Select(post => CommentService.CountForPostAsync(post, shutOut)));

            await Task.WhenAll(profiles, likers, replyCounts);

            ProfileData?[] readProfiles = await profiles;
            Dictionary<string, ProfileData> profileByAuthor = new(authorAddresses.Length);

            for (int index = 0; index < authorAddresses.Length; index++)
            {
                if (readProfiles[index] is ProfileData profile) profileByAuthor[authorAddresses[index]] = profile;
            }

            IReadOnlyList<string>[] readLikers = await likers;
            int[] readReplyCounts = await replyCounts;
            Dictionary<string, PostDetail> details = new(posts.Count);

            for (int index = 0; index < posts.Count; index++)
            {
                PostData post = posts[index];

                details[post.PostId] = new PostDetail(
                    profileByAuthor.GetValueOrDefault(post.AuthorAddress),
                    readLikers[index].Count,
                    readLikers[index].Contains(viewerAddress),
                    readReplyCounts[index]);
            }

            return details;
        }

        /// <summary> The counts for one post, or an empty set while its details have not been read. </summary>
        /// <param name="postId"> Id of the post being drawn. </param>
        /// <returns> That post's details. </returns>
        PostDetail DetailFor(string postId) => PostDetails.GetValueOrDefault(postId, MissingDetail);

        /// <summary> True when an address belongs to the reader, which is what replaces its follow button with a chip. </summary>
        /// <param name="address"> Address of the account in the row. </param>
        /// <returns> True when the row is the reader's own account. </returns>
        static bool IsViewersOwnAccount(string address)
            => string.Equals(address, SessionService.CurrentAddress, StringComparison.Ordinal);

        /// <summary> Marks the way to the square. </summary>
        const string SquareEmoji = "🪧";

        /// <summary> Text on that control. </summary>
        const string SquareLabel = "See what is being talked about";

        /// <summary> Opens the square, where every subject anybody has named is listed. </summary>
        void OpenSubjectBoard() => NavManager.NavigateTo(Subjects.SubjectBoardRoute);

        /// <summary> Opens one account's profile. </summary>
        /// <param name="address"> Address of the account to open. </param>
        void OpenProfile(string address)
            => NavManager.NavigateTo($"{NavigationConstants.Profile.Link}/{Uri.EscapeDataString(address)}");

        /// <summary>
        /// Follows or unfollows one account and moves its button to the state the write left behind. The address is
        /// held busy while the write runs, so a second press on that row does nothing while the first is still going,
        /// and other rows stay pressable.
        /// </summary>
        /// <param name="address"> Address of the account being followed or dropped. </param>
        /// <returns> A task that completes once the button reflects the stored follow. </returns>
        async Task ToggleFollowAsync(string address)
        {
            if (!SessionService.IsSignedIn || !BusyFollowAddresses.Add(address)) return;

            StateHasChanged();

            try
            {
                if (FollowedAddresses.Contains(address))
                {
                    await FollowService.UnfollowAsync(Account, address);
                    FollowedAddresses.Remove(address);
                }
                else if (await FollowService.FollowAsync(Account, address))
                {
                    FollowedAddresses.Add(address);
                }
            }
            catch (Exception error)
            {
                Log($"{nameof(Search)} could not change the follow on '{address}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                BusyFollowAddresses.Remove(address);
                StateHasChanged();
            }
        }

        /// <summary>
        /// Adds or removes the reader's like on a matched post and moves that card's heart and count to match, without
        /// running the search again.
        /// </summary>
        /// <param name="post"> The post whose heart was pressed. </param>
        /// <returns> A task that completes once the card shows the stored like. </returns>
        async Task ToggleLikeAsync(PostData post)
        {
            if (!SessionService.IsSignedIn) return;

            try
            {
                bool isLiked = await WallService.ToggleLikeAsync(post, Account.Public);
                PostDetail detail = DetailFor(post.PostId);
                int likeCount = Math.Max(0, detail.LikeCount + (isLiked ? 1 : -1));

                PostDetails[post.PostId] = detail with { LikeCount = likeCount, IsLikedByViewer = isLiked };
            }
            catch (Exception error)
            {
                Log($"{nameof(Search)} could not change the like on post '{post.PostId}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                StateHasChanged();
            }
        }
    }
}
