using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary> Which of the wall's two lists of posts is on screen. </summary>
    public enum WallFeed
    {
        /// <summary> Only posts written by the accounts the reader chose to follow. </summary>
        Following,

        /// <summary> Posts from every account, so the reader can find people they do not follow yet. </summary>
        Discover
    }

    /// <summary>
    /// The wall: what the reader writes, the two feeds they can read, and every post card in whichever feed is
    /// selected. The page owns all the reads and all the writes — the cards below it only draw what they are
    /// handed and report which button was pressed.
    /// </summary>
    public partial class Wall
    {
        /// <summary> Emoji beside the page heading: the teapot the whole app is named after. </summary>
        const string PageEmoji = "🫖";

        /// <summary> The page's heading. </summary>
        const string PageHeadline = "Your teahouse";

        /// <summary> Line under the heading, naming what makes this wall different from any other. </summary>
        const string PageSubtitle = "Every word here is signed by the hand that wrote it.";

        /// <summary> Label on the tab showing only followed accounts. </summary>
        const string FollowingTabLabel = "Following";

        /// <summary> Label on the tab showing everybody's posts. </summary>
        const string DiscoverTabLabel = "Discover";

        /// <summary> Emoji on the following tab: the people the reader already chose. </summary>
        const string FollowingTabEmoji = "🤝";

        /// <summary> Emoji on the discover tab: looking beyond the accounts already followed. </summary>
        const string DiscoverTabEmoji = "🔭";

        /// <summary> Tooltip on the following tab. </summary>
        const string FollowingTabHint = "Posts from the accounts you follow";

        /// <summary> Tooltip on the discover tab. </summary>
        const string DiscoverTabHint = "Posts from everybody";

        /// <summary> Class appended to whichever tab is selected; it carries the filled, glowing look. </summary>
        const string ActiveTabClass = "is-active";

        /// <summary> Value written into <c>aria-selected</c> on the tab that is showing. </summary>
        const string SelectedTabValue = "true";

        /// <summary> Value written into <c>aria-selected</c> on the tab that is not showing. </summary>
        const string UnselectedTabValue = "false";

        /// <summary> Line under the throbber while the first page of a feed is being read. </summary>
        const string LoadingLabel = "Warming the pot…";

        /// <summary> Emoji over the message shown when a feed could not be read. </summary>
        const string LoadFailureEmoji = "🌧️";

        /// <summary> Text on the button that runs a failed load again. </summary>
        const string RetryLabel = "Try again";

        /// <summary> Emoji for the placeholder shown when the reader follows nobody who has posted. </summary>
        const string EmptyFollowingEmoji = "🌱";

        /// <summary> Headline of that placeholder. </summary>
        const string EmptyFollowingHeadline = "Nothing steeping yet";

        /// <summary> Supporting line of that placeholder, pointing at the other tab. </summary>
        const string EmptyFollowingDescription = "Follow a few people and their posts will gather here. Discover is where you meet them.";

        /// <summary> Text on the button that switches the reader to the discover tab. </summary>
        const string GoToDiscoverLabel = "Wander over to Discover";

        /// <summary> Text on the button that leads two steps out from the reader's own people. </summary>
        const string GoToNearbyLabel = "Two steps out";

        /// <summary> Emoji for the placeholder shown when the whole app has no posts to read. </summary>
        const string EmptyDiscoverEmoji = "🍃";

        /// <summary> Headline of that placeholder. </summary>
        const string EmptyDiscoverHeadline = "The teahouse is quiet";

        /// <summary> Supporting line of that placeholder, inviting the reader to write the first post. </summary>
        const string EmptyDiscoverDescription = "Nobody has written anything yet. Say the first word and the rest will follow.";

        /// <summary> Emoji at the top of the report sheet. </summary>
        const string ReportEmoji = "🚩";

        /// <summary> Heading of the report sheet. </summary>
        const string ReportTitle = "Report this post";

        /// <summary> Line under that heading, saying what picking a reason does. </summary>
        const string ReportSubtitle = "Pick what is wrong with it. Your report hands this post's text to the moderators.";

        /// <summary> Line under the throbber while a report is being written. </summary>
        const string ReportSendingLabel = "Sending your report…";

        /// <summary> Text on the button that closes the report sheet without reporting anything. </summary>
        const string CancelLabel = "Never mind";

        /// <summary> Route a post's own page lives at; the post id is appended to it. </summary>
        const string PostRoutePrefix = "/post/";

        /// <summary> Route an account's profile lives at; the address is appended to it. </summary>
        const string ProfileRoutePrefix = "/profile/";

        /// <summary> Inside spacing of this page's pill buttons: wide enough to stay comfortable to tap on a phone. </summary>
        static readonly string ActionButtonPadding = $"{AppMeasures.Space.Px10}px {AppMeasures.Space.Px20}px";

        /// <summary> Line above the ordering strip, saying plainly what the strip does and what it is ordering. </summary>
        const string OrderStripLabel = "You choose the order. This device decides it, and nothing about it is sent anywhere.";

        /// <summary> Text on the button that throws the shuffle again. </summary>
        const string ReshuffleLabel = "Throw again";

        /// <summary> Marks that button. </summary>
        const string ReshuffleEmoji = "🎲";

        /// <summary> Tooltip on it. </summary>
        const string ReshuffleHint = "Deal this page from a new seed";

        /// <summary> Class appended to whichever order is chosen. </summary>
        const string ActiveOrderClass = "is-active";

        /// <summary> The orders offered, in the order they are drawn. </summary>
        static readonly IReadOnlyList<FeedOrder> SelectableOrders = FeedRecipe.Choices;

        /// <summary> The tabs offered, in the order they are drawn. </summary>
        static readonly WallFeed[] SelectableFeeds = Enum.GetValues<WallFeed>();

        /// <summary> Every reason the report sheet offers, in the order <see cref="ReportReason"/> declares them. </summary>
        static readonly ReportReason[] OfferedReportReasons = Enum.GetValues<ReportReason>();

        /// <summary>
        /// Reloads on a new post or like (wall), on a follow that changes what the following feed contains,
        /// on a block or report that hides an account, and on a sign-in or sign-out that changes who is reading.
        /// </summary>
        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.WallChanged,
            MainEvents.Names.FollowChanged,
            MainEvents.Names.SubjectFollowChanged,
            MainEvents.Names.ModerationChanged,
            MainEvents.Names.SessionChanged
        ];

        /// <summary> The tab currently showing; the wall opens on the reader's own people. </summary>
        WallFeed SelectedFeed = WallFeed.Following;

        /// <summary> Lines of the selected feed, newest first; each is a post plus whichever wall carried it here. </summary>
        IReadOnlyList<FeedEntry> Entries = [];

        /// <summary> One profile per distinct author in <see cref="Posts"/>, keyed by address; a value is null when that account has published no profile. </summary>
        Dictionary<string, ProfileData?> AuthorProfiles = [];

        /// <summary> The counts drawn under each post, keyed by post id. </summary>
        Dictionary<string, PostEngagement> Engagements = [];

        /// <summary> What the reader has typed into the composer but not published yet. </summary>
        string ComposerText = string.Empty;

        /// <summary> Media already uploaded for the post being written but not published yet. </summary>
        IReadOnlyList<MediaAttachment> ComposerAttachments = [];

        /// <summary> Answers typed into the composer for a question not published yet, blanks included. </summary>
        IReadOnlyList<string> ComposerPollChoices = [];

        /// <summary> Title typed for a long piece not published yet. </summary>
        string ComposerTitle = string.Empty;

        /// <summary> The long piece typed but not published yet. </summary>
        string ComposerLongBody = string.Empty;

        /// <summary> Post the composer is quoting, or null while a plain post is being written. </summary>
        PostData? ComposerQuotedPost;

        /// <summary> Profile of the composer's quoted author, or null when it could not be read. </summary>
        ProfileData? ComposerQuotedAuthorProfile;

        /// <summary> Originals of the quotes on screen, keyed by the quoted post's id; a missing entry means it was deleted. </summary>
        Dictionary<string, PostData> QuotedPosts = [];

        /// <summary> True while a post is being signed and stored, which locks the composer. </summary>
        bool IsPublishing;

        /// <summary> The post the report sheet is open for, or null while the sheet is closed. </summary>
        PostData? ReportedPost;

        /// <summary> True while a report is being written, which swaps the reasons for a throbber. </summary>
        bool IsReportInFlight;

        /// <summary> True while the report sheet should be on screen. </summary>
        bool IsReportOpen => ReportedPost is not null;

        /// <summary> Names the reader may publish under: themselves, and any page whose keys they hold. </summary>
        IReadOnlyList<WritingIdentity> WritingChoices = [];

        /// <summary> Address the next post will carry, which is the reader's own until they pick a page. </summary>
        string WritingAsAddress = string.Empty;

        /// <summary>
        /// The identity that will actually sign the next post. Falls back to the reader's own account when the
        /// chosen one is no longer among the choices — a page whose keys were taken back mid-session, say.
        /// </summary>
        WritingIdentity WritingAs
        {
            get
            {
                foreach (WritingIdentity choice in WritingChoices)
                {
                    if (choice.Address == WritingAsAddress) return choice;
                }

                return new WritingIdentity(Account.Public.Address, string.Empty, string.Empty, Account, IsPage: false);
            }
        }

        /// <summary> Emoji drawn in the composer's bubble: whichever name the next post is going out under. </summary>
        string ComposerAvatar
        {
            get
            {
                foreach (WritingIdentity choice in WritingChoices)
                {
                    if (choice.Address == WritingAsAddress) return choice.Avatar;
                }

                return SessionService.CurrentProfile?.Avatar ?? ProfileData.DefaultAvatar;
            }
        }

        /// <summary> Takes the reader's choice of who to post as. </summary>
        /// <param name="address"> Address they picked. </param>
        void HandleWritingAsChanged(string address) => WritingAsAddress = address;

        /// <summary> The frosted surface the two tabs sit on, built from the same glass recipe every other surface uses. </summary>
        string TabStripStyle => AppStyles.BuildAcrylicStyle(AcrylicLevel.Subtle, AppMeasures.Blur.Subtle);

        /// <summary>
        /// Reads the selected feed and everything its cards need to draw: one profile per author, and the like,
        /// comment and repost counts of every post.
        /// </summary>
        /// <returns> A task that completes once the page has all of it. </returns>
        protected override async Task LoadAsync()
        {
            string viewerAddress = SessionService.CurrentAddress;

            IReadOnlyList<FeedEntry> entries = SelectedFeed == WallFeed.Following
                ? await FeedService.ReadFollowingFeedAsync(viewerAddress)
                : await FeedService.ReadDiscoverFeedAsync(viewerAddress);

            PostData[] posts = [.. entries.Select(entry => entry.Post).DistinctBy(post => post.PostId)];
            Dictionary<string, PostData> quoted = await ReadQuotedPostsAsync(posts);

            // Quoted authors and the accounts that passed a post on are looked up alongside the feed's own authors,
            // so every name on a card is a name rather than an address.
            string[] addresses =
            [
                .. posts.Select(post => post.AuthorAddress),
                .. quoted.Values.Select(post => post.AuthorAddress),
                .. entries.Where(entry => entry.IsRepost).Select(entry => entry.ReposterAddress)
            ];

            Dictionary<string, ProfileData?> authorProfiles = await ReadProfilesAsync(addresses);
            Dictionary<string, PostEngagement> engagements = await FeedService.ReadEngagementsAsync(posts, viewerAddress);

            // Read here rather than once at startup, so a page founded or handed over during this session appears
            // in the picker on the next refresh instead of after a reload.
            WritingChoices = await WritingIdentities.ReadForAsync(Account);
            if (WritingChoices.All(choice => choice.Address != WritingAsAddress)) WritingAsAddress = viewerAddress;

            Entries = FeedOrdering.Apply(entries, engagements, FeedRecipe.Order, FeedRecipe.ShuffleSeed);
            QuotedPosts = quoted;
            AuthorProfiles = authorProfiles;
            Engagements = engagements;
        }

        /// <summary>
        /// Reads the feed in a different order. Nothing is fetched again: the page is already in memory and the
        /// order is arithmetic on it, so the wall rearranges under the reader's finger rather than after a wait.
        /// </summary>
        /// <param name="order"> The order the reader picked. </param>
        /// <returns> A task that completes once the choice has been stored. </returns>
        async Task SelectOrderAsync(FeedOrder order)
        {
            if (order == FeedRecipe.Order) return;

            await FeedRecipe.ApplyAndRememberAsync(order);
            Entries = FeedOrdering.Apply(Entries, Engagements, FeedRecipe.Order, FeedRecipe.ShuffleSeed);
        }

        /// <summary> Throws the shuffle again, which is the only thing that changes a shuffled page. </summary>
        /// <returns> A task that completes once the new seed has been stored. </returns>
        async Task ReshuffleAsync()
        {
            await FeedRecipe.ReshuffleAsync();
            Entries = FeedOrdering.Apply(Entries, Engagements, FeedRecipe.Order, FeedRecipe.ShuffleSeed);
        }

        /// <summary> The line drawn above one post, saying why it is where it is. </summary>
        /// <param name="entry"> The line being drawn. </param>
        /// <param name="position"> Where it sits, counting from one. </param>
        /// <returns> The receipt for that line. </returns>
        string OrderReceiptFor(FeedEntry entry, int position)
            => FeedOrdering.Explain(entry, EngagementFor(entry.Post), FeedRecipe.Order, position);

        /// <summary>
        /// Reads the originals behind the quotes in a feed. A quoted post that no longer exists simply does not
        /// come back, and the card draws the "taken down" line instead — which is the truth, and better than
        /// showing a copy of something its author removed.
        /// </summary>
        /// <param name="posts"> The posts about to be drawn. </param>
        /// <returns> The quoted originals keyed by their own id. </returns>
        static async Task<Dictionary<string, PostData>> ReadQuotedPostsAsync(IReadOnlyList<PostData> posts)
        {
            string[] quotedIds = [.. posts.Where(post => post.IsQuoting).Select(post => post.QuotedPostId).Distinct()];
            if (quotedIds.Length == 0) return [];

            PostData?[] originals = await Task.WhenAll(quotedIds.Select(WallService.ReadAsync));

            Dictionary<string, PostData> byPostId = new(quotedIds.Length);
            foreach (PostData? original in originals)
            {
                if (original is not null) byPostId[original.PostId] = original;
            }

            return byPostId;
        }

        /// <summary>
        /// Reads one profile per distinct address. Several posts by the same person share the one read, which is
        /// what keeps a feed full of one prolific author down to a single profile fetch.
        /// </summary>
        /// <param name="addresses"> The accounts named anywhere on the cards about to be drawn. </param>
        /// <returns> Each account's profile keyed by address; a value is null when that account published none. </returns>
        static async Task<Dictionary<string, ProfileData?>> ReadProfilesAsync(IReadOnlyList<string> addresses)
        {
            string[] distinct = [.. addresses.Distinct()];
            ProfileData?[] profiles = await Task.WhenAll(distinct.Select(ProfileService.ReadAsync));

            Dictionary<string, ProfileData?> byAddress = new(distinct.Length);
            for (int index = 0; index < distinct.Length; index++)
            {
                byAddress[distinct[index]] = profiles[index];
            }

            return byAddress;
        }

        /// <summary> The profile of a post's author, or null while it has not been read or was never published. </summary>
        /// <param name="post"> The post being drawn. </param>
        /// <returns> The author's profile, or null. </returns>
        ProfileData? AuthorProfileFor(PostData post) => AuthorProfiles.GetValueOrDefault(post.AuthorAddress);

        /// <summary>
        /// Name of the account that passed a post on, or null when it arrived on its own — which is also what
        /// tells the card whether to draw that line at all.
        /// </summary>
        /// <param name="entry"> The feed line being drawn. </param>
        /// <returns> The reposter's chosen name, the readable head of their address, or null. </returns>
        string? ReposterNameFor(FeedEntry entry)
        {
            if (!entry.IsRepost) return null;

            ProfileData? profile = AuthorProfiles.GetValueOrDefault(entry.ReposterAddress);
            return string.IsNullOrWhiteSpace(profile?.DisplayName)
                ? ProfileService.FallbackDisplayName(entry.ReposterAddress)
                : profile.DisplayName;
        }

        /// <summary> The counts for one post, all zero while they have not been read. </summary>
        /// <param name="post"> The post being drawn. </param>
        /// <returns> That post's like and comment counts. </returns>
        PostEngagement EngagementFor(PostData post) => Engagements.GetValueOrDefault(post.PostId);

        /// <summary> Label on one tab. </summary>
        /// <param name="feed"> The tab's feed. </param>
        /// <returns> The word drawn on it. </returns>
        static string FeedLabel(WallFeed feed) => feed switch
        {
            WallFeed.Following => FollowingTabLabel,
            _ => DiscoverTabLabel
        };

        /// <summary> Emoji on one tab. </summary>
        /// <param name="feed"> The tab's feed. </param>
        /// <returns> The emoji drawn before its label. </returns>
        static string FeedEmoji(WallFeed feed) => feed switch
        {
            WallFeed.Following => FollowingTabEmoji,
            _ => DiscoverTabEmoji
        };

        /// <summary> Tooltip on one tab, saying whose posts it shows. </summary>
        /// <param name="feed"> The tab's feed. </param>
        /// <returns> The hovered description. </returns>
        static string FeedHint(WallFeed feed) => feed switch
        {
            WallFeed.Following => FollowingTabHint,
            _ => DiscoverTabHint
        };

        /// <summary> Name of one report reason, as the reader reads it. </summary>
        /// <param name="reason"> The reason offered. </param>
        /// <returns> Its label. </returns>
        static string ReasonLabel(ReportReason reason) => reason switch
        {
            ReportReason.Spam => "Spam",
            ReportReason.Harassment => "Harassment",
            ReportReason.Violence => "Violence",
            ReportReason.SexualContent => "Sexual content",
            ReportReason.Impersonation => "Pretending to be someone",
            _ => "Something else"
        };

        /// <summary> Emoji beside one report reason, so the sheet can be read at a glance. </summary>
        /// <param name="reason"> The reason offered. </param>
        /// <returns> Its emoji. </returns>
        static string ReasonEmoji(ReportReason reason) => reason switch
        {
            ReportReason.Spam => "📢",
            ReportReason.Harassment => "💢",
            ReportReason.Violence => "⚠️",
            ReportReason.SexualContent => "🔞",
            ReportReason.Impersonation => "🎭",
            _ => "❓"
        };

        /// <summary> Takes each keystroke from the composer, which the page owns rather than the composer. </summary>
        /// <param name="text"> The textarea's new contents. </param>
        void HandleComposerTextChanged(string text) => ComposerText = text;

        /// <summary> Takes the composer's attached media, which the page owns alongside the text. </summary>
        /// <param name="attachments"> The media currently attached. </param>
        void HandleComposerAttachmentsChanged(IReadOnlyList<MediaAttachment> attachments) => ComposerAttachments = attachments;

        /// <summary> Takes the answers as they are typed. </summary>
        /// <param name="choices"> The answers currently in the composer, blanks included. </param>
        void HandleComposerPollChoicesChanged(IReadOnlyList<string> choices) => ComposerPollChoices = choices;

        /// <summary> Takes the title of a long piece as it is typed. </summary>
        /// <param name="title"> The title box's new contents. </param>
        void HandleComposerTitleChanged(string title) => ComposerTitle = title;

        /// <summary> Takes the long piece as it is typed. </summary>
        /// <param name="body"> The body box's new contents. </param>
        void HandleComposerLongBodyChanged(string body) => ComposerLongBody = body;

        /// <summary> Points the composer at a post to speak about, and scrolls nothing: the composer is already at the top. </summary>
        /// <param name="post"> Post being quoted. </param>
        void StartQuoting(PostData post)
        {
            ComposerQuotedPost = post;
            ComposerQuotedAuthorProfile = AuthorProfileFor(post);
        }

        /// <summary> Drops the quote so the composer writes a plain post again. </summary>
        void StopQuoting()
        {
            ComposerQuotedPost = null;
            ComposerQuotedAuthorProfile = null;
        }

        /// <summary> The original a post quotes, or null when it quotes nothing or the original is gone. </summary>
        /// <param name="post"> Post being drawn. </param>
        /// <returns> The quoted post, or null. </returns>
        PostData? QuotedPostFor(PostData post)
            => post.IsQuoting && QuotedPosts.TryGetValue(post.QuotedPostId, out PostData? quoted) ? quoted : null;

        /// <summary> Profile of the author of the post a post quotes. </summary>
        /// <param name="post"> Post being drawn. </param>
        /// <returns> The quoted author's profile, or null when it could not be read. </returns>
        ProfileData? QuotedAuthorProfileFor(PostData post)
            => QuotedPostFor(post) is PostData quoted ? AuthorProfileFor(quoted) : null;

        /// <summary>
        /// Switches tabs. The old feed's posts are dropped first so the throbber — not the previous tab's list —
        /// is what the reader sees while the new one is read.
        /// </summary>
        /// <param name="feed"> The tab to show. </param>
        /// <returns> A task that completes once the new feed has been read. </returns>
        async Task SelectFeedAsync(WallFeed feed)
        {
            if (feed == SelectedFeed) return;

            SelectedFeed = feed;
            Entries = [];
            AuthorProfiles = [];
            Engagements = [];

            await ReloadAsync();
        }

        /// <summary> Moves the reader to the discover tab, from the placeholder shown on an empty following feed. </summary>
        /// <returns> A task that completes once discover has been read. </returns>
        Task ShowDiscoverAsync() => SelectFeedAsync(WallFeed.Discover);

        /// <summary>
        /// Signs and publishes what the reader wrote, then empties the composer. The composer is only cleared on a
        /// post that was actually stored, so text the service refused stays on screen to be fixed.
        /// </summary>
        /// <returns> A task that completes once the post is stored. </returns>
        async Task PublishAsync()
        {
            if (IsPublishing) return;

            IsPublishing = true;
            try
            {
                PostData? published = await WallService.PublishAsync(
                    WritingAs.Signer, ComposerText, ComposerAttachments, ComposerQuotedPost?.PostId ?? string.Empty,
                    pollChoices: ComposerPollChoices, title: ComposerTitle, longBody: ComposerLongBody);

                if (published is null) return;

                ComposerText = string.Empty;
                ComposerAttachments = [];
                ComposerPollChoices = [];
                ComposerTitle = string.Empty;
                ComposerLongBody = string.Empty;
                StopQuoting();
            }
            finally
            {
                IsPublishing = false;
            }
        }

        /// <summary>
        /// Adds or removes the reader's like. The counts on screen are not patched here: the service announces the
        /// change and this page is subscribed to that announcement, so the numbers come back from a fresh read.
        /// </summary>
        /// <param name="post"> The post whose heart was tapped. </param>
        /// <returns> A task that completes once the like has been written. </returns>
        Task ToggleLikeAsync(PostData post) => WallService.ToggleLikeAsync(post, Account.Public);

        /// <summary> Carries a post onto the reader's own wall, or takes it back when it is already there. </summary>
        /// <param name="post"> The post whose arrows were tapped. </param>
        /// <returns> A task that completes once the repost has been written. </returns>
        Task ToggleRepostAsync(PostData post) => WallService.ToggleRepostAsync(post, Account);

        /// <summary> Removes one of the reader's own posts. </summary>
        /// <param name="post"> The post to remove. </param>
        /// <returns> A task that completes once the post is gone. </returns>
        Task DeletePostAsync(PostData post) => WallService.DeleteAsync(post, Account.Public);

        /// <summary> Opens the report sheet for one post. </summary>
        /// <param name="post"> The post being reported. </param>
        void OpenReport(PostData post) => ReportedPost = post;

        /// <summary> Closes the report sheet, leaving a report that is already being written to finish. </summary>
        void CloseReport()
        {
            if (IsReportInFlight) return;

            ReportedPost = null;
        }

        /// <summary> Files the report under the reason the reader picked and closes the sheet. </summary>
        /// <param name="reason"> The category the reader chose. </param>
        /// <returns> A task that completes once the report is stored. </returns>
        async Task SubmitReportAsync(ReportReason reason)
        {
            if (ReportedPost is null || IsReportInFlight) return;

            IsReportInFlight = true;
            try
            {
                await ModerationService.ReportPostAsync(Account, ReportedPost, reason);
            }
            finally
            {
                IsReportInFlight = false;
                ReportedPost = null;
            }
        }

        /// <summary> Opens an author's profile. </summary>
        /// <param name="address"> Address of the account whose profile to open. </param>
        void OpenAuthor(string address) => NavManager.NavigateTo($"{ProfileRoutePrefix}{address}");

        /// <summary> Opens one post's own page, where its comments are read and written. </summary>
        /// <param name="postId"> Id of the post to open. </param>
        void OpenComments(string postId) => NavManager.NavigateTo($"{PostRoutePrefix}{postId}");

        /// <summary> Opens the screen listing accounts the reader's own people already follow. </summary>
        void OpenNearby() => NavManager.NavigateTo(Nearby.NearbyRoute);
    }
}
