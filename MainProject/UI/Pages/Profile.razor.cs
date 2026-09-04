using ChaySocial.MainProject.Constants;
using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;
using Microsoft.AspNetCore.Components;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// One account's page. The same component serves the signed-in owner and any visitor looking at somebody else,
    /// because the two views differ only in which actions they offer: the owner edits their own name, avatar and bio
    /// and reaches their settings, while a visitor follows, messages, blocks or reports. Underneath both sits the same
    /// header — avatar, name, address, bio, joined date, follower and following counts — and the same list of that
    /// account's posts.
    /// </summary>
    public partial class Profile
    {
        /// <summary>
        /// Address taken from the route. Null on the plain "/Profile" route and treated as "show me my own page",
        /// which is why the parameter is optional rather than required.
        /// </summary>
        [Parameter] public string? Address { get; set; }

        /// <summary> Emoji the owner may pick from for their avatar; one tap replaces the picture a photo would be. </summary>
        static readonly string[] AvatarChoices =
        [
            "🫖", "🌸", "🍡", "🐣", "🍋", "🪼", "🌙", "🧁",
            "🐧", "🍄", "🪷", "🍑", "🐳", "☁️", "🌻", "🧸",
            "🍵", "🦊", "🫧", "🌈", "🦋", "🐝", "🍒", "⭐"
        ];

        /// <summary> Every reason a report may carry, in the order the enum declares them, for the reason picker. </summary>
        static readonly ReportReason[] ReportReasons = Enum.GetValues<ReportReason>();

        /// <summary> Diameter of the avatar at the top of the page: the largest the app draws, because this page is about one account. </summary>
        const int HeaderAvatarDiameterPx = AppMeasures.Size.Px100;

        /// <summary> Characters of the address kept at each end in the header, where there is room for more than a list row allows. </summary>
        const int HeaderAddressVisibleCharacters = 6;

        /// <summary> Visible lines of the bio editor, tall enough to show a whole short bio without scrolling. </summary>
        const int BioFieldRowCount = 3;

        /// <summary> Visible lines of the report detail field. </summary>
        const int ReportDetailRowCount = 3;

        /// <summary> Diameter of the throbber that replaces a button's label while its action is in flight. </summary>
        const int InlineSpinnerDiameterPx = AppMeasures.Size.Px16;

        /// <summary> Ring thickness of that throbber, thinned from the app default because the throbber itself is small. </summary>
        const int InlineSpinnerBorderPx = AppMeasures.Border.Medium;

        /// <summary> Reason a report starts on, so pressing send without touching the picker still files something meaningful. </summary>
        const ReportReason DefaultReportReason = ReportReason.Spam;

        /// <summary> Where the owner's device-level preferences live. Not a navigation tab, so the route is named here rather than read from <c>NavigationConstants</c>. </summary>
        const string SettingsRoute = "/settings";

        /// <summary> Class marking the avatar the owner currently has selected; defined in <c>ProfileCss</c>. </summary>
        const string PickedAvatarClass = "profile-avatar-choice--picked";

        /// <summary> Class marking the report reason currently selected; defined in <c>ProfileCss</c>. </summary>
        const string PickedReasonClass = "profile-reason--picked";

        /// <summary> Value written into <c>aria-pressed</c> for a selected toggle. Text, because the attribute is tri-state rather than a boolean flag. </summary>
        const string PressedTrue = "true";

        /// <summary> Value written into <c>aria-pressed</c> for an unselected toggle. </summary>
        const string PressedFalse = "false";

        /// <summary> Id tying the display-name label to its field. </summary>
        const string DisplayNameFieldId = "profile-display-name-field";

        /// <summary> Id tying the bio label to its field. </summary>
        const string BioFieldId = "profile-bio-field";

        /// <summary> Marks a notice about something that just succeeded. </summary>
        const string NoticeEmoji = "✨";

        /// <summary> Marks the card shown when the page could not be loaded at all. </summary>
        const string FailureEmoji = "🫧";

        /// <summary> Marks the joined date. </summary>
        const string JoinedEmoji = "🌱";

        /// <summary> Marks the button that opens the profile editor. </summary>
        const string EditEmoji = "✏️";

        /// <summary> Marks the button that saves the edited profile. </summary>
        const string SaveEmoji = "💾";

        /// <summary> Marks the link to the owner's settings. </summary>
        const string SettingsEmoji = "⚙️";

        /// <summary> Marks the button that opens a private conversation. </summary>
        const string MessageEmoji = "💌";

        /// <summary> Marks blocking, and the block badge on a blocked account's header. </summary>
        const string BlockEmoji = "🚫";

        /// <summary> Marks lifting a block. </summary>
        const string UnblockEmoji = "🤍";

        /// <summary> Marks reporting. </summary>
        const string ReportEmoji = "🚩";

        /// <summary> Marks the button that sends the owner off to write their first post. </summary>
        const string WriteEmoji = "🪶";

        /// <summary> Shown on the toggle while the full address is hidden — pressing it reveals the address. </summary>
        const string RevealAddressEmoji = "🔎";

        /// <summary> Shown on the toggle while the full address is revealed — pressing it hides the address again. </summary>
        const string HideAddressEmoji = "🙈";

        /// <summary> Emoji over the empty state on the owner's own, still empty, page. </summary>
        const string OwnEmptyEmoji = "🌱";

        /// <summary> Emoji over the empty state on somebody else's still empty page. </summary>
        const string OtherEmptyEmoji = "🍃";

        /// <summary> Label on the button that retries a failed load. </summary>
        const string RetryLabel = "Try again";

        /// <summary> Label on the button that opens the profile editor. </summary>
        const string EditProfileLabel = "Edit profile";

        /// <summary> Label on the link to the owner's settings. </summary>
        const string SettingsLabel = "Settings";

        /// <summary> Label on the button that opens a private conversation with the shown account. </summary>
        const string MessageLabel = "Message";

        /// <summary> Label on the button that starts blocking the shown account. </summary>
        const string BlockLabel = "Block";

        /// <summary> Label on the button that lifts an existing block. </summary>
        const string UnblockLabel = "Unblock";

        /// <summary> Label on the button that opens the report form. </summary>
        const string ReportLabel = "Report";

        /// <summary> Label on the button that stores the edited profile. </summary>
        const string SaveLabel = "Save";

        /// <summary> Label on every button that walks away from a form or a dialog without changing anything. </summary>
        const string CancelLabel = "Cancel";

        /// <summary> Heading over the profile editor. </summary>
        const string EditorTitle = "Edit your profile";

        /// <summary> Heading over the emoji picker. </summary>
        const string AvatarFieldLabel = "Pick an avatar";

        /// <summary> Tooltip on one emoji in the picker. </summary>
        const string AvatarChoiceHint = "Use this as your avatar";

        /// <summary> Label of the display name field. </summary>
        const string DisplayNameFieldLabel = "Display name";

        /// <summary> Label of the bio field. </summary>
        const string BioFieldLabel = "About you";

        /// <summary> Prompt inside an empty display name field. </summary>
        const string DisplayNamePlaceholder = "What should people call you?";

        /// <summary> Prompt inside an empty bio field. </summary>
        const string BioPlaceholder = "A line or two about yourself…";

        /// <summary> Caption under the follower total. </summary>
        const string FollowersLabel = "Followers";

        /// <summary> Caption under the following total. </summary>
        const string FollowingLabel = "Following";

        /// <summary> Caption under the post total. </summary>
        const string PostsCountLabel = "Posts";

        /// <summary> Heading over the owner's own list of posts. </summary>
        const string OwnPostsSectionTitle = "Your posts";

        /// <summary> Heading over somebody else's list of posts. </summary>
        const string OtherPostsSectionTitle = "Posts";

        /// <summary> Leads the joined date, e.g. "Joined 4 March 2026". </summary>
        const string JoinedLabelPrefix = "Joined";

        /// <summary> Stands in for the joined date when the account has never published a profile to read one from. </summary>
        const string UnknownJoinLabel = "New around here";

        /// <summary> Badge on the header of an account the reader has blocked. </summary>
        const string BlockedChipLabel = "You blocked this account";

        /// <summary> Tooltip on the button that shows or hides the whole address. </summary>
        const string AddressToggleHint = "Show or hide the full address";

        /// <summary> Headline of the empty state on the owner's own page. </summary>
        const string OwnEmptyHeadline = "Nothing here yet";

        /// <summary> Supporting line of the empty state on the owner's own page. </summary>
        const string OwnEmptyDescription = "Whatever you publish shows up here, signed by you and nobody else.";

        /// <summary> Label on the button that sends the owner off to write their first post. </summary>
        const string WriteFirstPostLabel = "Write something";

        /// <summary> Headline of the empty state on somebody else's page. </summary>
        const string OtherEmptyHeadline = "No posts yet";

        /// <summary> Shown under the display name field when the owner cleared it. </summary>
        const string EmptyNameMessage = "Give yourself a name — even a short one.";

        /// <summary> Shown when the typed name is longer than a profile may carry. </summary>
        const string LongNameMessage = "That name is a little too long.";

        /// <summary> Shown when the typed bio is longer than a profile may carry. </summary>
        const string LongBioMessage = "That bio is a little too long.";

        /// <summary> Shown when storing the edited profile threw. </summary>
        const string SaveFailedMessage = "We couldn't save that. Give it another try?";

        /// <summary> Notice after the profile was stored. </summary>
        const string ProfileSavedNotice = "Profile saved";

        /// <summary> Notice after a block was placed. </summary>
        const string BlockedNotice = "You won't see this account any more";

        /// <summary> Notice when the block could not be placed, which is what an empty address or your own address gets. </summary>
        const string BlockRefusedNotice = "That account can't be blocked";

        /// <summary> Notice after a block was lifted. </summary>
        const string UnblockedNotice = "Block lifted";

        /// <summary> Notice after a report was filed. </summary>
        const string ReportFiledNotice = "Thanks — the report is on its way";

        /// <summary> Notice when the report was refused, which is what an over-long detail gets. </summary>
        const string ReportRefusedNotice = "That report was too long to send";

        /// <summary> Heading over the block confirmation, completed with the account's name. </summary>
        const string BlockModalTitlePrefix = "Block";

        /// <summary> Body of the block confirmation. </summary>
        const string BlockModalDescription = "Their posts leave your feeds and they leave your threads. You can lift this whenever you like.";

        /// <summary> Label on the button that actually places the block. </summary>
        const string BlockConfirmLabel = "Block them";

        /// <summary> Heading over the report form for an account. </summary>
        const string AccountReportTitle = "Report this account";

        /// <summary> Heading over the report form for a single post. </summary>
        const string PostReportTitle = "Report this post";

        /// <summary> Body of the report form for an account: nothing the account wrote is handed over. </summary>
        const string AccountReportDescription = "Pick what's wrong. Nothing this account wrote is sent along.";

        /// <summary> Body of the report form for a post, which says plainly that the post's text travels with the report. </summary>
        const string PostReportDescription = "Pick what's wrong. The post's text is sent along so it can be reviewed.";

        /// <summary> Prompt inside the empty report detail field. </summary>
        const string ReportDetailPlaceholder = "Anything else we should know? (optional)";

        /// <summary> Screen-reader name of the report detail field, which carries no visible label. </summary>
        const string ReportDetailFieldLabel = "Extra detail about this report";

        /// <summary> Label on the button that files the report. </summary>
        const string SendReportLabel = "Send report";

        /// <summary> Profile of the account this page is about, or null when that account has never published one. </summary>
        ProfileData? ShownProfile;

        /// <summary> That account's wall, newest first: what they wrote and what they passed on. </summary>
        IReadOnlyList<FeedEntry> WallEntries = [];

        /// <summary> The counts drawn under each shown post, keyed by post id. </summary>
        Dictionary<string, PostEngagement> Engagements = [];

        /// <summary> Profiles of the accounts named on this page beyond its owner, keyed by address. </summary>
        Dictionary<string, ProfileData?> NamedProfiles = [];

        /// <summary> How many accounts follow the shown account. </summary>
        int FollowerCount;

        /// <summary> How many accounts the shown account follows. </summary>
        int FollowingCount;

        /// <summary> True when the reader follows the shown account. Always false on the reader's own page. </summary>
        bool IsFollowingShownAccount;

        /// <summary> True when the reader has blocked the shown account. </summary>
        bool IsShownAccountBlocked;

        /// <summary> True while a follow or unfollow is being written, which turns the follow button into a throbber. </summary>
        bool IsFollowBusy;

        /// <summary> True while a block or unblock is being written. </summary>
        bool IsModerationBusy;

        /// <summary> Address the data currently held belongs to, so navigating to another profile is noticed and reloaded. </summary>
        string? LoadedAddress;

        /// <summary> True once a load has finished for <see cref="LoadedAddress"/>, which is what lets a profile-less account show a header instead of a spinner forever. </summary>
        bool HasCompletedFirstLoad;

        /// <summary> True while the whole address is revealed under the shortened pill. </summary>
        bool IsAddressExpanded;

        /// <summary> One line about whatever the reader just did, or null when there is nothing to say. </summary>
        string? ActionNotice;

        /// <summary> True while the owner has the editor open instead of the header. </summary>
        bool IsEditing;

        /// <summary> True while the edited profile is being stored. </summary>
        bool IsSavingProfile;

        /// <summary> Avatar the owner has selected in the editor. </summary>
        string DraftAvatar = ProfileData.DefaultAvatar;

        /// <summary> Name the owner has typed in the editor. </summary>
        string DraftDisplayName = string.Empty;

        /// <summary> Bio the owner has typed in the editor. </summary>
        string DraftBio = string.Empty;

        /// <summary> What is wrong with the edited profile, or null while it is fine. </summary>
        string? EditErrorMessage;

        /// <summary> True while the block confirmation is on screen. </summary>
        bool IsBlockConfirmVisible;

        /// <summary> True while the report form is on screen. </summary>
        bool IsReportVisible;

        /// <summary> The post being reported, or null when the report is about the account itself. </summary>
        PostData? ReportTargetPost;

        /// <summary> Reason currently selected in the report form. </summary>
        ReportReason SelectedReportReason = DefaultReportReason;

        /// <summary> What the reader typed into the report form. </summary>
        string ReportDetail = string.Empty;

        /// <summary> True while the report is being filed. </summary>
        bool IsReportBusy;

        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.WallChanged,
            MainEvents.Names.FollowChanged,
            MainEvents.Names.ProfileChanged,
            MainEvents.Names.ModerationChanged,
            MainEvents.Names.SessionChanged
        ];

        /// <summary> Address of the account this page is about: the one in the route, or the reader's own when the route carries none. </summary>
        string TargetAddress => string.IsNullOrWhiteSpace(Address) ? SessionService.CurrentAddress : Address;

        /// <summary> True when the page is about the reader themselves, which is what swaps the visitor actions for the editor. </summary>
        bool IsOwnProfile => string.Equals(TargetAddress, SessionService.CurrentAddress, StringComparison.Ordinal);

        /// <summary> True while the page has nothing to draw yet and the spinner owns the screen. </summary>
        bool ShowInitialSpinner => IsLoading && !HasCompletedFirstLoad;

        /// <summary> True when the very first load failed, so the failure card replaces the page rather than sitting above stale content. </summary>
        bool ShowLoadFailure => LoadFailureMessage is not null && !HasCompletedFirstLoad;

        /// <summary> Name drawn for this account: the one its owner chose, or the readable head of its address. </summary>
        string ShownName
        {
            get
            {
                string? chosen = ShownProfile?.DisplayName;
                return string.IsNullOrWhiteSpace(chosen) ? ProfileService.FallbackDisplayName(TargetAddress) : chosen;
            }
        }

        /// <summary> Emoji drawn for this account: the one its owner picked, or the one its address maps to. </summary>
        string ShownAvatar
        {
            get
            {
                string? chosen = ShownProfile?.Avatar;
                return string.IsNullOrWhiteSpace(chosen) ? ProfileService.PickAvatar(TargetAddress) : chosen;
            }
        }

        /// <summary> The account's own words about itself, empty when it wrote none. </summary>
        string ShownBio => ShownProfile?.Bio ?? string.Empty;

        /// <summary> True when this account wrote a bio worth a paragraph of its own. </summary>
        bool HasBio => !string.IsNullOrWhiteSpace(ShownBio);

        /// <summary> When this account first published a profile, or a stand-in line when it never did. </summary>
        string JoinedLabel => ShownProfile is null
            ? UnknownJoinLabel
            : $"{JoinedLabelPrefix} {RelativeTimeFormatter.FormatDate(ShownProfile.CreatedAtUnixMs)}";

        /// <summary> Heading over the post list, phrased for whoever is reading it. </summary>
        string PostsSectionTitle => IsOwnProfile ? OwnPostsSectionTitle : OtherPostsSectionTitle;

        /// <summary> Supporting line of the empty state on somebody else's page, naming the account so it does not read as generic. </summary>
        string OtherEmptyDescription => $"{ShownName} hasn't published anything yet.";

        /// <summary> The emoji on the address toggle, which flips with what the toggle would do next. </summary>
        string AddressToggleEmoji => IsAddressExpanded ? HideAddressEmoji : RevealAddressEmoji;

        /// <summary> Heading of the block confirmation, naming who is about to be blocked. </summary>
        string BlockModalTitle => $"{BlockModalTitlePrefix} {ShownName}?";

        /// <summary> Heading of the report form, which differs for a post and for the account itself. </summary>
        string ReportModalTitle => ReportTargetPost is null ? AccountReportTitle : PostReportTitle;

        /// <summary> Body of the report form, which says what the report will and will not carry. </summary>
        string ReportModalDescription => ReportTargetPost is null ? AccountReportDescription : PostReportDescription;

        /// <summary> Characters still available in the display name field. </summary>
        int RemainingNameCharacters => ProfileData.MaximumDisplayNameLength - DraftDisplayName.Length;

        /// <summary> Characters still available in the bio field. </summary>
        int RemainingBioCharacters => ProfileData.MaximumBioLength - DraftBio.Length;

        /// <summary> Where messaging the shown account leads. Built from the navigation constant so the tab and this button never point at different pages. </summary>
        string MessagesRoute => $"{NavigationConstants.Messages.Link}/{TargetAddress}";

        /// <summary> Corner radius shared by the page's cards, as a CSS length. </summary>
        static string CardRadiusCss => $"{AppMeasures.Radius.XLarge}px";

        /// <summary> Inner padding shared by the page's cards, as a CSS length. </summary>
        static string CardPaddingCss => $"{AppMeasures.Space.Px24}px";

        /// <summary> Fully rounded corner, as a CSS length, for every pill-shaped control on the page. </summary>
        static string PillRadiusCss => $"{AppMeasures.Radius.Pill}px";

        /// <summary> Inner padding of an action button, as a CSS shorthand. </summary>
        static string ActionPaddingCss => $"{AppMeasures.Space.Px10}px {AppMeasures.Space.Px20}px";

        /// <summary> Hairline around a quiet, glass-filled action button. </summary>
        static string QuietButtonBorder => $"{AppMeasures.Border.Thin}px solid {AppColors.GlassBorderDefault.ToRgbaHex(true)}";

        /// <summary> Hairline around the block button, tinted so it reads as the one destructive action in the row. </summary>
        static string DangerButtonBorder => $"{AppMeasures.Border.Thin}px solid {AppColors.Error.ToRgbaHex(true)}";

        /// <summary> Hairline around the notice chip. </summary>
        static string NoticeChipBorder => $"{AppMeasures.Border.Thin}px solid {AppColors.Primary.ToRgbaHex(true)}";

        /// <summary> Hairline around the "you blocked this account" badge. </summary>
        static string BlockedChipBorder => $"{AppMeasures.Border.Thin}px solid {AppColors.Error.ToRgbaHex(true)}";

        /// <summary> The indigo-to-coral fill every inviting button on the page carries. </summary>
        static (Color Start, Color End)? PrimaryGradient => (AppColors.Primary, AppColors.Secondary);

        /// <summary>
        /// Reads everything the page draws: the account's profile, its follower and following totals, the reader's
        /// relationship to it, its posts, and the like and comment totals those posts carry.
        /// </summary>
        protected override async Task LoadAsync()
        {
            string address = TargetAddress;

            if (!string.Equals(LoadedAddress, address, StringComparison.Ordinal))
            {
                LoadedAddress = address;
                HasCompletedFirstLoad = false;
                IsEditing = false;
                IsAddressExpanded = false;
                ActionNotice = null;
                EditErrorMessage = null;
            }

            ShownProfile = await ProfileService.ReadAsync(address)
                           ?? (IsOwnProfile ? SessionService.CurrentProfile : null);

            FollowerCount = await FollowService.CountFollowersAsync(address);
            FollowingCount = await FollowService.CountFollowingAsync(address);

            if (IsOwnProfile)
            {
                IsFollowingShownAccount = false;
                IsShownAccountBlocked = false;
            }
            else
            {
                string viewerAddress = SessionService.CurrentAddress;
                IsFollowingShownAccount = await FollowService.IsFollowingAsync(viewerAddress, address);
                IsShownAccountBlocked = await ModerationService.IsBlockedAsync(viewerAddress, address);
            }

            WallEntries = await FeedService.ReadAccountWallAsync(address);
            await LoadPostEngagementAsync();

            HasCompletedFirstLoad = true;
        }

        /// <summary>
        /// Reloads when the route swaps one account for another. <see cref="LoadablePage.OnInitializedAsync"/> has
        /// already loaded the first account by the time this first runs, so comparing against
        /// <see cref="LoadedAddress"/> is what stops the page loading the same account twice.
        /// </summary>
        /// <returns> A task that completes once the new account's data is on screen, or immediately when nothing changed. </returns>
        protected override async Task OnParametersSetAsync()
        {
            if (!SessionService.IsSignedIn) return;
            if (string.Equals(LoadedAddress, TargetAddress, StringComparison.Ordinal)) return;

            await ReloadAsync();
        }

        /// <summary>
        /// Reads the counts under every post on this wall, and the profiles of the authors this account passed on —
        /// a passed-on post is drawn under its own author's name, who is somebody other than the page's owner.
        /// </summary>
        /// <returns> A task that completes once every shown post has its totals. </returns>
        async Task LoadPostEngagementAsync()
        {
            PostData[] posts = [.. WallEntries.Select(entry => entry.Post).DistinctBy(post => post.PostId)];

            Task<Dictionary<string, PostEngagement>> engagementsRead =
                FeedService.ReadEngagementsAsync(posts, SessionService.CurrentAddress);

            string[] otherAuthors = [.. posts.Select(post => post.AuthorAddress).Where(author => author != TargetAddress).Distinct()];
            Task<ProfileData?[]> profilesRead = Task.WhenAll(otherAuthors.Select(ProfileService.ReadAsync));

            await Task.WhenAll(engagementsRead, profilesRead);

            Dictionary<string, ProfileData?> byAddress = new(otherAuthors.Length);
            ProfileData?[] profiles = await profilesRead;
            for (int index = 0; index < otherAuthors.Length; index++)
            {
                byAddress[otherAuthors[index]] = profiles[index];
            }

            Engagements = await engagementsRead;
            NamedProfiles = byAddress;
        }

        /// <summary>
        /// How many of the shown lines this account actually wrote. The counter over a profile means what somebody
        /// published, so a post passed on from elsewhere does not add to it.
        /// </summary>
        int WrittenPostCount => WallEntries.Count(entry => !entry.IsRepost);

        /// <summary> The counts for one shown post, all zero while they have not been read. </summary>
        /// <param name="post"> Post being drawn. </param>
        /// <returns> That post's totals. </returns>
        PostEngagement EngagementFor(PostData post) => Engagements.GetValueOrDefault(post.PostId);

        /// <summary>
        /// Profile of a shown post's author: the page's own profile for what this account wrote, and a separately
        /// read one for a post it passed on.
        /// </summary>
        /// <param name="post"> Post being drawn. </param>
        /// <returns> The author's profile, or null when it could not be read. </returns>
        ProfileData? AuthorProfileFor(PostData post)
            => post.AuthorAddress == TargetAddress ? ShownProfile : NamedProfiles.GetValueOrDefault(post.AuthorAddress);

        /// <summary>
        /// Name shown above a post this account passed on. Only a post by somebody else says so: an account
        /// passing its own post on would just be repeating itself back at its own readers.
        /// </summary>
        /// <param name="entry"> The wall line being drawn. </param>
        /// <returns> The owner's name, or null when the line needs no such header. </returns>
        string? RepostedByNameFor(FeedEntry entry)
            => entry.IsRepost && entry.Post.AuthorAddress != TargetAddress ? ShownName : null;

        /// <summary> Turns the reader's like on one of these posts on, or off when it was already on. </summary>
        /// <param name="post"> Post whose heart was tapped. </param>
        /// <returns> A task that completes once the like has been written; the wall event then reloads the page. </returns>
        Task ToggleLikeAsync(PostData post) => WallService.ToggleLikeAsync(post, Account.Public);

        /// <summary> Carries a post onto the reader's own wall, or takes it back when it is already there. </summary>
        /// <param name="post"> Post whose arrows were tapped. </param>
        /// <returns> A task that completes once the repost has been written; the wall event then reloads the page. </returns>
        Task ToggleRepostAsync(PostData post) => WallService.ToggleRepostAsync(post, Account);

        /// <summary> Removes one of the reader's own posts. </summary>
        /// <param name="post"> Post to remove. </param>
        /// <returns> A task that completes once the post is gone; the wall event then reloads the page. </returns>
        Task DeletePostAsync(PostData post) => WallService.DeleteAsync(post, Account.Public);

        /// <summary> Shows or hides the whole address under the shortened pill, so it can be read out or copied. </summary>
        void ToggleAddressDetail() => IsAddressExpanded = !IsAddressExpanded;

        /// <summary> Opens the wall, where a post is written. </summary>
        void OpenWall() => NavManager.NavigateTo(NavigationConstants.Wall.Link);

        /// <summary> Opens the private conversation with the shown account. </summary>
        void OpenMessages() => NavManager.NavigateTo(MessagesRoute);

        /// <summary> Opens the owner's settings. </summary>
        void OpenSettings() => NavManager.NavigateTo(SettingsRoute);

        /// <summary> Opens another account's profile, from a post this one passed on. </summary>
        /// <param name="address"> Address of the account whose profile to open. </param>
        void OpenAuthor(string address) => NavManager.NavigateTo($"{NavigationConstants.Profile.Link}/{address}");

        /// <summary> Follows the shown account, or lets it go when the reader already followed it. </summary>
        /// <returns> A task that completes once the change has been written; the follow event then reloads the counts. </returns>
        async Task ToggleFollowAsync()
        {
            if (IsOwnProfile || IsFollowBusy) return;

            IsFollowBusy = true;
            ActionNotice = null;

            try
            {
                if (IsFollowingShownAccount) await FollowService.UnfollowAsync(Account, TargetAddress);
                else await FollowService.FollowAsync(Account, TargetAddress);
            }
            catch (Exception error)
            {
                Log($"Toggling the follow on '{TargetAddress}' failed.\n{error}", LogLevel.Error);
            }
            finally
            {
                IsFollowBusy = false;
            }
        }

        /// <summary> Fills the editor with what the profile currently says and puts it on screen. </summary>
        void BeginEditing()
        {
            if (!IsOwnProfile) return;

            DraftAvatar = ShownAvatar;
            DraftDisplayName = ShownName;
            DraftBio = ShownBio;
            EditErrorMessage = null;
            ActionNotice = null;
            IsEditing = true;
        }

        /// <summary> Closes the editor and throws the draft away. </summary>
        void CancelEditing()
        {
            IsEditing = false;
            EditErrorMessage = null;
        }

        /// <summary> Selects one emoji from the picker as the draft avatar. </summary>
        /// <param name="emoji"> The emoji that was tapped. </param>
        void ChooseAvatar(string emoji) => DraftAvatar = emoji;

        /// <summary> Keeps the draft name in step with the field on every keystroke. </summary>
        /// <param name="args"> The input event; its value is the field's new contents. </param>
        void HandleDisplayNameInput(ChangeEventArgs args) => DraftDisplayName = args.Value?.ToString() ?? string.Empty;

        /// <summary> Keeps the draft bio in step with the field on every keystroke. </summary>
        /// <param name="args"> The input event; its value is the field's new contents. </param>
        void HandleBioInput(ChangeEventArgs args) => DraftBio = args.Value?.ToString() ?? string.Empty;

        /// <summary>
        /// Stores the edited profile and hands the new copy to the session, so the rest of the app draws the new name
        /// and avatar without re-reading them.
        /// </summary>
        /// <returns> A task that completes once the profile has been stored, or immediately when the draft was refused. </returns>
        async Task SaveProfileAsync()
        {
            if (!IsOwnProfile || IsSavingProfile) return;

            ProfileData? current = ShownProfile ?? SessionService.CurrentProfile;
            if (current is null) return;

            string name = DraftDisplayName.Trim();
            string bio = DraftBio.Trim();

            if (name.Length == 0)
            {
                EditErrorMessage = EmptyNameMessage;
                return;
            }

            if (name.Length > ProfileData.MaximumDisplayNameLength)
            {
                EditErrorMessage = LongNameMessage;
                return;
            }

            if (bio.Length > ProfileData.MaximumBioLength)
            {
                EditErrorMessage = LongBioMessage;
                return;
            }

            IsSavingProfile = true;
            EditErrorMessage = null;

            try
            {
                ProfileData updated = current with
                {
                    DisplayName = name,
                    Avatar = string.IsNullOrWhiteSpace(DraftAvatar) ? ProfileData.DefaultAvatar : DraftAvatar,
                    Bio = bio
                };

                await ProfileService.SaveAsync(updated);
                SessionService.UpdateCurrentProfile(updated);

                ShownProfile = updated;
                IsEditing = false;
                ActionNotice = ProfileSavedNotice;
            }
            catch (Exception error)
            {
                EditErrorMessage = SaveFailedMessage;
                Log($"Saving the profile for '{TargetAddress}' failed.\n{error}", LogLevel.Error);
            }
            finally
            {
                IsSavingProfile = false;
            }
        }

        /// <summary> Puts the block confirmation on screen. Blocking is one tap away from irreversible-feeling, so it is asked about first. </summary>
        void OpenBlockConfirm()
        {
            ActionNotice = null;
            IsBlockConfirmVisible = true;
        }

        /// <summary> Closes the block confirmation without blocking anyone. </summary>
        void CloseBlockConfirm() => IsBlockConfirmVisible = false;

        /// <summary> Places the block the reader just confirmed. </summary>
        /// <returns> A task that completes once the block has been written; the moderation event then reloads the page. </returns>
        async Task ConfirmBlockAsync()
        {
            if (IsModerationBusy) return;

            IsModerationBusy = true;

            try
            {
                bool blocked = await ModerationService.BlockAsync(Account, TargetAddress);

                IsBlockConfirmVisible = false;
                ActionNotice = blocked ? BlockedNotice : BlockRefusedNotice;
            }
            catch (Exception error)
            {
                IsBlockConfirmVisible = false;
                ActionNotice = BlockRefusedNotice;
                Log($"Blocking '{TargetAddress}' failed.\n{error}", LogLevel.Error);
            }
            finally
            {
                IsModerationBusy = false;
            }
        }

        /// <summary> Lifts the block the reader had placed on the shown account. </summary>
        /// <returns> A task that completes once the block has been removed; the moderation event then reloads the page. </returns>
        async Task UnblockAsync()
        {
            if (IsModerationBusy) return;

            IsModerationBusy = true;

            try
            {
                await ModerationService.UnblockAsync(Account, TargetAddress);
                ActionNotice = UnblockedNotice;
            }
            catch (Exception error)
            {
                Log($"Unblocking '{TargetAddress}' failed.\n{error}", LogLevel.Error);
            }
            finally
            {
                IsModerationBusy = false;
            }
        }

        /// <summary> Opens the report form for the shown account, where nothing the account wrote is disclosed. </summary>
        void OpenAccountReport()
        {
            ReportTargetPost = null;
            OpenReportForm();
        }

        /// <summary> Opens the report form for one post, whose text travels with the report. </summary>
        /// <param name="post"> Post being complained about. </param>
        void OpenPostReport(PostData post)
        {
            ReportTargetPost = post;
            OpenReportForm();
        }

        /// <summary> Resets the report form to its starting state and puts it on screen. </summary>
        void OpenReportForm()
        {
            SelectedReportReason = DefaultReportReason;
            ReportDetail = string.Empty;
            ActionNotice = null;
            IsReportVisible = true;
        }

        /// <summary> Closes the report form without filing anything. </summary>
        void CloseReport() => IsReportVisible = false;

        /// <summary> Selects one reason in the report form. </summary>
        /// <param name="reason"> The reason that was tapped. </param>
        void SelectReportReason(ReportReason reason) => SelectedReportReason = reason;

        /// <summary> Keeps the report detail in step with its field on every keystroke. </summary>
        /// <param name="args"> The input event; its value is the field's new contents. </param>
        void HandleReportDetailInput(ChangeEventArgs args) => ReportDetail = args.Value?.ToString() ?? string.Empty;

        /// <summary> Files the report the reader filled in, about either one post or the account itself. </summary>
        /// <returns> A task that completes once the report has been stored or refused. </returns>
        async Task SubmitReportAsync()
        {
            if (IsReportBusy) return;

            IsReportBusy = true;

            try
            {
                ReportData? filed = ReportTargetPost is null
                    ? await ModerationService.ReportAccountAsync(Account, TargetAddress, SelectedReportReason, ReportDetail)
                    : await ModerationService.ReportPostAsync(Account, ReportTargetPost, SelectedReportReason, ReportDetail);

                IsReportVisible = false;
                ActionNotice = filed is null ? ReportRefusedNotice : ReportFiledNotice;
            }
            catch (Exception error)
            {
                IsReportVisible = false;
                ActionNotice = ReportRefusedNotice;
                Log($"Filing a report about '{TargetAddress}' failed.\n{error}", LogLevel.Error);
            }
            finally
            {
                IsReportBusy = false;
            }
        }

        /// <summary> Names one report reason in words a reader recognises. </summary>
        /// <param name="reason"> The reason to name. </param>
        /// <returns> The label drawn on that reason's chip. </returns>
        static string DescribeReason(ReportReason reason) => reason switch
        {
            ReportReason.Spam => "Spam",
            ReportReason.Harassment => "Harassment",
            ReportReason.Violence => "Violence",
            ReportReason.SexualContent => "Sexual content",
            ReportReason.Impersonation => "Impersonation",
            _ => "Something else"
        };
    }
}
