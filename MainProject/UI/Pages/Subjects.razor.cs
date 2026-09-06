using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// The public square. Which subjects exist, how each one stands, and one press to follow any of them.
    /// </summary>
    /// <remarks>
    /// The ordering is offered rather than decided, and "least named first" sits beside "most named" on purpose:
    /// a subject two people write under quietly is exactly the one no advertising-funded feed would ever put in
    /// front of a reader, and here it is one press away.
    /// </remarks>
    public partial class Subjects
    {
        /// <summary> Address this page answers on. Announced here so every link to the square comes from one place. </summary>
        public const string SubjectBoardRoute = "/subjects";

        /// <summary> Subjects listed at once. </summary>
        const int SubjectsShown = 40;

        /// <summary> The page's heading. </summary>
        const string PageHeadline = "The square";

        /// <summary> Browser tab title. </summary>
        const string PageTitleText = "Subjects";

        /// <summary>
        /// Line under the heading. It names the window every count on the page was made over, because a number
        /// whose window is hidden is a number a reader cannot check.
        /// </summary>
        static readonly string WindowLine =
            $"Counted over the newest {SubjectBoardService.SubjectMentionsScanned} times anybody named a subject. "
            + "Nothing here is weighted or boosted.";

        /// <summary> Emoji for the placeholder shown when nobody has named a subject yet. </summary>
        const string EmptyEmoji = "🪧";

        /// <summary> Headline of that placeholder. </summary>
        const string EmptyHeadline = "The square is empty";

        /// <summary> Supporting line of that placeholder. </summary>
        const string EmptyDescription = "Write a post with a #subject in it and this is where it will stand.";

        /// <summary> Emoji for the placeholder shown when the square could not be read. </summary>
        const string LoadFailedEmoji = "🌧️";

        /// <summary> Headline of that placeholder; the supporting line is the failure message the page base supplies. </summary>
        const string LoadFailedHeadline = "This didn't come through";

        /// <summary> Label on the button that runs the failed load again. </summary>
        const string TryAgainLabel = "Try again";

        /// <summary> Class appended to whichever ordering is chosen. </summary>
        const string ActiveOrderClass = "is-active";

        /// <summary> Wording of a row's standing; the placeholders take the count and how long ago it was last named. </summary>
        const string StandingFormat = "named {0}, most recently {1}";

        /// <summary> Word after a count of one, where the plural would read wrong. </summary>
        const string OnceWord = "once";

        /// <summary> Wording after any other count. </summary>
        const string ManyTimesFormat = "{0} times";

        /// <summary> Added to a row that has been going long enough inside the window to be worth saying so. </summary>
        const string RunningForFormat = ", going for {0}";

        /// <summary> Diameter of the small spinner in the header while a reload runs. </summary>
        const int RefreshSpinnerDiameterPx = AppMeasures.Size.Px20;

        /// <summary> Ring thickness of that spinner. </summary>
        const int RefreshSpinnerBorderPx = AppMeasures.Border.Medium;

        /// <summary> Corner radius of this page's pill buttons, as a CSS length. </summary>
        static readonly string ActionButtonRadiusCss = AppMeasures.Radius.Pill + "px";

        /// <summary> Inside spacing of those buttons. </summary>
        static readonly string ActionButtonPaddingCss = $"{AppMeasures.Space.Px10}px {AppMeasures.Space.Px20}px";

        /// <summary> The orderings offered, in the order they are drawn. </summary>
        static readonly SubjectBoardOrder[] SelectableOrders = Enum.GetValues<SubjectBoardOrder>();

        /// <summary> Where each subject stands, in the chosen order. </summary>
        IReadOnlyList<SubjectStanding> standings = [];

        /// <summary> Subjects this reader already follows. </summary>
        HashSet<string> followedSubjects = [];

        /// <summary> Subjects whose follow is being written right now, so a second press cannot land mid-write. </summary>
        readonly HashSet<string> busySubjects = [];

        /// <summary> The ordering the reader chose. </summary>
        SubjectBoardOrder ChosenOrder = SubjectBoardOrder.MostNamed;

        /// <summary> True once a load has finished, so later reloads refresh in place instead of blanking the page. </summary>
        bool hasLoadedOnce;

        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.WallChanged,
            MainEvents.Names.SubjectFollowChanged
        ];

        /// <summary> True while the very first load runs and there is nothing on screen to keep. </summary>
        bool IsFirstLoad => IsLoading && !hasLoadedOnce;

        /// <summary> True while a reload refreshes a page that is already drawn; only the header spinner reacts. </summary>
        bool IsRefreshing => IsLoading && hasLoadedOnce;

        /// <summary> Frosted bar the sticky header is painted on. </summary>
        static string HeaderSurfaceStyle => AppStyles.BuildBarSurface(pinnedToBottom: false);

        /// <summary> Reads the square and which of its subjects this reader already follows. </summary>
        /// <returns> A task that completes once the page has both. </returns>
        protected override async Task LoadAsync()
        {
            Task<IReadOnlyList<SubjectStanding>> boardRead = SubjectBoardService.ReadAsync(ChosenOrder, SubjectsShown);
            Task<IReadOnlyList<string>> followedRead = SubjectFollowService.ReadFollowedSubjectsAsync(SessionService.CurrentAddress);

            await Task.WhenAll(boardRead, followedRead);

            standings = await boardRead;
            followedSubjects = [.. await followedRead];
            hasLoadedOnce = true;
        }

        /// <summary>
        /// Reads the square in a different order. The counting is the same either way, so this is arithmetic on
        /// what is already known rather than another read.
        /// </summary>
        /// <param name="order"> The ordering the reader picked. </param>
        /// <returns> A task that completes once the page has been read in that order. </returns>
        async Task SelectOrderAsync(SubjectBoardOrder order)
        {
            if (order == ChosenOrder) return;

            ChosenOrder = order;
            await ReloadAsync();
        }

        /// <summary> What a reader is told each ordering does. </summary>
        /// <param name="order"> The ordering being described. </param>
        /// <returns> A short English label. </returns>
        static string DescribeOrder(SubjectBoardOrder order) => order switch
        {
            SubjectBoardOrder.MostNamed => "Most named",
            SubjectBoardOrder.LeastNamed => "Least named",
            SubjectBoardOrder.NewestFirst => "Newest first",
            SubjectBoardOrder.LongestRunning => "Going longest",
            _ => string.Empty
        };

        /// <summary> A subject as it is written, with the mark it is named by. </summary>
        /// <param name="standing"> The row being drawn. </param>
        /// <returns> The label for that row. </returns>
        static string LabelFor(SubjectStanding standing) => WrittenText.SubjectMark + standing.Subject;

        /// <summary> The one line under a subject's name: the numbers that put it where it is. </summary>
        /// <param name="standing"> The row being drawn. </param>
        /// <returns> The standing, in words. </returns>
        static string StandingLineFor(SubjectStanding standing)
        {
            string times = standing.MentionCount == 1
                ? OnceWord
                : string.Format(ManyTimesFormat, standing.MentionCount);

            string line = string.Format(StandingFormat, times, RelativeTimeFormatter.Format(standing.NewestAtUnixMs));

            // Said only when there is a run to speak of: a subject named twice in the same minute has not been
            // "going" for anything, and saying so would be arithmetic dressed up as a story.
            if (standing.MentionCount > 1 && standing.RunningForMs > 0)
            {
                line += string.Format(RunningForFormat, RelativeTimeFormatter.FormatDuration(standing.RunningForMs));
            }

            return line;
        }

        /// <summary> True when the reader already follows a subject. </summary>
        /// <param name="subject"> The subject in its stored form. </param>
        /// <returns> Whether it is followed. </returns>
        bool IsFollowing(string subject) => followedSubjects.Contains(subject);

        /// <summary> True while that subject's follow is being written. </summary>
        /// <param name="subject"> The subject in its stored form. </param>
        /// <returns> Whether a write is in flight for it. </returns>
        bool IsBusy(string subject) => busySubjects.Contains(subject);

        /// <summary>
        /// Starts or stops following one subject. The row is written first and the pill flipped after, so the pill
        /// never claims something the store has not accepted.
        /// </summary>
        /// <param name="subject"> The subject in its stored form. </param>
        /// <returns> A task that completes once the interest has been written or withdrawn. </returns>
        async Task ToggleFollowAsync(string subject)
        {
            if (!SessionService.IsSignedIn || !busySubjects.Add(subject)) return;

            try
            {
                if (followedSubjects.Contains(subject))
                {
                    await SubjectFollowService.UnfollowAsync(Account, subject);
                    followedSubjects.Remove(subject);
                }
                else
                {
                    await SubjectFollowService.FollowAsync(Account, subject);
                    followedSubjects.Add(subject);
                }
            }
            finally
            {
                busySubjects.Remove(subject);
            }
        }

        /// <summary> Opens one subject's own page. </summary>
        /// <param name="subject"> The subject in its stored form. </param>
        void OpenSubject(string subject) => NavManager.NavigateTo($"{Subject.SubjectRoutePrefix}/{subject}");

        /// <summary> Leaves the square for the wall. </summary>
        void GoBackToWall() => NavManager.NavigateTo(NavigationConstants.Wall.Link);
    }
}
