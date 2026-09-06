using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;
using Microsoft.AspNetCore.Components;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// The groups screen: the ones the reader is in, the ones they could join, and the sheet that founds a new one.
    /// Membership is read alongside the listing so a row knows which of the two it belongs in without asking again
    /// once it is already drawn.
    /// </summary>
    public partial class Groups
    {
        /// <summary> Route this page answers on, taken from the navigation itself so the tab and the page never point at different addresses. </summary>
        public const string GroupsRoute = NavigationConstants.Groups.Link;

        /// <summary> Emoji beside the page heading. </summary>
        const string PageEmoji = "🫂";

        /// <summary> The page's heading. </summary>
        const string PageHeadline = "Groups";

        /// <summary> Line under it. </summary>
        const string PageSubtitle = "Rooms a few people share. What is said inside one stays inside it.";

        /// <summary> Heading over the groups the reader belongs to. </summary>
        const string MineSectionTitle = "Yours";

        /// <summary> Heading over the rest. </summary>
        const string DiscoverSectionTitle = "Open to join";

        /// <summary> Emoji on the button and sheet that found a group. </summary>
        const string FoundEmoji = "✨";

        /// <summary> Emoji on the button that leads to pages. </summary>
        const string PagesEmoji = "📰";

        /// <summary> Label on that button. </summary>
        const string PagesLabel = "Pages";

        /// <summary> Emoji on the button that leads to meeting a stranger. </summary>
        const string StrangersEmoji = "🎲";

        /// <summary> Label on that button. </summary>
        const string StrangersLabel = "Meet somebody";

        /// <summary> Emoji on the button that leads two steps out from the reader's own people. </summary>
        const string NearbyEmoji = "🧭";

        /// <summary> Label on that button. </summary>
        const string NearbyLabel = "Two steps out";

        /// <summary> Label on that button. </summary>
        const string FoundLabel = "Start a group";

        /// <summary> Heading of the founding sheet. </summary>
        const string FoundSheetTitle = "Start a group";

        /// <summary> Line under that heading. </summary>
        const string FoundSheetSubtitle = "Give it a name and a line about what it is for. You can be its only member for a while.";

        /// <summary> Grey prompt in the name field. </summary>
        const string NamePlaceholder = "What is it called?";

        /// <summary> Grey prompt in the description field. </summary>
        const string DescriptionPlaceholder = "What is it for? (optional)";

        /// <summary> Label on the button that actually founds it. </summary>
        const string FoundConfirmLabel = "Found it";

        /// <summary> Label on the button that closes the sheet without founding anything. </summary>
        const string CancelLabel = "Never mind";

        /// <summary> Message shown when founding was refused. </summary>
        const string FoundFailureMessage = "That didn't go through. Check the name and try again?";

        /// <summary> Class marking the emoji currently chosen for the new group. </summary>
        const string SelectedAvatarClass = "is-chosen";

        /// <summary> Line under the throbber while the first listing is read. </summary>
        const string LoadingLabel = "Looking for rooms…";

        /// <summary> Emoji on the placeholder shown when there is nothing left to join. </summary>
        const string EmptyEmoji = "🌾";

        /// <summary> Headline of that placeholder. </summary>
        const string EmptyHeadline = "Nothing else out here yet";

        /// <summary> Supporting line of that placeholder. </summary>
        const string EmptyDescription = "Start one and it will be the first thing anybody finds here.";

        /// <summary> Emoji on the placeholder shown when the listing could not be read. </summary>
        const string LoadFailedEmoji = "🌧️";

        /// <summary> Headline of that placeholder. </summary>
        const string LoadFailedHeadline = "This didn't come through";

        /// <summary> Label on the button that runs a failed load again. </summary>
        const string TryAgainLabel = "Try again";

        /// <summary> Emoji a new group may be given, offered rather than typed so the choice is one tap. </summary>
        static readonly string[] AvatarChoices = ["🫂", "🍵", "🌙", "🔥", "🌱", "📚", "🎧", "⚙️"];

        /// <summary> Fully rounded corners on this page's buttons, as a CSS length. </summary>
        static readonly string PillRadiusCss = $"{AppMeasures.Radius.Pill}px";

        /// <summary> Inside spacing of those buttons. </summary>
        static readonly string ActionPaddingCss = $"{AppMeasures.Space.Px10}px {AppMeasures.Space.Px20}px";

        /// <summary> Hairline around the quiet button in the sheet. </summary>
        static string QuietButtonBorder => $"{AppMeasures.Border.Thin}px solid {AppColors.BorderSoft.ToRgbaHex(true)}";

        /// <summary> Diameter of the throbber inside the founding button. </summary>
        const int BusySpinnerDiameterPx = AppMeasures.Size.Px20;

        /// <summary> Ring thickness of that throbber. </summary>
        const int BusySpinnerBorderPx = AppMeasures.Border.Medium;

        /// <summary> Groups the reader belongs to. </summary>
        IReadOnlyList<GroupData> mine = [];

        /// <summary> Groups the reader could join. </summary>
        IReadOnlyList<GroupData> others = [];

        /// <summary> How many members each listed group has, keyed by address. </summary>
        Dictionary<string, int> memberCounts = [];

        /// <summary> True once a load has finished, so later reloads refresh in place instead of blanking the page. </summary>
        bool hasLoadedOnce;

        /// <summary> True while the founding sheet is on screen. </summary>
        bool isFoundingOpen;

        /// <summary> True while a group is being founded, which locks the sheet open until it finishes. </summary>
        bool isFounding;

        /// <summary> Address of the group being joined right now, or empty while no join is running. </summary>
        string joiningAddress = string.Empty;

        /// <summary> Name typed into the founding sheet. </summary>
        string draftName = string.Empty;

        /// <summary> Description typed into the founding sheet. </summary>
        string draftDescription = string.Empty;

        /// <summary> Emoji chosen for the new group. </summary>
        string draftAvatar = GroupData.DefaultAvatar;

        /// <summary> Message shown when founding failed, or null when it did not. </summary>
        string? foundErrorMessage;

        /// <summary> Reloads when a group is founded or joined, and when the signed-in account changes. </summary>
        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.GroupsChanged,
            MainEvents.Names.SessionChanged
        ];

        /// <summary> True while the very first load runs and there is nothing on screen to keep. </summary>
        bool IsFirstLoad => IsLoading && !hasLoadedOnce;

        /// <summary> True when the sheet holds enough to found a group and nothing is already being founded. </summary>
        bool CanFound => !isFounding && draftName.Trim().Length > 0;

        /// <summary> Reads every group and sorts it into the two lists by whether the reader is in it. </summary>
        /// <returns> A task that completes once the page has both lists and their member counts. </returns>
        protected override async Task LoadAsync()
        {
            string viewerAddress = SessionService.CurrentAddress;

            Task<IReadOnlyList<GroupData>> allRead = GroupService.ReadRecentAsync();
            Task<IReadOnlyList<GroupData>> mineRead = GroupService.ReadGroupsOfAsync(viewerAddress);
            await Task.WhenAll(allRead, mineRead);

            IReadOnlyList<GroupData> everyGroup = await allRead;
            IReadOnlyList<GroupData> membership = await mineRead;

            HashSet<string> joined = [.. membership.Select(group => group.Address)];

            mine = membership;
            others = [.. everyGroup.Where(group => !joined.Contains(group.Address))];

            // Counted for both lists at once, so a row never goes back to the store for a number the page could
            // have read alongside everything else.
            GroupData[] listed = [.. mine, .. others];
            IReadOnlyList<string>[] members = await Task.WhenAll(
                listed.Select(group => GroupService.ReadMembersAsync(group.Address)));

            Dictionary<string, int> counts = new(listed.Length);
            for (int index = 0; index < listed.Length; index++)
            {
                counts[listed[index].Address] = members[index].Count;
            }

            memberCounts = counts;
            hasLoadedOnce = true;
        }

        /// <summary> How many people are in one listed group. </summary>
        /// <param name="group"> The group being drawn. </param>
        /// <returns> Its member count, or zero while it has not been read. </returns>
        int MemberCountOf(GroupData group) => memberCounts.GetValueOrDefault(group.Address);

        /// <summary> Opens the founding sheet on a clean draft. </summary>
        void OpenFounding()
        {
            draftName = string.Empty;
            draftDescription = string.Empty;
            draftAvatar = GroupData.DefaultAvatar;
            foundErrorMessage = null;
            isFoundingOpen = true;
        }

        /// <summary> Closes the sheet, leaving a founding that is already running to finish. </summary>
        void CloseFounding()
        {
            if (isFounding) return;

            isFoundingOpen = false;
        }

        /// <summary> Keeps the typed name on the page. </summary>
        /// <param name="args"> The input event; its value is the field's new contents. </param>
        void HandleNameInput(ChangeEventArgs args) => draftName = args.Value?.ToString() ?? string.Empty;

        /// <summary> Keeps the typed description on the page. </summary>
        /// <param name="args"> The input event; its value is the field's new contents. </param>
        void HandleDescriptionInput(ChangeEventArgs args) => draftDescription = args.Value?.ToString() ?? string.Empty;

        /// <summary>
        /// Founds the group and opens it. Opening it rather than returning to the listing is what somebody who has
        /// just made a room actually wants next.
        /// </summary>
        /// <returns> A task that completes once the group exists or the attempt has failed. </returns>
        async Task FoundAsync()
        {
            if (!CanFound) return;

            isFounding = true;
            foundErrorMessage = null;

            try
            {
                GroupData? founded = await GroupService.FoundAsync(Account, draftName, draftDescription, draftAvatar);

                if (founded is null)
                {
                    foundErrorMessage = FoundFailureMessage;
                    return;
                }

                isFoundingOpen = false;
                OpenGroup(founded.Address);
            }
            catch (Exception error)
            {
                foundErrorMessage = FoundFailureMessage;
                Log($"{nameof(Groups)} could not found a group.\n{error}", LogLevel.Error);
            }
            finally
            {
                isFounding = false;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary> Joins one group. The listing is not patched here: the service announces the change and this page reloads on it. </summary>
        /// <param name="group"> Group to join. </param>
        /// <returns> A task that completes once the membership has been written. </returns>
        async Task JoinAsync(GroupData group)
        {
            if (joiningAddress.Length > 0) return;

            joiningAddress = group.Address;

            try
            {
                await GroupService.JoinAsync(group, Account.Public);
            }
            catch (Exception error)
            {
                Log($"{nameof(Groups)} could not join '{group.Address}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                joiningAddress = string.Empty;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary> Opens one group's own page. </summary>
        /// <param name="address"> Address of the group to open. </param>
        void OpenGroup(string address) => NavManager.NavigateTo(Group.LinkTo(address));

        /// <summary> Opens the pages screen, which sits beside this one rather than in the bottom bar. </summary>
        void OpenPages() => NavManager.NavigateTo(PageList.PagesRoute);

        /// <summary> Opens the screen that pairs the reader with a stranger. </summary>
        void OpenStrangers() => NavManager.NavigateTo(Strangers.StrangersRoute);

        /// <summary> Opens the screen that lists accounts the reader's own people already follow. </summary>
        void OpenNearby() => NavManager.NavigateTo(Nearby.NearbyRoute);
    }
}
