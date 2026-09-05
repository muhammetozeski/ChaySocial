using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;
using Microsoft.AspNetCore.Components;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary> One person listed in a page's editor sheet, with the name and face their own profile publishes. </summary>
    /// <param name="Address"> The editor's account address. </param>
    /// <param name="Name"> Their display name, or the readable head of their address. </param>
    /// <param name="Avatar"> Their emoji. </param>
    /// <param name="IsFounder"> True for the account that founded the page, whose right cannot be taken. </param>
    public readonly record struct EditorEntry(string Address, string Name, string Avatar, bool IsFounder);

    /// <summary>
    /// The pages screen: the ones this account may speak as, the ones everybody else runs, and the two sheets that
    /// found a page and say who may speak as it. A page is otherwise an ordinary account, so its own screen is the
    /// profile screen every account already has.
    /// </summary>
    public partial class PageList
    {
        /// <summary> Route this page answers on. </summary>
        public const string PagesRoute = "/pages";

        /// <summary> Emoji beside the heading. </summary>
        const string PageEmoji = "📰";

        /// <summary> The screen's heading. </summary>
        const string PageHeadline = "Pages";

        /// <summary> Line under it. </summary>
        const string PageSubtitle = "A name several people can publish under. Everything posted carries the page's own signature.";

        /// <summary> Heading over the pages this account may speak as. </summary>
        const string MineSectionTitle = "Yours to write";

        /// <summary> Heading over the rest. </summary>
        const string OthersSectionTitle = "Others";

        /// <summary> Emoji on the founding button and sheet. </summary>
        const string FoundEmoji = "✨";

        /// <summary> Label on that button. </summary>
        const string FoundLabel = "Start a page";

        /// <summary> Heading of the founding sheet. </summary>
        const string FoundSheetTitle = "Start a page";

        /// <summary> Line under that heading. </summary>
        const string FoundSheetSubtitle = "It gets its own address and its own signature. You can hand the keys to other people afterwards.";

        /// <summary> Grey prompt in the name field. </summary>
        const string NamePlaceholder = "What is the page called?";

        /// <summary> Grey prompt in the description field. </summary>
        const string DescriptionPlaceholder = "What is it about? (optional)";

        /// <summary> Label on the button that founds it. </summary>
        const string FoundConfirmLabel = "Start it";

        /// <summary> Label on the button that closes a sheet without doing anything. </summary>
        const string CancelLabel = "Never mind";

        /// <summary> Label on the button that closes the editor sheet. </summary>
        const string DoneLabel = "Done";

        /// <summary> Message shown when founding was refused. </summary>
        const string FoundFailureMessage = "That didn't go through. Check the name and try again?";

        /// <summary> Emoji on the editor sheet. </summary>
        const string ManageEmoji = "🔑";

        /// <summary> Label on the button that opens it. </summary>
        const string ManageLabel = "Editors";

        /// <summary> Heading of that sheet. </summary>
        const string ManageSheetTitle = "Who can write as this page";

        /// <summary> Line under it, saying plainly what handing over the keys means. </summary>
        const string ManageSheetSubtitle =
            "Anybody here can post as the page. The page's key is sealed to each of them, so it never passes through "
            + "our server in a form anybody else could use.";

        /// <summary> Grey prompt in the field that takes a new editor's address. </summary>
        const string EditorAddressPlaceholder = "Paste an account address…";

        /// <summary> Label on the button that grants the right. </summary>
        const string AddEditorLabel = "Give the keys";

        /// <summary> Shown beside the founder, whose right cannot be taken. </summary>
        const string FounderRoleLabel = "Founder";

        /// <summary> Mark on the control that takes an editor's right back. </summary>
        const string RemoveMark = "✕";

        /// <summary> Tooltip on that control. </summary>
        const string RemoveHint = "Take the keys back";

        /// <summary> Shown under a page this account founded. </summary>
        const string FounderPageRole = "You founded this";

        /// <summary> Shown under a page this account only writes for. </summary>
        const string EditorPageRole = "You can write as this";

        /// <summary> Message shown when the pasted address belongs to nobody. </summary>
        const string UnknownAccountMessage = "No account here has that address.";

        /// <summary> Message shown when handing over the keys was refused. </summary>
        const string AddEditorFailureMessage = "That didn't go through. Try again?";

        /// <summary> Tooltip on a row, which opens the page's profile. </summary>
        const string OpenHint = "Open this page";

        /// <summary> Class marking the chosen emoji in the founding sheet. </summary>
        const string SelectedAvatarClass = "is-chosen";

        /// <summary> Line under the throbber while the listing is read. </summary>
        const string LoadingLabel = "Looking for pages…";

        /// <summary> Emoji on the placeholder shown when nobody else runs a page. </summary>
        const string EmptyEmoji = "🗞️";

        /// <summary> Headline of that placeholder. </summary>
        const string EmptyHeadline = "No other pages yet";

        /// <summary> Supporting line of it. </summary>
        const string EmptyDescription = "Start one and it will be the first thing anybody finds here.";

        /// <summary> Emoji on the placeholder shown when the listing could not be read. </summary>
        const string LoadFailedEmoji = "🌧️";

        /// <summary> Headline of that placeholder. </summary>
        const string LoadFailedHeadline = "This didn't come through";

        /// <summary> Label on the button that runs a failed load again. </summary>
        const string TryAgainLabel = "Try again";

        /// <summary> Route an account's profile lives at; the address is appended to it. </summary>
        const string ProfileRoutePrefix = "/profile/";

        /// <summary> Emoji a new page may be given, offered rather than typed. A whole number of grid rows, so none is left alone. </summary>
        static readonly string[] AvatarChoices = ["📰", "🗞️", "📣", "🎙️", "🏷️", "🎬", "🧭", "🪧"];

        /// <summary> Fully rounded corners on this screen's buttons, as a CSS length. </summary>
        static readonly string PillRadiusCss = $"{AppMeasures.Radius.Pill}px";

        /// <summary> Inside spacing of a leading button. </summary>
        static readonly string ActionPaddingCss = $"{AppMeasures.Space.Px10}px {AppMeasures.Space.Px20}px";

        /// <summary> Inside spacing of a quieter button that sits beside content. </summary>
        static readonly string QuietPaddingCss = $"{AppMeasures.Space.Px6}px {AppMeasures.Space.Px14}px";

        /// <summary> Hairline around those quiet buttons. </summary>
        static string QuietButtonBorder => $"{AppMeasures.Border.Thin}px solid {AppColors.BorderSoft.ToRgbaHex(true)}";

        /// <summary> The frosted ground a row sits on. </summary>
        static string RowSurfaceStyle => AppStyles.BuildAcrylicStyle(AcrylicLevel.Subtle, AppMeasures.Blur.Subtle);

        /// <summary> Diameter of a page's avatar in a row. </summary>
        const int RowAvatarDiameterPx = AppMeasures.Size.Px48;

        /// <summary> Diameter of an editor's avatar in the sheet. </summary>
        const int EditorAvatarDiameterPx = AppMeasures.Size.Px36;

        /// <summary> Diameter of the throbber inside a busy button. </summary>
        const int BusySpinnerDiameterPx = AppMeasures.Size.Px20;

        /// <summary> Ring thickness of that throbber. </summary>
        const int BusySpinnerBorderPx = AppMeasures.Border.Medium;

        /// <summary> Pages this account may speak as. </summary>
        IReadOnlyList<PageData> mine = [];

        /// <summary> Every other page. </summary>
        IReadOnlyList<PageData> others = [];

        /// <summary> Each listed page's own published profile, keyed by address. </summary>
        Dictionary<string, ProfileData?> pageProfiles = [];

        /// <summary> True once a load has finished, so later reloads refresh in place. </summary>
        bool hasLoadedOnce;

        /// <summary> True while the founding sheet is on screen. </summary>
        bool isFoundingOpen;

        /// <summary> True while a page is being founded. </summary>
        bool isFounding;

        /// <summary> Name typed into the founding sheet. </summary>
        string draftName = string.Empty;

        /// <summary> Description typed into the founding sheet. </summary>
        string draftDescription = string.Empty;

        /// <summary> Emoji chosen for the new page. </summary>
        string draftAvatar = "📰";

        /// <summary> Message shown when founding failed. </summary>
        string? foundErrorMessage;

        /// <summary> Page whose editors are being managed, or null while that sheet is closed. </summary>
        PageData? managedPage;

        /// <summary> Who may currently speak as that page. </summary>
        IReadOnlyList<EditorEntry> editors = [];

        /// <summary> Address typed into the field that grants the right. </summary>
        string draftEditorAddress = string.Empty;

        /// <summary> True while an editor is being added or removed. </summary>
        bool isChangingEditors;

        /// <summary> Message shown when granting or taking back the right failed. </summary>
        string? editorErrorMessage;

        /// <summary> Reloads when a page changes and when the signed-in account changes. </summary>
        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.PagesChanged,
            MainEvents.Names.SessionChanged
        ];

        /// <summary> True while the very first load runs. </summary>
        bool IsFirstLoad => IsLoading && !hasLoadedOnce;

        /// <summary> True when the founding sheet holds enough to found a page. </summary>
        bool CanFound => !isFounding && draftName.Trim().Length > 0;

        /// <summary> True when the editor sheet holds something worth trying as an address. </summary>
        bool CanAddEditor => !isChangingEditors && draftEditorAddress.Trim().Length > 0;

        /// <summary> Reads every page, sorts it by whether this account may write as it, and reads each one's profile. </summary>
        /// <returns> A task that completes once the screen has all of it. </returns>
        protected override async Task LoadAsync()
        {
            string viewerAddress = SessionService.CurrentAddress;

            Task<IReadOnlyList<PageData>> allRead = PageService.ReadRecentAsync();
            Task<IReadOnlyList<PageData>> mineRead = PageService.ReadPagesOfAsync(viewerAddress);
            await Task.WhenAll(allRead, mineRead);

            IReadOnlyList<PageData> everyPage = await allRead;
            IReadOnlyList<PageData> writable = await mineRead;

            HashSet<string> writableAddresses = [.. writable.Select(page => page.Address)];

            mine = writable;
            others = [.. everyPage.Where(page => !writableAddresses.Contains(page.Address))];

            // A page's name and face live on its own profile, which is the whole point of a page being an account.
            string[] addresses = [.. mine.Select(page => page.Address), .. others.Select(page => page.Address)];
            ProfileData?[] profiles = await Task.WhenAll(addresses.Select(ProfileService.ReadAsync));

            Dictionary<string, ProfileData?> byAddress = new(addresses.Length);
            for (int index = 0; index < addresses.Length; index++)
            {
                byAddress[addresses[index]] = profiles[index];
            }

            pageProfiles = byAddress;
            hasLoadedOnce = true;
        }

        /// <summary> The name a page publishes, or the readable head of its address. </summary>
        /// <param name="page"> The page being drawn. </param>
        /// <returns> Its name. </returns>
        string NameOf(PageData page)
        {
            ProfileData? profile = pageProfiles.GetValueOrDefault(page.Address);
            return string.IsNullOrWhiteSpace(profile?.DisplayName)
                ? ProfileService.FallbackDisplayName(page.Address)
                : profile.DisplayName;
        }

        /// <summary> The emoji a page publishes, or the one its address maps to. </summary>
        /// <param name="page"> The page being drawn. </param>
        /// <returns> Its emoji. </returns>
        string AvatarOf(PageData page)
        {
            ProfileData? profile = pageProfiles.GetValueOrDefault(page.Address);
            return string.IsNullOrWhiteSpace(profile?.Avatar) ? ProfileService.PickAvatar(page.Address) : profile.Avatar;
        }

        /// <summary> What this account is to a page it can write as. </summary>
        /// <param name="page"> The page being drawn. </param>
        /// <returns> The line under its name. </returns>
        static string RoleOf(PageData page)
            => page.FounderAddress == SessionService.CurrentAddress ? FounderPageRole : EditorPageRole;

        /// <summary> Opens the founding sheet on a clean draft. </summary>
        void OpenFounding()
        {
            draftName = string.Empty;
            draftDescription = string.Empty;
            draftAvatar = AvatarChoices[0];
            foundErrorMessage = null;
            isFoundingOpen = true;
        }

        /// <summary> Closes it, leaving a founding already under way to finish. </summary>
        void CloseFounding()
        {
            if (isFounding) return;

            isFoundingOpen = false;
        }

        /// <summary> Keeps the typed name on the screen. </summary>
        /// <param name="args"> The input event. </param>
        void HandleNameInput(ChangeEventArgs args) => draftName = args.Value?.ToString() ?? string.Empty;

        /// <summary> Keeps the typed description on the screen. </summary>
        /// <param name="args"> The input event. </param>
        void HandleDescriptionInput(ChangeEventArgs args) => draftDescription = args.Value?.ToString() ?? string.Empty;

        /// <summary> Keeps the typed editor address on the screen. </summary>
        /// <param name="args"> The input event. </param>
        void HandleEditorAddressInput(ChangeEventArgs args) => draftEditorAddress = args.Value?.ToString() ?? string.Empty;

        /// <summary> Founds the page and opens its profile, which is where somebody who just made one wants to be. </summary>
        /// <returns> A task that completes once the page exists or the attempt has failed. </returns>
        async Task FoundAsync()
        {
            if (!CanFound) return;

            isFounding = true;
            foundErrorMessage = null;

            try
            {
                PageData? founded = await PageService.FoundAsync(Account, draftName, draftDescription, draftAvatar);

                if (founded is null)
                {
                    foundErrorMessage = FoundFailureMessage;
                    return;
                }

                isFoundingOpen = false;
                OpenProfile(founded.Address);
            }
            catch (Exception error)
            {
                foundErrorMessage = FoundFailureMessage;
                Log($"{nameof(PageList)} could not found a page.\n{error}", LogLevel.Error);
            }
            finally
            {
                isFounding = false;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary> Opens the editor sheet for one page and reads who is currently in it. </summary>
        /// <param name="page"> The page to manage. </param>
        /// <returns> A task that completes once the editors are on screen. </returns>
        async Task OpenEditorsAsync(PageData page)
        {
            managedPage = page;
            draftEditorAddress = string.Empty;
            editorErrorMessage = null;
            editors = [];

            await RefreshEditorsAsync();
        }

        /// <summary> Closes the editor sheet. </summary>
        void CloseEditors()
        {
            if (isChangingEditors) return;

            managedPage = null;
        }

        /// <summary> Re-reads who may speak as the managed page, with each of their published names and faces. </summary>
        /// <returns> A task that completes once the list is current. </returns>
        async Task RefreshEditorsAsync()
        {
            if (managedPage is null) return;

            IReadOnlyList<string> addresses = await PageService.ReadEditorsAsync(managedPage.Address);
            ProfileData?[] profiles = await Task.WhenAll(addresses.Select(ProfileService.ReadAsync));

            List<EditorEntry> listed = new(addresses.Count);
            for (int index = 0; index < addresses.Count; index++)
            {
                ProfileData? profile = profiles[index];

                listed.Add(new EditorEntry(
                    addresses[index],
                    string.IsNullOrWhiteSpace(profile?.DisplayName)
                        ? ProfileService.FallbackDisplayName(addresses[index])
                        : profile.DisplayName,
                    string.IsNullOrWhiteSpace(profile?.Avatar)
                        ? ProfileService.PickAvatar(addresses[index])
                        : profile.Avatar,
                    addresses[index] == managedPage.FounderAddress));
            }

            editors = listed;
        }

        /// <summary> Hands the page's keys to the pasted address. </summary>
        /// <returns> A task that completes once the right is granted or the attempt has failed. </returns>
        async Task AddEditorAsync()
        {
            if (!CanAddEditor || managedPage is null) return;

            isChangingEditors = true;
            editorErrorMessage = null;

            try
            {
                ProfileData? invited = await ProfileService.ReadAsync(draftEditorAddress.Trim());

                if (invited is null)
                {
                    editorErrorMessage = UnknownAccountMessage;
                    return;
                }

                if (!await PageService.AddEditorAsync(managedPage, Account, invited))
                {
                    editorErrorMessage = AddEditorFailureMessage;
                    return;
                }

                draftEditorAddress = string.Empty;
                await RefreshEditorsAsync();
            }
            catch (Exception error)
            {
                editorErrorMessage = AddEditorFailureMessage;
                Log($"{nameof(PageList)} could not add an editor to '{managedPage.Address}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                isChangingEditors = false;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary> Takes one editor's right back. </summary>
        /// <param name="editorAddress"> Account losing it. </param>
        /// <returns> A task that completes once the right is gone. </returns>
        async Task RemoveEditorAsync(string editorAddress)
        {
            if (isChangingEditors || managedPage is null) return;

            isChangingEditors = true;
            editorErrorMessage = null;

            try
            {
                await PageService.RemoveEditorAsync(managedPage, Account, editorAddress);
                await RefreshEditorsAsync();
            }
            catch (Exception error)
            {
                editorErrorMessage = AddEditorFailureMessage;
                Log($"{nameof(PageList)} could not remove an editor from '{managedPage.Address}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                isChangingEditors = false;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary> Opens a page's profile, which is the screen every account already has. </summary>
        /// <param name="address"> The page's address. </param>
        void OpenProfile(string address) => NavManager.NavigateTo($"{ProfileRoutePrefix}{address}");
    }
}
