using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// A reader's own shelf: the posts they kept and what they wrote about each of them. Sealed to them, so this
    /// page is the only place any of it is readable.
    /// </summary>
    public partial class Clippings
    {
        /// <summary> Where this page lives. </summary>
        public const string ClippingsRoute = "/clippings";

        /// <summary> Heading at the top of the page. </summary>
        const string PageHeadline = "Your shelf";

        /// <summary> Line under it, saying the one thing worth saying about a shelf. </summary>
        const string PageSubtitle = "What you kept, and why. Sealed to you — the server sees that you keep things and nothing else.";

        /// <summary> Emoji beside the heading. </summary>
        const string PageEmoji = "📚";

        /// <summary> Emoji on the placeholder shown when the shelf is empty. </summary>
        const string EmptyEmoji = "🔖";

        /// <summary> Headline of that placeholder. </summary>
        const string EmptyHeadline = "Nothing on the shelf yet";

        /// <summary> Supporting line of it. </summary>
        const string EmptyDescription = "Keep a post from anywhere in the app and it gathers here, with whatever you wanted to remember about it.";

        /// <summary> Emoji on the placeholder shown when the shelf could not be read. </summary>
        const string LoadFailedEmoji = "🌧️";

        /// <summary> Headline of that placeholder. </summary>
        const string LoadFailedHeadline = "This didn't come through";

        /// <summary> Shown in place of a post that no longer exists, so the note it carries is not orphaned. </summary>
        const string PostGoneLabel = "The post this was kept from is gone. Your note stays.";

        /// <summary> Text on the control that takes a clipping off the shelf. </summary>
        const string ForgetLabel = "Take it off the shelf";

        /// <summary> The shelf, newest first. </summary>
        IReadOnlyList<KeptClipping> shelf = [];

        /// <summary> The posts behind the shelf, keyed by id; a missing entry means that post is gone. </summary>
        IReadOnlyDictionary<string, PostData> posts = new Dictionary<string, PostData>();

        /// <summary> One profile per author drawn on the page, keyed by address. </summary>
        IReadOnlyDictionary<string, ProfileData> profiles = new Dictionary<string, ProfileData>();

        /// <summary> True once a load has finished, so later reloads refresh in place instead of blanking the page. </summary>
        bool hasLoadedOnce;

        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.SessionChanged
        ];

        /// <summary> True while the very first load runs and there is nothing on screen to keep. </summary>
        bool IsFirstLoad => IsLoading && !hasLoadedOnce;

        /// <summary> Frosted bar the header is painted on. </summary>
        static string HeaderSurfaceStyle => AppStyles.BuildBarSurface(pinnedToBottom: false);

        /// <summary> Reads and opens the shelf, then the posts and profiles it names. </summary>
        /// <returns> A task that completes once the page has all of it. </returns>
        protected override async Task LoadAsync()
        {
            IReadOnlyList<KeptClipping> kept = await ClippingService.ReadShelfAsync(Account);

            PostData?[] read = await Task.WhenAll(kept.Select(clipping => WallService.ReadAsync(clipping.PostId)));

            Dictionary<string, PostData> byId = new(kept.Count);
            for (int index = 0; index < kept.Count; index++)
            {
                if (read[index] is PostData post) byId[kept[index].PostId] = post;
            }

            shelf = kept;
            posts = byId;
            profiles = await ReadProfilesAsync([.. byId.Values.Select(post => post.AuthorAddress)]);
            hasLoadedOnce = true;
        }

        /// <summary> Reads one profile per distinct author named on the page. </summary>
        /// <param name="addresses"> The authors. </param>
        /// <returns> The profiles that exist, keyed by address. </returns>
        static async Task<IReadOnlyDictionary<string, ProfileData>> ReadProfilesAsync(IReadOnlyList<string> addresses)
        {
            string[] distinct = [.. addresses.Distinct(StringComparer.Ordinal)];
            ProfileData?[] read = await Task.WhenAll(distinct.Select(ProfileService.ReadAsync));

            Dictionary<string, ProfileData> byAddress = new(distinct.Length, StringComparer.Ordinal);
            for (int index = 0; index < distinct.Length; index++)
            {
                if (read[index] is ProfileData profile) byAddress[distinct[index]] = profile;
            }

            return byAddress;
        }

        /// <summary> The post one clipping was kept from, or null when it is gone. </summary>
        /// <param name="postId"> The post's id. </param>
        /// <returns> The post, or null. </returns>
        PostData? PostFor(string postId) => posts.GetValueOrDefault(postId);

        /// <summary> The profile of one author, or null when they published none. </summary>
        /// <param name="address"> The author. </param>
        /// <returns> Their profile, or null. </returns>
        ProfileData? ProfileFor(string address) => profiles.GetValueOrDefault(address);

        /// <summary> Takes one clipping off the shelf and reads the shelf again. </summary>
        /// <param name="clippingId"> The clipping to forget. </param>
        /// <returns> A task that completes once it is gone and the page has been read again. </returns>
        async Task ForgetAsync(string clippingId)
        {
            await ClippingService.ForgetAsync(Account, clippingId);
            await ReloadAsync();
        }

        /// <summary> Opens the conversation under one kept post. </summary>
        /// <param name="postId"> The post to open. </param>
        void OpenPost(string postId) => NavManager.NavigateTo(PostThread.LinkTo(postId));

        /// <summary> Opens one author's profile. </summary>
        /// <param name="address"> The author. </param>
        void OpenAuthor(string address) => NavManager.NavigateTo($"{NavigationConstants.Profile.Link}/{address}");

        /// <summary> Goes back to the reader's own profile, which is where the shelf is reached from. </summary>
        void GoBack() => NavManager.NavigateTo(NavigationConstants.Profile.Link);
    }
}
