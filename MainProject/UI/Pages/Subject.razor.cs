using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;
using ChaySocial.MainProject.Text;
using Microsoft.AspNetCore.Components;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// Everything written under one subject. The page reads an index rather than searching text, but trusts none of
    /// it: each post it gets back is kept only when the post's own words name the subject being read.
    /// </summary>
    public partial class Subject
    {
        /// <summary> Start of every subject address. </summary>
        public const string SubjectRoutePrefix = "/subject";

        /// <summary>
        /// Route this page answers on. The parameter segment is spelled from <see cref="Name"/> itself, so renaming
        /// the property moves the route with it instead of silently breaking every link.
        /// </summary>
        public const string SubjectRoute = SubjectRoutePrefix + "/{" + nameof(Name) + "}";

        /// <summary> The subject taken from the route, without its mark. </summary>
        [Parameter] public string? Name { get; set; }

        /// <summary> Header line for a subject nothing has been written under. </summary>
        const string NothingYetLabel = "nothing under this yet";

        /// <summary> Header line for a subject with exactly one post, where the plural would read wrong. </summary>
        const string SinglePostLabel = "1 post";

        /// <summary> Header line for any other count; the placeholder takes the count. </summary>
        const string ManyPostsLabelFormat = "{0} posts";

        /// <summary> Emoji for the placeholder shown when nothing has been written under this subject. </summary>
        const string EmptyEmoji = "🍃";

        /// <summary> Headline of that placeholder. </summary>
        const string EmptyHeadline = "Nobody has said anything here";

        /// <summary> Supporting line of that placeholder: the invitation to be the one who does. </summary>
        const string EmptyDescription = "Write a post naming this and it will be the first one standing here.";

        /// <summary> Emoji for the placeholder shown when the listing could not be read at all. </summary>
        const string LoadFailedEmoji = "🌧️";

        /// <summary> Headline of that placeholder; the supporting line is the failure message the page base supplies. </summary>
        const string LoadFailedHeadline = "This didn't come through";

        /// <summary> Label on the button that runs the failed load again. </summary>
        const string TryAgainLabel = "Try again";

        /// <summary> Label on the button that leaves an empty subject. </summary>
        const string BackToWallLabel = "Back to the wall";

        /// <summary> Route a post's own page lives at; the post id is appended to it. </summary>
        const string PostRoutePrefix = "/post/";

        /// <summary> Route an account's profile lives at; the address is appended to it. </summary>
        const string ProfileRoutePrefix = "/profile/";

        /// <summary> Diameter of the small spinner in the header while a reload runs. </summary>
        const int RefreshSpinnerDiameterPx = AppMeasures.Size.Px20;

        /// <summary> Ring thickness of that spinner. </summary>
        const int RefreshSpinnerBorderPx = AppMeasures.Border.Medium;

        /// <summary> Corner radius of this page's pill buttons, as a CSS length. </summary>
        static readonly string ActionButtonRadiusCss = AppMeasures.Radius.Pill + "px";

        /// <summary> Inside spacing of those buttons. </summary>
        static readonly string ActionButtonPaddingCss = $"{AppMeasures.Space.Px10}px {AppMeasures.Space.Px20}px";

        /// <summary> Posts written under this subject, newest first. </summary>
        IReadOnlyList<PostData> posts = [];

        /// <summary> Profiles of the accounts that wrote them, keyed by address. </summary>
        Dictionary<string, ProfileData?> authorProfiles = [];

        /// <summary> The counts drawn under each post, keyed by post id. </summary>
        Dictionary<string, PostEngagement> engagements = [];

        /// <summary> True once a load has finished, so later reloads refresh the listing in place instead of blanking it. </summary>
        bool hasLoadedOnce;

        /// <summary> Subject the last load ran for; a different one in the route means the reader followed another name. </summary>
        string loadedSubject = string.Empty;

        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.WallChanged,
            MainEvents.Names.SessionChanged
        ];

        /// <summary> True while the very first load runs and there is nothing on screen to keep. </summary>
        bool IsFirstLoad => IsLoading && !hasLoadedOnce;

        /// <summary> True while a reload refreshes a listing that is already drawn; only the header spinner reacts to it. </summary>
        bool IsRefreshing => IsLoading && hasLoadedOnce;

        /// <summary> The subject being read, in the form it is stored under. </summary>
        string WantedSubject => WrittenText.NormaliseSubject((Name ?? string.Empty).Trim());

        /// <summary> The subject as the page shows it, with the mark it is written with. </summary>
        string SubjectLabel => WrittenText.SubjectMark + WantedSubject;

        /// <summary> Browser tab title. </summary>
        string PageTitleText => SubjectLabel;

        /// <summary> Header line under the subject: how much is written under it. </summary>
        string CountLabel => posts.Count switch
        {
            0 => NothingYetLabel,
            1 => SinglePostLabel,
            _ => string.Format(ManyPostsLabelFormat, posts.Count)
        };

        /// <summary> Frosted bar the sticky header is painted on, so the listing scrolls under glass instead of under nothing. </summary>
        static string HeaderSurfaceStyle => AppStyles.BuildBarSurface(pinnedToBottom: false);

        /// <summary>
        /// Reloads when the route swaps one subject for another — following a name inside a post already on this
        /// page, for instance. The first pass is already covered by the page base, which is why a name matching the
        /// one just loaded is left alone.
        /// </summary>
        /// <returns> A task that completes once the new subject has been read, or immediately when nothing changed. </returns>
        protected override async Task OnParametersSetAsync()
        {
            await base.OnParametersSetAsync();

            if (string.Equals(loadedSubject, WantedSubject, StringComparison.Ordinal)) return;

            await ReloadAsync();
        }

        /// <summary> Reads the posts under this subject and everything their cards need to draw. </summary>
        /// <returns> A task that completes once the page has all of it. </returns>
        protected override async Task LoadAsync()
        {
            loadedSubject = WantedSubject;

            IReadOnlyList<PostData> found = await WallService.ReadSubjectAsync(WantedSubject);

            string[] authors = [.. found.Select(post => post.AuthorAddress).Distinct()];
            Task<ProfileData?[]> profilesRead = Task.WhenAll(authors.Select(ProfileService.ReadAsync));
            Task<Dictionary<string, PostEngagement>> engagementsRead =
                FeedService.ReadEngagementsAsync(found, SessionService.CurrentAddress);

            await Task.WhenAll(profilesRead, engagementsRead);

            Dictionary<string, ProfileData?> byAddress = new(authors.Length);
            ProfileData?[] profiles = await profilesRead;
            for (int index = 0; index < authors.Length; index++)
            {
                byAddress[authors[index]] = profiles[index];
            }

            posts = found;
            authorProfiles = byAddress;
            engagements = await engagementsRead;
            hasLoadedOnce = true;
        }

        /// <summary> The profile behind a post's author. </summary>
        /// <param name="address"> Address to look up. </param>
        /// <returns> The stored profile, or null when that account has never published one. </returns>
        ProfileData? ProfileFor(string address) => authorProfiles.GetValueOrDefault(address);

        /// <summary> The counts for one post, all zero while they have not been read. </summary>
        /// <param name="post"> Post being drawn. </param>
        /// <returns> That post's totals. </returns>
        PostEngagement EngagementFor(PostData post) => engagements.GetValueOrDefault(post.PostId);

        /// <summary> Turns the reader's like on one of these posts on, or off when it was already on. </summary>
        /// <param name="post"> Post whose heart was tapped. </param>
        /// <returns> A task that completes once the like has been written. </returns>
        Task ToggleLikeAsync(PostData post) => WallService.ToggleLikeAsync(post, Account.Public);

        /// <summary> Carries a post onto the reader's own wall, or takes it back when it is already there. </summary>
        /// <param name="post"> Post whose arrows were tapped. </param>
        /// <returns> A task that completes once the repost has been written. </returns>
        Task ToggleRepostAsync(PostData post) => WallService.ToggleRepostAsync(post, Account);

        /// <summary> Opens an author's profile. </summary>
        /// <param name="address"> Address of the account whose profile to open. </param>
        void OpenAuthor(string address) => NavManager.NavigateTo($"{ProfileRoutePrefix}{address}");

        /// <summary> Opens one post's own page, where its comments are read and written. </summary>
        /// <param name="postId"> Id of the post to open. </param>
        void OpenComments(string postId) => NavManager.NavigateTo($"{PostRoutePrefix}{postId}");

        /// <summary> Leaves this subject for the wall. </summary>
        void GoBackToWall() => NavManager.NavigateTo(NavigationConstants.Wall.Link);
    }
}
