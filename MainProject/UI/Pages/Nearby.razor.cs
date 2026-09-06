using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// People two steps out. The only suggestion this app makes, and the only kind it can make with nothing
    /// hidden: an account is here because accounts the reader chose already follow it, the row names which ones,
    /// and the order is that count.
    /// </summary>
    public partial class Nearby
    {
        /// <summary> Address this page answers on. Announced here so every link to it comes from one place. </summary>
        public const string NearbyRoute = "/nearby";

        /// <summary> Suggestions shown at once. </summary>
        const int SuggestionsShown = 25;

        /// <summary> Connecting names spelled out before the rest are counted. </summary>
        const int NamesSpelledOut = 2;

        /// <summary> The page's heading. </summary>
        const string PageHeadline = "Two steps out";

        /// <summary> Line under it, saying what the whole ranking is. </summary>
        const string PageSubtitle = "Accounts your own people already follow, ordered by how many of them do. Nothing else.";

        /// <summary> Browser tab title. </summary>
        const string PageTitleText = "Two steps out";

        /// <summary> Emoji for the placeholder shown when there is nothing to suggest. </summary>
        const string EmptyEmoji = "🧭";

        /// <summary> Headline of that placeholder. </summary>
        const string EmptyHeadline = "Nothing two steps out yet";

        /// <summary> Supporting line of it: a second circle needs a first one. </summary>
        const string EmptyDescription = "This is built from the accounts you follow, so it fills up as soon as you follow a few people.";

        /// <summary> Label on the button that leads to where people are found. </summary>
        const string GoToDiscoverLabel = "Wander over to Discover";

        /// <summary> Emoji for the placeholder shown when the page could not be read. </summary>
        const string LoadFailedEmoji = "🌧️";

        /// <summary> Headline of that placeholder; the supporting line is the failure message the page base supplies. </summary>
        const string LoadFailedHeadline = "This didn't come through";

        /// <summary> Label on the button that runs the failed load again. </summary>
        const string TryAgainLabel = "Try again";

        /// <summary> Wording of a row's reason when one account leads to it. </summary>
        const string SingleReasonFormat = "{0} follows them.";

        /// <summary> Wording when exactly the spelled-out names lead to it. </summary>
        const string NamedReasonFormat = "{0} follow them.";

        /// <summary> Wording when more lead to it than are spelled out; the placeholders take the names and the rest. </summary>
        const string NamedAndMoreReasonFormat = "{0} and {1} more of your people follow them.";

        /// <summary> What joins two names. </summary>
        const string NameJoiner = " and ";

        /// <summary> What separates names before the last one. </summary>
        const string NameSeparator = ", ";

        /// <summary> Diameter of the small spinner in the header while a reload runs. </summary>
        const int RefreshSpinnerDiameterPx = AppMeasures.Size.Px20;

        /// <summary> Ring thickness of that spinner. </summary>
        const int RefreshSpinnerBorderPx = AppMeasures.Border.Medium;

        /// <summary> Corner radius of this page's pill buttons, as a CSS length. </summary>
        static readonly string ActionButtonRadiusCss = AppMeasures.Radius.Pill + "px";

        /// <summary> Inside spacing of those buttons. </summary>
        static readonly string ActionButtonPaddingCss = $"{AppMeasures.Space.Px10}px {AppMeasures.Space.Px20}px";

        /// <summary> The suggestions, most-connected first. </summary>
        IReadOnlyList<NeighbourCandidate> candidates = [];

        /// <summary> Profiles of everyone named on the page — the suggestions and the accounts that lead to them. </summary>
        IReadOnlyDictionary<string, ProfileData> profiles = new Dictionary<string, ProfileData>();

        /// <summary> Accounts this reader already follows, so a row pressed once does not offer itself again. </summary>
        HashSet<string> following = [];

        /// <summary> Accounts whose follow is being written right now. </summary>
        readonly HashSet<string> busyFollowAddresses = [];

        /// <summary> True once a load has finished, so later reloads refresh in place instead of blanking the page. </summary>
        bool hasLoadedOnce;

        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.FollowChanged,
            MainEvents.Names.ModerationChanged,
            MainEvents.Names.SessionChanged
        ];

        /// <summary> True while the very first load runs and there is nothing on screen to keep. </summary>
        bool IsFirstLoad => IsLoading && !hasLoadedOnce;

        /// <summary> True while a reload refreshes a page that is already drawn. </summary>
        bool IsRefreshing => IsLoading && hasLoadedOnce;

        /// <summary> Frosted bar the header is painted on. </summary>
        static string HeaderSurfaceStyle => AppStyles.BuildBarSurface(pinnedToBottom: false);

        /// <summary> Reads the second circle and every profile the page will name. </summary>
        /// <returns> A task that completes once the page has all of it. </returns>
        protected override async Task LoadAsync()
        {
            string viewer = SessionService.CurrentAddress;

            IReadOnlyList<NeighbourCandidate> found = await NeighbourService.ReadSecondCircleAsync(viewer, SuggestionsShown);

            // Both the suggestions and the accounts that lead to them are named on screen, so both are read — once
            // each, however many rows a given account appears in.
            string[] named =
            [
                .. found.Select(candidate => candidate.Address),
                .. found.SelectMany(candidate => candidate.ConnectingAddresses)
            ];

            candidates = found;
            profiles = await ReadProfilesAsync(named);
            following = [.. await FollowService.ReadFollowingAsync(viewer, NeighbourService.FirstCircleAccounts)];
            hasLoadedOnce = true;
        }

        /// <summary> Reads one profile per distinct address named on the page. </summary>
        /// <param name="addresses"> Everyone the page will name. </param>
        /// <returns> The profiles that exist, keyed by address. </returns>
        static async Task<IReadOnlyDictionary<string, ProfileData>> ReadProfilesAsync(IReadOnlyList<string> addresses)
        {
            string[] distinct = [.. addresses.Distinct()];
            ProfileData?[] read = await Task.WhenAll(distinct.Select(ProfileService.ReadAsync));

            Dictionary<string, ProfileData> byAddress = new(distinct.Length);
            for (int index = 0; index < distinct.Length; index++)
            {
                if (read[index] is ProfileData profile) byAddress[distinct[index]] = profile;
            }

            return byAddress;
        }

        /// <summary>
        /// The profile of one account, or null when it could not be read.
        /// </summary>
        /// <param name="address"> The account. </param>
        /// <returns> Its profile, or null — in which case the row is left out rather than drawn empty. </returns>
        ProfileData? ProfileFor(string address) => profiles.GetValueOrDefault(address);

        /// <summary> The one line under a suggestion: who, by name, led to it. </summary>
        /// <param name="candidate"> The suggestion being drawn. </param>
        /// <returns> The reason, in words. </returns>
        string ReasonFor(NeighbourCandidate candidate)
        {
            string[] names = [.. candidate.ConnectingAddresses.Select(NameOf)];

            if (names.Length == 1) return string.Format(SingleReasonFormat, names[0]);

            if (names.Length <= NamesSpelledOut)
            {
                return string.Format(NamedReasonFormat, string.Join(NameJoiner, names));
            }

            string spelled = string.Join(NameSeparator, names.Take(NamesSpelledOut));
            return string.Format(NamedAndMoreReasonFormat, spelled, names.Length - NamesSpelledOut);
        }

        /// <summary> The name drawn for one account, falling back to the readable head of its address. </summary>
        /// <param name="address"> The account. </param>
        /// <returns> Its name. </returns>
        string NameOf(string address)
            => profiles.TryGetValue(address, out ProfileData? profile) && !string.IsNullOrWhiteSpace(profile.DisplayName)
                ? profile.DisplayName
                : ProfileService.FallbackDisplayName(address);

        /// <summary> True when the reader already follows a suggestion. </summary>
        /// <param name="address"> The account. </param>
        /// <returns> Whether it is followed. </returns>
        bool IsFollowing(string address) => following.Contains(address);

        /// <summary> True while that account's follow is being written. </summary>
        /// <param name="address"> The account. </param>
        /// <returns> Whether a write is in flight for it. </returns>
        bool IsBusy(string address) => busyFollowAddresses.Contains(address);

        /// <summary>
        /// Starts or stops following one suggestion. The row is written first and the pill flipped after, so the
        /// pill never claims something the store has not accepted.
        /// </summary>
        /// <param name="address"> The account. </param>
        /// <returns> A task that completes once the follow has been written or withdrawn. </returns>
        async Task ToggleFollowAsync(string address)
        {
            if (!SessionService.IsSignedIn || !busyFollowAddresses.Add(address)) return;

            try
            {
                if (following.Contains(address))
                {
                    await FollowService.UnfollowAsync(Account, address);
                    following.Remove(address);
                }
                else
                {
                    await FollowService.FollowAsync(Account, address);
                    following.Add(address);
                }
            }
            finally
            {
                busyFollowAddresses.Remove(address);
            }
        }

        /// <summary> Opens one account's profile. </summary>
        /// <param name="address"> The account. </param>
        void OpenProfile(string address) => NavManager.NavigateTo($"{NavigationConstants.Profile.Link}/{address}");

        /// <summary> Leaves for the wall, where the discover feed lives. </summary>
        void OpenDiscover() => NavManager.NavigateTo(NavigationConstants.Wall.Link);

        /// <summary> Goes back to the wall. </summary>
        void GoBack() => NavManager.NavigateTo(NavigationConstants.Wall.Link);
    }
}
