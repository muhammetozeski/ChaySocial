using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Moderation;
using ChaySocial.MainProject.Protection;
using ChaySocial.MainProject.Services;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// The owner's own screen: the address other people know them by, the secret that <em>is</em> the account, the
    /// accounts they have blocked, and the way out of this device. It is deliberately absent from the bottom
    /// navigation — the profile page is the one door into it — because everything here is either private or
    /// irreversible, and neither belongs one stray tap away.
    /// </summary>
    public partial class Settings
    {
        /// <summary> How far the owner has taken the secret panel. Revealing takes two deliberate presses, never one. </summary>
        public enum SecretStage
        {
            /// <summary> Nothing of the secret is on screen; only the offer to reveal it. </summary>
            Hidden,

            /// <summary> The owner asked for it and is being asked to check who else can see the screen. </summary>
            Confirming,

            /// <summary> The characters are on screen, in a readonly field the owner can select and copy. </summary>
            Revealed
        }

        /// <summary> One blocked account as this screen draws it: the address that was blocked, and its profile when that account published one. </summary>
        /// <param name="Address"> Address the owner blocked. </param>
        /// <param name="Profile"> That account's public profile, or null when it has never published one. </param>
        public readonly record struct BlockedAccount(string Address, ProfileData? Profile);

        /// <summary>
        /// Route this screen answers on. Public so the profile page — the only place that links here — can name the
        /// route instead of spelling the path a second time.
        /// </summary>
        public const string SettingsRoute = "/settings";

        /// <summary> Where the back arrow leads: the owner's own profile, which is where this screen was opened from. </summary>
        const string ProfileRoute = NavigationConstants.Profile.Link;

        /// <summary> Text on the browser tab / window title. </summary>
        const string PageTitleText = "Settings";

        /// <summary> Heading at the top of the screen. </summary>
        const string PageHeadline = "Settings";

        /// <summary> Line under the heading, naming what all of this has in common. </summary>
        const string PageSubtitle = "Your account lives on this device. Here is everything that means.";

        /// <summary> Emoji beside the heading. </summary>
        const string PageEmoji = "⚙️";

        /// <summary> Emoji on the section holding the owner's address. </summary>
        const string AddressSectionEmoji = "🏷️";

        /// <summary> Heading of that section. </summary>
        const string AddressSectionHeadline = "Your address";

        /// <summary> Line under that heading, saying what the address is safe to do with. </summary>
        const string AddressSectionDescription =
            "This is who you are here. Hand it to anybody you would like to be found by — it is public by design, and it gives away nothing about your secret.";

        /// <summary> Emoji on the section that changes how the app looks. </summary>
        const string AppearanceSectionEmoji = "🎨";

        /// <summary> Heading of the section that changes how the app looks. </summary>
        const string AppearanceSectionHeadline = "Appearance";

        /// <summary> Line under that heading. </summary>
        const string AppearanceSectionDescription =
            "Pick the colours you would rather read in. The choice stays on this device and takes effect at once.";

        /// <summary> Emoji on the section holding the secret. </summary>
        const string SecretSectionEmoji = "🔐";

        /// <summary> Heading of that section. </summary>
        const string SecretSectionHeadline = "Your secret";

        /// <summary> Line under that heading: where the secret lives and who cannot help you with it. </summary>
        const string SecretSectionDescription =
            "Your secret is the whole account. It was drawn on this device and never reaches our server, so nobody here can read it, reset it, or hand it back to you.";

        /// <summary> Emoji on the button that begins revealing the secret. </summary>
        const string RevealEmoji = "👁️";

        /// <summary> Text on that button. </summary>
        const string RevealLabel = "Reveal my secret";

        /// <summary> Emoji on the panel that asks the owner to look around before the characters appear. </summary>
        const string RevealConfirmEmoji = "🫣";

        /// <summary> Headline of that panel. </summary>
        const string RevealConfirmHeadline = "Who else can see this screen?";

        /// <summary> Line under that headline, saying exactly what pressing on does. </summary>
        const string RevealConfirmDescription =
            "The characters appear in plain sight. Anybody who reads them — over your shoulder, in a photo, on a shared screen — owns this account from that moment on.";

        /// <summary> Text on the button that goes ahead and shows the secret. </summary>
        const string RevealConfirmLabel = "Show it anyway";

        /// <summary> Text on the button that backs out without showing anything. </summary>
        const string RevealCancelLabel = "Not now";

        /// <summary> Emoji on the warning that sits beside the revealed secret. </summary>
        const string SecretWarningEmoji = "⚠️";

        /// <summary> The warning itself: what these characters are worth, and that no copy exists anywhere else. </summary>
        const string SecretWarningText =
            "Lose these characters and the account is gone with them — there is no reset, because nobody is holding a copy. Share them and the account is theirs.";

        /// <summary> Screen-reader name of the readonly field holding the secret. </summary>
        const string SecretFieldLabel = "Your account secret";

        /// <summary> Emoji on the button that puts the secret away again. </summary>
        const string HideSecretEmoji = "🙈";

        /// <summary> Text on that button. </summary>
        const string HideSecretLabel = "Hide it again";

        /// <summary> Shown in place of the secret when reading it back off this device threw. </summary>
        const string SecretUnavailableMessage = "We couldn't read your secret off this device just now. Give it another try?";

        /// <summary> Text lines the secret field shows before it starts scrolling. </summary>
        const int SecretRowCount = 3;

        /// <summary> Emoji on the section listing blocked accounts. </summary>
        /// <summary> Emoji on the anonymity section. </summary>
        const string AnonymitySectionEmoji = "🧅";

        /// <summary> Heading of that section. </summary>
        const string AnonymitySectionHeadline = "Anonymity";

        /// <summary> Line under it. </summary>
        const string AnonymitySectionDescription =
            "These choices live on this device and reach no server. Nobody is told what you decide here.";

        /// <summary> Label on the switch that refuses anything but Tor. </summary>
        const string TorOnlyLabel = "Refuse anything that is not Tor";

        /// <summary> Line under that label, saying plainly how it is judged. </summary>
        const string TorOnlyHint =
            "Judged from the address this app was served on — a hidden service answers on a .onion host. "
            + "It bites the next time the app opens.";

        /// <summary> Leads the address the app was actually reached on. </summary>
        const string ReachedOnLabel = "Reached on";

        /// <summary> Class added to a switch that is on. </summary>
        const string SwitchOnClass = "is-on";

        /// <summary> Emoji on the section leading to the account's archive. </summary>
        const string ArchiveSectionEmoji = "🧳";

        /// <summary> Heading of that section. </summary>
        const string ArchiveSectionHeadline = "Everything you have written";

        /// <summary> Line under it, saying why the archive is the other half of the secret. </summary>
        const string ArchiveSectionDescription =
            "Your secret carries who you are to another machine. Your archive carries what you wrote. Seal one and " +
            "you can walk away from this server whenever you like, and lose nothing by it.";

        /// <summary> What the link into the archive screen says. </summary>
        const string ArchiveSectionLinkLabel = "Open my archive";

        /// <summary> Emoji on the section where the reader decides what their own device covers. </summary>
        const string GuardSectionEmoji = "🪟";

        /// <summary> Heading of that section. </summary>
        const string GuardSectionHeadline = "What you would rather not see first";

        /// <summary> Line under it, saying plainly what this does and, more importantly, what it does not do. </summary>
        const string GuardSectionDescription =
            "Your device reads what arrives and can draw a curtain over the worst of it, which you lift with one tap. " +
            "Nothing is deleted, nothing leaves this device, and nobody is told you covered them.";

        /// <summary> Class added to the band this reader has chosen. </summary>
        const string GuardChosenClass = "is-chosen";

        /// <summary> Remembers what this reader wants covered and redraws everything already on screen. </summary>
        /// <param name="band"> The lowest band to cover. </param>
        /// <returns> A task that completes once the choice has been stored. </returns>
        async Task ChooseCoveredBandAsync(ContentBand band) => await ContentGuard.ApplyAndRememberAsync(band);

        /// <summary> Emoji on the carried-accounts section. </summary>
        const string AccountsSectionEmoji = "🎭";

        /// <summary> Heading of that section. </summary>
        const string AccountsSectionHeadline = "Accounts on this device";

        /// <summary> Line under it. </summary>
        const string AccountsSectionDescription =
            "One person can keep as many accounts here as they like. Every account you sign into stays on this device "
            + "so switching costs one tap instead of pasting a secret back in.";

        /// <summary> Line under the list, on what dropping an account does and does not do. </summary>
        const string AccountsNote =
            "Dropping one only takes it off this device. The account itself is untouched and reopens with its secret.";

        /// <summary> Shown beside the account currently open. </summary>
        const string OpenAccountLabel = "Open";

        /// <summary> Class marking that row. </summary>
        const string OpenAccountClass = "is-open";

        /// <summary> Label on the control that opens another carried account. </summary>
        const string SwitchLabel = "Switch";

        /// <summary> Mark on the control that stops carrying one. </summary>
        const string DropMark = "✕";

        /// <summary> Tooltip on that control. </summary>
        const string DropHint = "Forget this account on this device";

        /// <summary> Diameter of a carried account's avatar. </summary>
        const int CarriedAvatarDiameterPx = AppMeasures.Size.Px36;

        /// <summary> True while this device refuses anything that is not Tor. </summary>
        bool _isTorOnly;

        /// <summary> True while an anonymity choice or an account switch is being written. </summary>
        bool _isChangingAnonymity;

        /// <summary> Accounts this device is carrying. </summary>
        IReadOnlyList<CarriedAccount> _carriedAccounts = [];

        /// <summary> Their published profiles, keyed by address. </summary>
        Dictionary<string, ProfileData?> _carriedProfiles = [];

        /// <summary> The address this app was actually served from, shown so the Tor judgement is checkable. </summary>
        string ReachedHost => NavManager.BaseUri;

        /// <summary> Emoji on the reports section. </summary>
        const string ReportsSectionEmoji = "🚩";

        /// <summary> Heading of that section. </summary>
        const string ReportsSectionHeadline = "Reports you filed";

        /// <summary> Line under it. </summary>
        const string ReportsSectionDescription =
            "What you reported and where each one got to. Nobody is told you filed these.";

        /// <summary> Shown when this account has reported nothing. </summary>
        const string NoReportsLabel = "You haven't reported anything.";

        /// <summary> Line under the list, on what withdrawing does. </summary>
        const string ReportsNote =
            "Withdrawing takes the complaint back and deletes the content you handed over with it.";

        /// <summary> Label on the control that takes a report back. </summary>
        const string WithdrawLabel = "Withdraw";

        /// <summary> Tooltip on it. </summary>
        const string WithdrawHint = "Take this complaint back";

        /// <summary> Reports this account filed, newest first. </summary>
        IReadOnlyList<ReportData> _reports = [];

        /// <summary> True while a withdrawal is being written. </summary>
        bool _isWithdrawing;

        /// <summary> What one report is about, as the reader reads it. </summary>
        /// <param name="kind"> The report's kind. </param>
        /// <returns> Its label. </returns>
        static string KindLabel(ReportKind kind) => kind switch
        {
            ReportKind.Post => "Post",
            ReportKind.Account => "Account",
            ReportKind.Comment => "Comment",
            _ => "Message"
        };

        /// <summary> Why it was filed, as the reader reads it. </summary>
        /// <param name="reason"> The reason chosen. </param>
        /// <returns> Its label. </returns>
        static string ReasonLabel(ReportReason reason) => reason switch
        {
            ReportReason.Spam => "spam",
            ReportReason.Harassment => "harassment",
            ReportReason.Violence => "violence",
            ReportReason.SexualContent => "sexual content",
            ReportReason.Impersonation => "impersonation",
            _ => "something else"
        };

        /// <summary> Where it got to, as the reader reads it. </summary>
        /// <param name="status"> The report's status. </param>
        /// <returns> Its label. </returns>
        static string StatusLabel(ReportStatus status) => status switch
        {
            ReportStatus.Open => "Open",
            ReportStatus.Upheld => "Acted on",
            ReportStatus.Dismissed => "Nothing done",
            _ => "Withdrawn"
        };

        /// <summary> Takes one of this account's own complaints back. </summary>
        /// <param name="report"> The report to withdraw. </param>
        /// <returns> A task that completes once it is withdrawn. </returns>
        async Task WithdrawReportAsync(ReportData report)
        {
            if (_isWithdrawing) return;

            _isWithdrawing = true;

            try
            {
                await ModerationService.WithdrawAsync(report, Account.Public);
                _reports = await ModerationService.ReadFiledByAsync(SessionService.CurrentAddress);
            }
            catch (Exception error)
            {
                Log($"{nameof(Settings)} could not withdraw '{report.ReportId}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                _isWithdrawing = false;
            }
        }

        const string BlockedSectionEmoji = "🚫";

        /// <summary> Heading of that section. </summary>
        const string BlockedSectionHeadline = "Blocked accounts";

        /// <summary> Line under that heading, saying what a block does in both directions. </summary>
        const string BlockedSectionDescription =
            "Blocked accounts cannot see you, and you cannot see them. Lifting a block takes effect at once, and nobody is told either way.";

        /// <summary> Text on the button that lifts one block. </summary>
        const string UnblockLabel = "Unblock";

        /// <summary> Emoji on the placeholder shown when nobody is blocked. </summary>
        const string EmptyBlockedEmoji = "🕊️";

        /// <summary> Headline of that placeholder. </summary>
        const string EmptyBlockedHeadline = "Nobody is blocked";

        /// <summary> Supporting line of that placeholder, saying what would fill the list. </summary>
        const string EmptyBlockedDescription = "Anyone you block gathers here, so you can let them back in whenever you like.";

        /// <summary> Emoji on the section that ends the session on this device. </summary>
        const string SignOutSectionEmoji = "👋";

        /// <summary> Heading of that section. </summary>
        const string SignOutSectionHeadline = "Sign out";

        /// <summary> Line under that heading: what signing out forgets, and what it leaves alone. </summary>
        const string SignOutSectionDescription =
            "Signing out forgets your secret on this device and nothing else. The account itself stays exactly where it is, waiting for that secret to be pasted back in.";

        /// <summary> Text on the button that opens the sign-out confirmation. </summary>
        const string SignOutLabel = "Sign out";

        /// <summary> Heading of the sign-out confirmation. </summary>
        const string SignOutConfirmTitle = "Sign out of this device?";

        /// <summary> Line under that heading, naming the one thing to check first. </summary>
        const string SignOutConfirmDescription =
            "Make sure your secret is written down somewhere safe before you go. Without it, nobody — us included — can open this account again.";

        /// <summary> Text on the button that actually signs out. </summary>
        const string SignOutConfirmLabel = "Sign me out";

        /// <summary> Text on the button that closes the confirmation and changes nothing. </summary>
        const string SignOutCancelLabel = "Stay signed in";

        /// <summary> Line under the throbber while the session is being closed. </summary>
        const string SignOutBusyLabel = "Putting your secret away…";

        /// <summary> Emoji over the message shown when this screen could not be loaded. </summary>
        const string LoadFailureEmoji = "🌧️";

        /// <summary> Text on the button that runs a failed load again. </summary>
        const string RetryLabel = "Try again";

        /// <summary> Diameter of the owner's avatar beside their address: large enough to read as a portrait rather than a list-row bubble. </summary>
        const int OwnerAvatarDiameterPx = AppMeasures.Size.Px64;

        /// <summary> Characters kept at each end of the abbreviated address pill here, where the whole address sits right below it anyway. </summary>
        const int AddressVisibleCharacters = 6;

        /// <summary> Diameter of the avatar on a blocked row that has no profile behind it, matching the bubble <c>PersonRow</c> draws so both kinds of row line up. </summary>
        const int BlockedRowAvatarDiameterPx = AppMeasures.Size.Px48;

        /// <summary> Diameter of the throbber that replaces a button's label while its work runs. </summary>
        const int BusySpinnerDiameterPx = AppMeasures.Size.Px16;

        /// <summary> Ring thickness of that throbber, thinned so a small circle still reads as a ring. </summary>
        const int BusySpinnerBorderPx = AppMeasures.Border.Medium;

        /// <summary> Corner radius shared by this screen's section cards. </summary>
        static readonly string CardRadius = $"{AppMeasures.Radius.XLarge}px";

        /// <summary> Inside spacing of those cards. </summary>
        static readonly string CardPadding = $"{AppMeasures.Space.Px24}px";

        /// <summary> Fully rounded corners on every button this screen draws. </summary>
        static readonly string ButtonRadius = $"{AppMeasures.Radius.Pill}px";

        /// <summary> Inside spacing of a leading button, wide enough to stay comfortable to tap on a phone. </summary>
        static readonly string ActionButtonPadding = $"{AppMeasures.Space.Px12}px {AppMeasures.Space.Px24}px";

        /// <summary> Inside spacing of a quieter button — an unblock, a "not now" — which sits beside content rather than under it. </summary>
        static readonly string QuietButtonPadding = $"{AppMeasures.Space.Px8}px {AppMeasures.Space.Px16}px";

        /// <summary> Hairline around a quiet button, following the active theme rather than freezing one glass edge. </summary>
        static string QuietButtonBorder => $"{AppMeasures.Border.Thin}px solid {AppColors.GlassBorderDefault.ToRgbaHex(true)}";

        /// <summary> Emoji beside the writing-permit heading. </summary>
        const string PermitSectionEmoji = "✍️";

        /// <summary> Heading of the writing-permit card. </summary>
        const string PermitSectionHeadline = "Permission to write";

        /// <summary> Line under it, saying plainly what the cost is and why anybody would pay it. </summary>
        const string PermitSectionDescription =
            "Reading costs nothing and never will. Writing is earned once: this device works on a puzzle for a few "
            + "minutes, and after that everything you post or send goes out instantly, forever. It is what stops one "
            + "person quietly running a thousand accounts.";

        /// <summary> Mark on the settled state once a permit is held. </summary>
        const string PermitGrantedEmoji = "✅";

        /// <summary> Text beside that mark. </summary>
        const string PermitGrantedLabel = "You can write. Nothing here will ask you to wait again.";

        /// <summary> Emoji on the button that starts the work. </summary>
        const string PermitStartEmoji = "⏳";

        /// <summary> Label on that button. </summary>
        const string PermitStartLabel = "Earn it now";

        /// <summary> Line shown while the work runs. </summary>
        const string PermitWorkingLabel = "Working on it…";

        /// <summary>
        /// Line under that. It says to stay on the tab because that is true: a browser slows a hidden tab right
        /// down, and telling somebody to walk away would be telling them to make their own wait longer.
        /// </summary>
        const string PermitWorkingNote =
            "Keep this tab in front — browsers slow down work in a tab you have switched away from. Closing it stops "
            + "the work, and starting again costs you only the time already spent.";

        /// <summary> Format of the attempt counter; the placeholder takes the count. </summary>
        const string PermitAttemptsFormat = "{0:N0} attempts";

        /// <summary> Message shown when the work finished but the server would not grant the permit. </summary>
        const string PermitFailureMessage = "That didn't go through. Give it another try?";

        /// <summary> Diameter of the throbber shown while the permit is being earned. </summary>
        const int PermitSpinnerDiameterPx = AppMeasures.Size.Px24;

        /// <summary> Ring thickness of that throbber. </summary>
        const int PermitSpinnerBorderPx = AppMeasures.Border.Medium;

        /// <summary> True while the permit is being worked on, which swaps the button for a progress display. </summary>
        bool _isEarningPermit;

        /// <summary> How many attempts the search has made, shown so the wait visibly does something. </summary>
        long _permitAttempts;

        /// <summary> Message drawn when earning the permit failed, or null when it did not. </summary>
        string? _permitErrorMessage;

        /// <summary> True when this account already holds a writing permit. </summary>
        bool HasWritingPermit => AppServices.ProofOfWork?.HasWritingPermit == true;

        /// <summary> True while the work is running. </summary>
        bool IsEarningPermit => _isEarningPermit;

        /// <summary> The attempt counter as it is read. </summary>
        string PermitAttemptsLabel => string.Format(PermitAttemptsFormat, _permitAttempts);

        /// <summary> Message drawn when earning the permit failed, or null when it did not. </summary>
        string? PermitErrorMessage => _permitErrorMessage;

        /// <summary>
        /// Works through the permit puzzle and claims the permit with the answer. This is the one long wait in the
        /// app; the screen shows the attempt count throughout so nobody is left wondering whether it has hung.
        /// </summary>
        /// <returns> A task that completes once the permit is granted or the attempt has failed. </returns>
        async Task EarnWritingPermitAsync()
        {
            if (_isEarningPermit || AppServices.ProofOfWork is not ProofOfWorkClient proof) return;

            _isEarningPermit = true;
            _permitAttempts = 0;
            _permitErrorMessage = null;

            try
            {
                bool granted = await proof.EarnWritingPermitAsync(SessionService.CurrentAddress, ReportPermitProgress);
                if (!granted) _permitErrorMessage = PermitFailureMessage;
            }
            catch (Exception error)
            {
                _permitErrorMessage = PermitFailureMessage;
                Log($"{nameof(Settings)} could not earn a writing permit.\n{error}", LogLevel.Error);
            }
            finally
            {
                _isEarningPermit = false;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary>
        /// Takes an attempt count from the search and redraws. The search hands the thread back between attempts,
        /// which is what lets this repaint at all while the work is running.
        /// </summary>
        /// <param name="attempts"> Attempts made so far. </param>
        void ReportPermitProgress(long attempts)
        {
            _permitAttempts = attempts;

            if (attempts % PermitRedrawInterval == 0) StateHasChanged();
        }

        /// <summary> Attempts between redraws, so a counter climbing through thousands does not repaint per attempt. </summary>
        const int PermitRedrawInterval = 4;

        /// <summary> The accounts this owner has blocked, with whatever profile each of them published. </summary>
        IReadOnlyList<BlockedAccount> _blocked = [];

        /// <summary> How far the secret panel has been taken. </summary>
        SecretStage _secretStage = SecretStage.Hidden;

        /// <summary> The secret while it is on screen. Held only between revealing and hiding, and never written anywhere by this page. </summary>
        string _secretText = string.Empty;

        /// <summary> Message drawn in place of the secret when reading it threw, or null when it did not. </summary>
        string? _secretFailureMessage;

        /// <summary> Address whose block is being lifted right now, or null while no unblock is running. </summary>
        string? _unblockingAddress;

        /// <summary> True while the sign-out confirmation is on screen. </summary>
        bool _isSignOutOpen;

        /// <summary> True while the session is actually being closed, which locks the confirmation open until it finishes. </summary>
        bool _isSigningOut;

        /// <summary> Reloads when a block is placed or lifted, and when the signed-in account changes under this screen. </summary>
        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.ModerationChanged,
            MainEvents.Names.SessionChanged
        ];

        /// <summary> The owner's display name, falling back to the readable head of their address when they never set one. </summary>
        string OwnerName => string.IsNullOrWhiteSpace(SessionService.CurrentProfile?.DisplayName)
            ? ProfileService.FallbackDisplayName(SessionService.CurrentAddress)
            : SessionService.CurrentProfile!.DisplayName;

        /// <summary> The owner's avatar emoji, falling back to the one their address would have been given. </summary>
        string OwnerAvatar => string.IsNullOrWhiteSpace(SessionService.CurrentProfile?.Avatar)
            ? ProfileData.DefaultAvatar
            : SessionService.CurrentProfile!.Avatar;

        /// <summary> True while the two-step reveal has reached the question but not the answer. </summary>
        bool IsAskingToReveal => _secretStage == SecretStage.Confirming;

        /// <summary> True while the secret characters are on screen. </summary>
        bool IsSecretShowing => _secretStage == SecretStage.Revealed;

        /// <summary>
        /// Reads the accounts this owner has blocked and the profile behind each of them. Signing out fires the very
        /// event this page reloads on, so an empty session is a normal outcome here rather than a failure: the list
        /// is emptied and the secret is dropped, and the sign-out itself does the navigating.
        /// </summary>
        /// <returns> A task that completes once the list has been read. </returns>
        protected override async Task LoadAsync()
        {
            if (!SessionService.IsSignedIn)
            {
                _blocked = [];
                _carriedAccounts = [];
                _reports = [];
                ForgetSecret();
                return;
            }

            _blocked = await ReadBlockedAsync(SessionService.CurrentAddress);
            _reports = await ModerationService.ReadFiledByAsync(SessionService.CurrentAddress);
            _isTorOnly = await AnonymityService.IsTorOnlyAsync();
            _carriedAccounts = await AnonymityService.ReadCarriedAsync();
            _carriedProfiles = await ReadCarriedProfilesAsync(_carriedAccounts);
        }

        /// <summary> Reads the published profile of each carried account, so the switcher shows faces and names. </summary>
        /// <param name="carried"> The accounts this device holds. </param>
        /// <returns> Each account's profile keyed by address; a value is null when that account published none. </returns>
        static async Task<Dictionary<string, ProfileData?>> ReadCarriedProfilesAsync(IReadOnlyList<CarriedAccount> carried)
        {
            ProfileData?[] profiles = await Task.WhenAll(carried.Select(account => ProfileService.ReadAsync(account.Address)));

            Dictionary<string, ProfileData?> byAddress = new(carried.Count);
            for (int index = 0; index < carried.Count; index++)
            {
                byAddress[carried[index].Address] = profiles[index];
            }

            return byAddress;
        }

        /// <summary> The name drawn for a carried account. </summary>
        /// <param name="address"> The account's address. </param>
        /// <returns> Its display name, or the readable head of the address. </returns>
        string NameFor(string address)
        {
            ProfileData? profile = _carriedProfiles.GetValueOrDefault(address);
            return string.IsNullOrWhiteSpace(profile?.DisplayName)
                ? ProfileService.FallbackDisplayName(address)
                : profile.DisplayName;
        }

        /// <summary> The emoji drawn for a carried account. </summary>
        /// <param name="address"> The account's address. </param>
        /// <returns> Its avatar, or the one its address maps to. </returns>
        string AvatarFor(string address)
        {
            ProfileData? profile = _carriedProfiles.GetValueOrDefault(address);
            return string.IsNullOrWhiteSpace(profile?.Avatar) ? ProfileService.PickAvatar(address) : profile.Avatar;
        }

        /// <summary> Turns the Tor requirement on or off. It bites on the next load, which is when the address is judged. </summary>
        /// <returns> A task that completes once the choice is stored. </returns>
        async Task ToggleTorOnlyAsync()
        {
            if (_isChangingAnonymity) return;

            _isChangingAnonymity = true;

            try
            {
                _isTorOnly = !_isTorOnly;
                await AnonymityService.SetTorOnlyAsync(_isTorOnly);
            }
            finally
            {
                _isChangingAnonymity = false;
            }
        }

        /// <summary> Opens another account this device is carrying. </summary>
        /// <param name="account"> The account to switch to. </param>
        /// <returns> A task that completes once that account is the open one. </returns>
        async Task SwitchToAsync(CarriedAccount account)
        {
            if (_isChangingAnonymity) return;

            _isChangingAnonymity = true;

            try
            {
                if (await SessionService.SwitchToAsync(account.Secret)) NavManager.NavigateTo(ProfileRoute);
            }
            catch (Exception error)
            {
                Log($"{nameof(Settings)} could not switch to '{account.Address}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                _isChangingAnonymity = false;
            }
        }

        /// <summary> Stops carrying one account on this device. The account itself is untouched. </summary>
        /// <param name="account"> The account to drop. </param>
        /// <returns> A task that completes once it is gone from this device. </returns>
        async Task DropCarriedAsync(CarriedAccount account)
        {
            if (_isChangingAnonymity) return;

            _isChangingAnonymity = true;

            try
            {
                await AnonymityService.DropAsync(account.Address);
                _carriedAccounts = await AnonymityService.ReadCarriedAsync();
            }
            finally
            {
                _isChangingAnonymity = false;
            }
        }

        /// <summary>
        /// Reads every blocked address and, for each, the profile that address published. An account with no stored
        /// profile still comes back, so a block can always be lifted even when there is no name to draw beside it.
        /// </summary>
        /// <param name="ownerAddress"> Address whose blocks are being listed. </param>
        /// <returns> One entry per blocked account, in the order the store returned them. </returns>
        static async Task<IReadOnlyList<BlockedAccount>> ReadBlockedAsync(string ownerAddress)
        {
            IReadOnlyList<string> addresses = await ModerationService.ReadBlockedAddressesAsync(ownerAddress);
            ProfileData?[] profiles = await Task.WhenAll(addresses.Select(ProfileService.ReadAsync));

            return [.. addresses.Select((address, index) => new BlockedAccount(address, profiles[index]))];
        }

        /// <summary> The name drawn for a blocked account, falling back to the readable head of its address. </summary>
        /// <param name="account"> The blocked account being drawn. </param>
        /// <returns> That account's display name, or the fallback built from its address. </returns>
        static string NameFor(BlockedAccount account)
            => string.IsNullOrWhiteSpace(account.Profile?.DisplayName)
                ? ProfileService.FallbackDisplayName(account.Address)
                : account.Profile!.DisplayName;

        /// <summary>
        /// The avatar drawn for a blocked account. An account with no stored profile still gets the emoji its address
        /// would have been given, so the row never opens on a blank circle.
        /// </summary>
        /// <param name="account"> The blocked account being drawn. </param>
        /// <returns> One emoji standing in for that account. </returns>
        static string AvatarFor(BlockedAccount account)
            => string.IsNullOrWhiteSpace(account.Profile?.Avatar)
                ? ProfileService.PickAvatar(account.Address)
                : account.Profile!.Avatar;

        /// <summary> True while this particular block is being lifted, which is what puts a throbber on its own button alone. </summary>
        /// <param name="address"> Address of the row being drawn. </param>
        /// <returns> True when that row's unblock is the one currently running. </returns>
        bool IsUnblocking(string address) => string.Equals(_unblockingAddress, address, StringComparison.Ordinal);

        /// <summary> Asks the owner to check their surroundings before anything is shown. </summary>
        void AskToRevealSecret()
        {
            _secretFailureMessage = null;
            _secretStage = SecretStage.Confirming;
        }

        /// <summary> Backs out of the reveal, leaving the section exactly as it was before the first press. </summary>
        void CancelReveal()
        {
            _secretFailureMessage = null;
            _secretStage = SecretStage.Hidden;
        }

        /// <summary>
        /// Puts the secret on screen. The characters are read from the unlocked account on this device — no request
        /// is made, because there is nowhere to make one to.
        /// </summary>
        void RevealSecret()
        {
            try
            {
                string secret = SessionService.ExportSecretText();

                if (secret.Length == 0)
                {
                    _secretFailureMessage = SecretUnavailableMessage;
                    Log($"{nameof(Settings)} was handed an empty secret while a session was open.", LogLevel.Warning);
                    return;
                }

                _secretText = secret;
                _secretFailureMessage = null;
                _secretStage = SecretStage.Revealed;
            }
            catch (Exception error)
            {
                _secretFailureMessage = SecretUnavailableMessage;
                Log($"{nameof(Settings)} could not read the account secret off this device.\n{error}", LogLevel.Error);
            }
        }

        /// <summary> Puts the secret away and drops the characters this page was holding. </summary>
        void HideSecret() => ForgetSecret();

        /// <summary> Clears the revealed characters and returns the section to its resting state. </summary>
        void ForgetSecret()
        {
            _secretText = string.Empty;
            _secretFailureMessage = null;
            _secretStage = SecretStage.Hidden;
        }

        /// <summary>
        /// Lifts one block. The list on screen is not patched here: the service announces the change and this page is
        /// subscribed to that announcement, so the row leaves on a fresh read.
        /// </summary>
        /// <param name="address"> Address whose block is being lifted. </param>
        /// <returns> A task that completes once the block is gone. </returns>
        async Task UnblockAsync(string address)
        {
            if (_unblockingAddress is not null) return;

            _unblockingAddress = address;

            try
            {
                await ModerationService.UnblockAsync(Account, address);
            }
            catch (Exception error)
            {
                Log($"{nameof(Settings)} could not lift the block on '{address}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                _unblockingAddress = null;
            }
        }

        /// <summary> Opens the sign-out confirmation, and puts any revealed secret away first so it cannot sit behind the sheet. </summary>
        void OpenSignOutConfirmation()
        {
            ForgetSecret();
            _isSignOutOpen = true;
        }

        /// <summary> Closes the sign-out confirmation, leaving a sign-out that is already running to finish. </summary>
        void CloseSignOutConfirmation()
        {
            if (_isSigningOut) return;

            _isSignOutOpen = false;
        }

        /// <summary>
        /// Forgets the secret on this device and returns to the welcome screen. The account is untouched — it reopens
        /// wherever that secret is typed back in.
        /// </summary>
        /// <returns> A task that completes once the session is closed. </returns>
        async Task SignOutAsync()
        {
            if (_isSigningOut) return;

            _isSigningOut = true;

            try
            {
                ForgetSecret();
                await SessionService.SignOutAsync();
                NavManager.NavigateTo(WelcomeRoute);
            }
            catch (Exception error)
            {
                Log($"{nameof(Settings)} could not sign out of this device.\n{error}", LogLevel.Error);
            }
            finally
            {
                _isSigningOut = false;
                _isSignOutOpen = false;

                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary> Returns to the owner's own profile, the screen this one is reached from. </summary>
        void GoBackToProfile() => NavManager.NavigateTo(ProfileRoute);
    }
}
