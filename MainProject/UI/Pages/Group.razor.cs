using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;
using Microsoft.AspNetCore.Components;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// One group's own page. A member sees the room's posts and the composer; anybody else sees only what the group
    /// says about itself, because a group whose words anybody can read is a wall with extra steps.
    /// </summary>
    public partial class Group
    {
        /// <summary> Start of every group address. </summary>
        public const string GroupRoutePrefix = "/group";

        /// <summary>
        /// Route this page answers on. The parameter segment is spelled from <see cref="Address"/> itself, so
        /// renaming the property moves the route with it instead of silently breaking every link.
        /// </summary>
        public const string GroupRoute = GroupRoutePrefix + "/{" + nameof(Address) + "}";

        /// <summary> Builds the link to one group's page. </summary>
        /// <param name="address"> The group's address. </param>
        /// <returns> The route that opens it. </returns>
        public static string LinkTo(string address) => $"{GroupRoutePrefix}/{address}";

        /// <summary> The group's address, taken from the route. Blazor assigns null to an unmatched parameter, so it is normalised here. </summary>
        [Parameter]
        public string? Address
        {
            get => _address;
            set => _address = value ?? string.Empty;
        }

        string _address = string.Empty;

        /// <summary> Invitation in this page's composer, so it asks about the room rather than about the wall. </summary>
        const string GroupComposerPlaceholder = "Say something in here…";

        /// <summary> Line under the throbber and beside a missing group. </summary>
        const string MissingEmoji = "🫧";

        /// <summary> Headline shown when the address resolves to nothing. </summary>
        const string MissingHeadline = "This room isn't here";

        /// <summary> Supporting line of that placeholder. </summary>
        const string MissingDescription = "It may have been taken down, or the link lost a character on its way here.";

        /// <summary> Label on the button that leaves a missing group. </summary>
        const string BackToGroupsLabel = "Back to groups";

        /// <summary> Emoji for the placeholder shown to somebody who is not in the group. </summary>
        const string OutsideEmoji = "🚪";

        /// <summary> Headline of that placeholder. </summary>
        const string OutsideHeadline = "You're not in this room";

        /// <summary> Supporting line of it, saying plainly why there is nothing to read. </summary>
        const string OutsideDescription = "What is said in a group stays inside it. Join and it opens up.";

        /// <summary> Emoji for the placeholder shown in a group nobody has written in. </summary>
        const string QuietEmoji = "🌱";

        /// <summary> Headline of that placeholder. </summary>
        const string QuietHeadline = "Nothing said yet";

        /// <summary> Supporting line of it. </summary>
        const string QuietDescription = "Say the first thing and the room starts.";

        /// <summary> Emoji for the placeholder shown when the page could not be read. </summary>
        const string LoadFailedEmoji = "🌧️";

        /// <summary> Headline of that placeholder. </summary>
        const string LoadFailedHeadline = "This didn't come through";

        /// <summary> Label on the button that runs a failed load again. </summary>
        const string TryAgainLabel = "Try again";

        /// <summary> Marks the badge shown when the group's founding signature does not verify. </summary>
        const string UnverifiedEmoji = "⚠️";

        /// <summary> Text on that badge. </summary>
        const string UnverifiedLabel = "unverified group";

        /// <summary> Label on the button that joins an open group. </summary>
        const string JoinLabel = "Join this group";

        /// <summary> Label on the button that leaves one. </summary>
        const string LeaveLabel = "Leave";

        /// <summary> Shown in place of that button for the founder, who cannot leave their own group. </summary>
        const string FounderRoleLabel = "You founded this";

        /// <summary> Shown for a group nobody may let themselves into. </summary>
        const string ClosedLabel = "By invitation only";

        /// <summary> Member count for a group of exactly one. </summary>
        const string SingleMemberLabel = "1 member";

        /// <summary> Member count for any other number; the placeholder takes the count. </summary>
        const string ManyMembersFormat = "{0} members";

        /// <summary> Route an account's profile lives at; the address is appended to it. </summary>
        const string ProfileRoutePrefix = "/profile/";

        /// <summary> Route a post's own page lives at; the post id is appended to it. </summary>
        const string PostRoutePrefix = "/post/";

        /// <summary> Diameter of the small throbber in the header while a reload runs. </summary>
        const int RefreshSpinnerDiameterPx = AppMeasures.Size.Px20;

        /// <summary> Ring thickness of that throbber. </summary>
        const int RefreshSpinnerBorderPx = AppMeasures.Border.Medium;

        /// <summary> Fully rounded corners on this page's buttons, as a CSS length. </summary>
        static readonly string PillRadiusCss = $"{AppMeasures.Radius.Pill}px";

        /// <summary> Inside spacing of those buttons. </summary>
        static readonly string ActionPaddingCss = $"{AppMeasures.Space.Px10}px {AppMeasures.Space.Px20}px";

        /// <summary> Corner radius of the card describing the group. </summary>
        static readonly string CardRadiusCss = $"{AppMeasures.Radius.XLarge}px";

        /// <summary> Inside spacing of that card. </summary>
        static readonly string CardPaddingCss = $"{AppMeasures.Space.Px20}px";

        /// <summary> Hairline around the quiet leave button. </summary>
        static string QuietButtonBorder => $"{AppMeasures.Border.Thin}px solid {AppColors.BorderSoft.ToRgbaHex(true)}";

        /// <summary> Hairline outlining the unverified badge in the warning colour. </summary>
        static string UnverifiedChipBorder => $"{AppMeasures.Border.Thin}px solid {AppColors.Warning.ToRgbaHex(true)}";

        /// <summary> The group this page is about, or null when nothing is stored under the address. </summary>
        GroupData? group;

        /// <summary> Result of the last founding-signature check, kept between renders. </summary>
        bool IsFounderVerified;

        /// <summary> True when the reader is in this group. </summary>
        bool IsMember;

        /// <summary> True when the reader founded it, which is the one membership that cannot be given up. </summary>
        bool IsFounder => group is not null && group.FounderAddress == SessionService.CurrentAddress;

        /// <summary> How many people are in it. </summary>
        int memberCount;

        /// <summary> The group's posts, newest first. </summary>
        IReadOnlyList<PostData> posts = [];

        /// <summary> Profiles of the accounts that wrote them, keyed by address. </summary>
        Dictionary<string, ProfileData?> authorProfiles = [];

        /// <summary> The counts drawn under each post, keyed by post id. </summary>
        Dictionary<string, PostEngagement> engagements = [];

        /// <summary> What the reader has typed into the composer and not published yet. </summary>
        string draftText = string.Empty;

        /// <summary> Media already uploaded for that post but not published yet. </summary>
        IReadOnlyList<MediaAttachment> draftAttachments = [];

        /// <summary> True while a post is being signed and stored, which locks the composer. </summary>
        bool isPublishing;

        /// <summary> True while a join or a leave is being written. </summary>
        bool isChangingMembership;

        /// <summary> True once a load has finished, so later reloads refresh in place instead of blanking the page. </summary>
        bool hasLoadedOnce;

        /// <summary> Address the last load ran for; a different one in the route means another group was opened. </summary>
        string loadedAddress = string.Empty;

        /// <summary> Reloads when this group changes, when a post inside it changes, and when the session changes. </summary>
        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.GroupsChanged,
            MainEvents.Names.WallChanged,
            MainEvents.Names.SessionChanged
        ];

        /// <summary> True while the very first load runs and there is nothing on screen to keep. </summary>
        bool IsFirstLoad => IsLoading && !hasLoadedOnce;

        /// <summary> True while a reload refreshes a page that is already drawn; only the header throbber reacts. </summary>
        bool IsRefreshing => IsLoading && hasLoadedOnce;

        /// <summary> Name in the header, which falls back to the page's own word before the group has been read. </summary>
        string HeadingName => group?.Name ?? MissingHeadline;

        /// <summary> Browser tab title. </summary>
        string PageTitleText => group?.Name ?? MissingHeadline;

        /// <summary> The member count as it is read. </summary>
        string MemberLabel => memberCount == 1 ? SingleMemberLabel : string.Format(ManyMembersFormat, memberCount);

        /// <summary> Emoji in the composer's bubble: the reader's own avatar. </summary>
        string ComposerAvatar => SessionService.CurrentProfile?.Avatar ?? ProfileData.DefaultAvatar;

        /// <summary> Frosted bar the sticky header is painted on. </summary>
        static string HeaderSurfaceStyle => AppStyles.BuildBarSurface(pinnedToBottom: false);

        /// <summary>
        /// Reloads when the route swaps one group for another without leaving the page. The first pass is already
        /// covered by the page base, which is why an address matching the one just loaded is left alone.
        /// </summary>
        /// <returns> A task that completes once the new group has been read, or immediately when nothing changed. </returns>
        protected override async Task OnParametersSetAsync()
        {
            await base.OnParametersSetAsync();

            if (string.Equals(loadedAddress, _address, StringComparison.Ordinal)) return;

            await ReloadAsync();
        }

        /// <summary> Reads the group, its membership, and — for a member — its posts and everything their cards need. </summary>
        /// <returns> A task that completes once the page has all of it. </returns>
        protected override async Task LoadAsync()
        {
            loadedAddress = _address;
            group = await GroupService.ReadAsync(_address);

            if (group is null)
            {
                IsMember = false;
                posts = [];
                memberCount = 0;
                hasLoadedOnce = true;
                return;
            }

            string viewerAddress = SessionService.CurrentAddress;

            IReadOnlyList<string> members = await GroupService.ReadMembersAsync(group.Address);
            memberCount = members.Count;
            IsMember = members.Contains(viewerAddress);

            IsFounderVerified = GroupService.VerifyFounder(group, await ProfileService.ReadAsync(group.FounderAddress));

            // Only a member reads the room's words. Somebody outside gets the card describing the group and
            // nothing else, which is what makes a group a room rather than a differently-shaped wall.
            if (!IsMember)
            {
                posts = [];
                hasLoadedOnce = true;
                return;
            }

            IReadOnlyList<PostData> inside = await WallService.ReadGroupPostsAsync(group.Address);

            string[] authors = [.. inside.Select(post => post.AuthorAddress).Distinct()];
            Task<ProfileData?[]> profilesRead = Task.WhenAll(authors.Select(ProfileService.ReadAsync));
            Task<Dictionary<string, PostEngagement>> engagementsRead = FeedService.ReadEngagementsAsync(inside, viewerAddress);

            await Task.WhenAll(profilesRead, engagementsRead);

            Dictionary<string, ProfileData?> byAddress = new(authors.Length);
            ProfileData?[] profiles = await profilesRead;
            for (int index = 0; index < authors.Length; index++)
            {
                byAddress[authors[index]] = profiles[index];
            }

            posts = inside;
            authorProfiles = byAddress;
            engagements = await engagementsRead;
            hasLoadedOnce = true;
        }

        /// <summary> The profile behind a post's author. </summary>
        /// <param name="post"> Post being drawn. </param>
        /// <returns> The author's profile, or null when it could not be read. </returns>
        ProfileData? AuthorProfileFor(PostData post) => authorProfiles.GetValueOrDefault(post.AuthorAddress);

        /// <summary> The counts for one post, all zero while they have not been read. </summary>
        /// <param name="post"> Post being drawn. </param>
        /// <returns> That post's totals. </returns>
        PostEngagement EngagementFor(PostData post) => engagements.GetValueOrDefault(post.PostId);

        /// <summary> Keeps the composer's text on the page, so what is half-written survives every redraw. </summary>
        /// <param name="text"> The field's new contents. </param>
        void HandleDraftChanged(string text) => draftText = text;

        /// <summary> Keeps the composer's attached media on the page alongside its text. </summary>
        /// <param name="attachments"> The media currently attached. </param>
        void HandleDraftAttachmentsChanged(IReadOnlyList<MediaAttachment> attachments) => draftAttachments = attachments;

        /// <summary>
        /// Signs and publishes what the reader wrote, inside this group. The composer is only cleared on a post
        /// that was actually stored, so text the service refused stays on screen to be fixed.
        /// </summary>
        /// <returns> A task that completes once the post is stored. </returns>
        async Task PublishAsync()
        {
            if (group is null || isPublishing) return;

            isPublishing = true;

            try
            {
                PostData? published = await WallService.PublishAsync(
                    Account, draftText, draftAttachments, groupAddress: group.Address);

                if (published is null) return;

                draftText = string.Empty;
                draftAttachments = [];
            }
            catch (Exception error)
            {
                Log($"{nameof(Group)} could not publish inside '{group.Address}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                isPublishing = false;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary> Joins this group. </summary>
        /// <returns> A task that completes once the membership has been written. </returns>
        async Task JoinAsync()
        {
            if (group is null || isChangingMembership) return;

            isChangingMembership = true;

            try
            {
                await GroupService.JoinAsync(group, Account.Public);
            }
            catch (Exception error)
            {
                Log($"{nameof(Group)} could not join '{group.Address}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                isChangingMembership = false;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary> Leaves this group; the founder's own attempt is refused by the service. </summary>
        /// <returns> A task that completes once the membership is gone. </returns>
        async Task LeaveAsync()
        {
            if (group is null || isChangingMembership) return;

            isChangingMembership = true;

            try
            {
                await GroupService.LeaveAsync(group, Account.Public);
            }
            catch (Exception error)
            {
                Log($"{nameof(Group)} could not leave '{group.Address}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                isChangingMembership = false;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary> Turns the reader's chay on one of these posts on, or off when it was already on. </summary>
        /// <param name="post"> Post whose glass was tapped. </param>
        /// <returns> A task that completes once it has been written. </returns>
        Task ToggleLikeAsync(PostData post) => WallService.ToggleLikeAsync(post, Account.Public);

        /// <summary> Removes one of the reader's own posts. </summary>
        /// <param name="post"> Post to remove. </param>
        /// <returns> A task that completes once the post is gone. </returns>
        Task DeletePostAsync(PostData post) => WallService.DeleteAsync(post, Account.Public);

        /// <summary> Opens an author's profile. </summary>
        /// <param name="address"> Address of the account whose profile to open. </param>
        void OpenAuthor(string address) => NavManager.NavigateTo($"{ProfileRoutePrefix}{address}");

        /// <summary> Opens one post's own page, where its comments are read and written. </summary>
        /// <param name="postId"> Id of the post to open. </param>
        void OpenComments(string postId) => NavManager.NavigateTo($"{PostRoutePrefix}{postId}");

        /// <summary> Leaves this group's page for the listing. </summary>
        void GoBackToGroups() => NavManager.NavigateTo(Groups.GroupsRoute);
    }
}
