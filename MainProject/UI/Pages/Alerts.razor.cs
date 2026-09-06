using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// The screen behind the bell: every like, comment, follow and message aimed at the signed-in account, newest
    /// first. A notification only points at something, so opening a row marks it read and then jumps to the thing
    /// itself — the post, the account, or the conversation — rather than showing a second stored copy of it.
    /// </summary>
    public partial class Alerts
    {
        /// <summary> Route a like or comment alert opens; the post id is appended to it. </summary>
        const string PostRoutePrefix = "/post/";

        /// <summary> Route a follow alert opens; the follower's address is appended to it. </summary>
        const string ProfileRoutePrefix = "/profile/";

        /// <summary> Route a message alert opens; the sender's address is appended to it. </summary>
        const string MessagesRoutePrefix = "/messages/";

        /// <summary> Heading drawn at the top of the screen. </summary>
        const string PageHeadline = "Alerts";

        /// <summary> Text on the browser tab / window title. </summary>
        const string PageTitleText = "Alerts";

        /// <summary> Supporting line shown when every alert has already been opened. </summary>
        const string CaughtUpSubtitle = "You're all caught up ✨";

        /// <summary> Supporting line shown when exactly one alert is still unread, where a count would read oddly. </summary>
        const string SingleUnreadSubtitle = "1 new thing to see";

        /// <summary> Supporting line shown for two or more unread alerts; the placeholder takes the count. </summary>
        const string ManyUnreadSubtitleFormat = "{0} new things to see";

        /// <summary> Label on the header action that opens every unread alert at once. </summary>
        const string MarkAllReadLabel = "Mark all as read";

        /// <summary> Label on the button offered after a failed load. </summary>
        const string RetryLabel = "Try again";

        /// <summary> Screen-reader name of the dot marking a row nobody has opened yet. </summary>
        const string UnreadDotLabel = "Unread";

        /// <summary> Emoji on the placeholder shown when the account has never been notified of anything. </summary>
        const string EmptyStateEmoji = "🔔";

        /// <summary> Headline on that placeholder. </summary>
        const string EmptyStateHeadline = "Nothing new yet";

        /// <summary> Supporting line on that placeholder, saying what fills the list. </summary>
        const string EmptyStateDescription = "Chays, comments, new followers and messages all land right here.";

        /// <summary> Diameter of the actor's avatar in a row: large enough to carry the emoji beside two lines of text. </summary>
        const int RowAvatarDiameterPx = AppMeasures.Size.Px48;

        /// <summary> Milliseconds each row waits beyond the one above it, so the list fans in instead of appearing at once. </summary>
        const int ArrivalStaggerStepMs = 40;

        /// <summary>
        /// Rows that still get their own stagger step. Past this the delay is held flat, so a long list never ends
        /// with rows that sit blank for a noticeable beat.
        /// </summary>
        const int LastStaggeredRowIndex = 8;

        /// <summary> Emoji on the badge of a chay alert. </summary>
        const string LikeKindEmoji = "🍵";

        /// <summary> Emoji on the badge of a comment alert. </summary>
        const string CommentKindEmoji = "💬";

        /// <summary> Emoji on the badge of a follow alert. </summary>
        const string FollowKindEmoji = "🌱";

        /// <summary> Emoji on the badge of a message alert. </summary>
        const string MessageKindEmoji = "💌";

        /// <summary> Emoji on the badge of an alert about being named. </summary>
        const string MentionKindEmoji = "📣";

        /// <summary> Emoji used for a kind this screen was written before, so an unknown alert still draws a badge. </summary>
        const string UnknownKindEmoji = "🔔";

        /// <summary> Sentence completing "<c>&lt;name&gt; …</c>" for a chay alert. </summary>
        const string LikeSentence = "poured you a chay";

        /// <summary> Sentence completing "<c>&lt;name&gt; …</c>" for a comment alert. </summary>
        const string CommentSentence = "commented on your post";

        /// <summary> Sentence completing "<c>&lt;name&gt; …</c>" for a follow alert. </summary>
        const string FollowSentence = "started following you";

        /// <summary> Sentence completing "<c>&lt;name&gt; …</c>" for a message alert. </summary>
        const string MessageSentence = "sent you a message";

        /// <summary> Sentence completing "<c>&lt;name&gt; …</c>" for an alert about being named. </summary>
        const string MentionSentence = "mentioned you";

        /// <summary> Emoji on the badge of an alert about somebody vouching. </summary>
        const string VouchKindEmoji = "✍️";

        /// <summary>
        /// Sentence completing "<c>&lt;name&gt; …</c>" for a vouch. What they wrote is deliberately not here: the
        /// name is theirs to choose and an alert line is not the place for one account to write into another's.
        /// </summary>
        const string VouchSentence = "put their name behind your account";

        /// <summary> Sentence used for a kind this screen was written before — true of any alert, whatever it turns out to be. </summary>
        const string UnknownSentence = "interacted with you";

        /// <summary> Modifier class on a row whose recipient has already opened it. </summary>
        const string ReadRowClass = "alert-row--read";

        /// <summary> Modifier class on a row nobody has opened yet. </summary>
        const string UnreadRowClass = "alert-row--unread";

        /// <summary> Route the whole postbox lives at, for a letter alert whose seal will not open. </summary>
        const string MessagesRoute = "/messages";

        /// <summary> Drawn in place of an avatar when an alert's seal will not open — the same padlock a letter that cannot be read gets. </summary>
        const string UnreadableEmoji = "🔏";

        /// <summary> Name drawn for that alert. It is the truth: somebody wrote, and this device cannot say who. </summary>
        const string UnreadableName = "Someone";

        /// <summary>
        /// What each sealed alert was carrying, keyed by its id. Opened once per load rather than per render,
        /// because opening one is a decapsulation and a row redraws far more often than it is fetched.
        /// </summary>
        IReadOnlyDictionary<string, SealedAlertDetail> _openedDetails = new Dictionary<string, SealedAlertDetail>();

        /// <summary> Fully rounded corners on the header action, so it reads as a soft pill beside the heading rather than a second panel. </summary>
        static string MarkAllButtonRadius => $"{AppMeasures.Radius.Pill}px";

        /// <summary> Padding inside the header action: enough to press comfortably without the pill outgrowing the heading it sits next to. </summary>
        static string MarkAllButtonPadding => $"{AppMeasures.Space.Px8}px {AppMeasures.Space.Px16}px";

        /// <summary> Hairline around the header action, matching every other glass edge and following the active theme rather than freezing one. </summary>
        static string MarkAllButtonBorder => $"{AppMeasures.Border.Thin}px solid {AppColors.GlassBorderDefault.ToRgbaHex(true)}";

        /// <summary> Fully rounded corners on the button offered after a failed load, matching the header action. </summary>
        static string RetryButtonRadius => $"{AppMeasures.Radius.Pill}px";

        /// <summary> Padding inside that retry button, which sits alone in a card and can afford to be larger than the header pill. </summary>
        static string RetryButtonPadding => $"{AppMeasures.Space.Px12}px {AppMeasures.Space.Px24}px";

        /// <summary> This account's alerts, newest first, as of the last load. </summary>
        IReadOnlyList<NotificationData> _alerts = [];

        /// <summary>
        /// Profiles of everyone who appears as an actor in <see cref="_alerts"/>, keyed by address. Fetched once per
        /// load so a list where the same person acted ten times still costs one profile read.
        /// </summary>
        IReadOnlyDictionary<string, ProfileData> _actorProfiles = new Dictionary<string, ProfileData>();

        /// <summary> True while the header action is clearing the unread alerts, so it cannot be pressed twice. </summary>
        bool _isMarkingAllRead;

        /// <summary> Reloads whenever an alert is written or opened, and whenever the signed-in account changes. </summary>
        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.NotificationsChanged,
            MainEvents.Names.SessionChanged
        ];

        /// <summary> How many of the loaded alerts the recipient has not opened yet. </summary>
        int UnreadCount => _alerts.Count(alert => !alert.IsRead);

        /// <summary> The line under the heading, which counts what is waiting or says there is nothing left. </summary>
        string SubtitleText => UnreadCount switch
        {
            0 => CaughtUpSubtitle,
            1 => SingleUnreadSubtitle,
            _ => string.Format(ManyUnreadSubtitleFormat, UnreadCount)
        };

        /// <summary> Reads this account's alerts, opens whatever they kept sealed, and fetches the actors' profiles. </summary>
        protected override async Task LoadAsync()
        {
            _alerts = await NotificationService.ReadForAsync(SessionService.CurrentAddress);
            _openedDetails = OpenSealedDetails(_alerts);
            _actorProfiles = await ReadActorProfilesAsync(_alerts.Select(ActorAddressOf));
        }

        /// <summary> Opens every sealed alert this account can open, and quietly leaves the rest closed. </summary>
        /// <param name="alerts"> The alerts about to be drawn. </param>
        /// <returns> What each one was carrying, keyed by its id. </returns>
        IReadOnlyDictionary<string, SealedAlertDetail> OpenSealedDetails(IReadOnlyList<NotificationData> alerts)
        {
            Dictionary<string, SealedAlertDetail> opened = [];

            foreach (NotificationData alert in alerts)
            {
                if (alert.IsSealed && NotificationService.TryOpenSealed(Account, alert, out SealedAlertDetail detail))
                {
                    opened[alert.NotificationId] = detail;
                }
            }

            return opened;
        }

        /// <summary>
        /// Fetches one profile per distinct actor in a page of alerts, so a row can draw a name and an avatar
        /// without re-reading the same account for every alert it caused.
        /// </summary>
        /// <param name="addresses"> Addresses of everyone who acted, sealed alerts already opened. </param>
        /// <returns> The profiles that exist, keyed by address; actors who never published one are simply absent. </returns>
        static async Task<IReadOnlyDictionary<string, ProfileData>> ReadActorProfilesAsync(IEnumerable<string> addresses)
        {
            Dictionary<string, ProfileData> profiles = [];

            // An alert whose seal would not open leaves no address at all, and asking the store for the profile of
            // nobody is a read that can only come back empty.
            foreach (string address in addresses.Where(address => address.Length > 0).Distinct())
            {
                ProfileData? profile = await ProfileService.ReadAsync(address);
                if (profile is not null) profiles[address] = profile;
            }

            return profiles;
        }

        /// <summary> Who caused an alert: the address it names, or the one its seal was hiding. </summary>
        /// <param name="alert"> The alert being drawn. </param>
        /// <returns> The actor's address, or empty when the alert is sealed and this device could not open it. </returns>
        string ActorAddressOf(NotificationData alert)
            => alert.IsSealed
                ? (_openedDetails.TryGetValue(alert.NotificationId, out SealedAlertDetail detail) ? detail.ActorAddress : string.Empty)
                : alert.ActorAddress;

        /// <summary> What an alert points at: the id it names, or the one its seal was hiding. </summary>
        /// <param name="alert"> The alert being drawn. </param>
        /// <returns> The target id, or empty when the alert is sealed and this device could not open it. </returns>
        string TargetIdOf(NotificationData alert)
            => alert.IsSealed
                ? (_openedDetails.TryGetValue(alert.NotificationId, out SealedAlertDetail detail) ? detail.TargetId : string.Empty)
                : alert.TargetId;

        /// <summary>
        /// Opens an alert: marks it read, then sends the reader to whatever it points at. Marking read first means
        /// the bell's count is already correct by the time the destination draws.
        /// </summary>
        /// <param name="alert"> The row the reader pressed. </param>
        async Task OpenAsync(NotificationData alert)
        {
            await NotificationService.MarkReadAsync(alert);
            NavManager.NavigateTo(BuildDestination(alert));
        }

        /// <summary>
        /// Opens every unread alert at once. Does nothing while a previous clear is still running or when there is
        /// nothing unread, so the header action cannot fire twice against the same list.
        /// </summary>
        async Task MarkEverythingReadAsync()
        {
            if (_isMarkingAllRead || UnreadCount == 0) return;

            _isMarkingAllRead = true;

            try
            {
                await NotificationService.MarkAllReadAsync(SessionService.CurrentAddress);
            }
            catch (Exception error)
            {
                Log($"{nameof(Alerts)} could not mark every alert read.\n{error}", LogLevel.Error);
            }
            finally
            {
                _isMarkingAllRead = false;
            }
        }

        /// <summary>
        /// Works out where an alert leads. A like or a comment leads to the post it names; when that alert somehow
        /// carries no post id there is nothing to open, so it falls back to the account that caused it, which is
        /// always known.
        /// </summary>
        /// <param name="alert"> The alert that was opened. </param>
        /// <returns> The route to navigate to. </returns>
        string BuildDestination(NotificationData alert)
        {
            string actorAddress = ActorAddressOf(alert);
            string targetId = TargetIdOf(alert);

            // An alert this device cannot open names nobody, so there is no conversation and no profile to jump to.
            // The postbox is the honest destination: the letter is in there somewhere.
            if (actorAddress.Length == 0) return MessagesRoute;

            string actorRoute = ProfileRoutePrefix + actorAddress;

            return alert.Kind switch
            {
                NotificationKind.Like or NotificationKind.Comment or NotificationKind.Mention =>
                    targetId.Length == 0 ? actorRoute : PostRoutePrefix + targetId,
                NotificationKind.Message => MessagesRoutePrefix + actorAddress,
                _ => actorRoute
            };
        }

        /// <summary> Puts what the actor did into words, to sit after their name. </summary>
        /// <param name="kind"> What the actor did. </param>
        /// <returns> The sentence completing the row, e.g. <c>liked your post</c>. </returns>
        static string DescribeAction(NotificationKind kind) => kind switch
        {
            NotificationKind.Like => LikeSentence,
            NotificationKind.Comment => CommentSentence,
            NotificationKind.Follow => FollowSentence,
            NotificationKind.Message => MessageSentence,
            NotificationKind.Mention => MentionSentence,
            NotificationKind.Vouch => VouchSentence,
            _ => UnknownSentence
        };

        /// <summary> Picks the little emoji badge that sits on the corner of the actor's avatar. </summary>
        /// <param name="kind"> What the actor did. </param>
        /// <returns> One emoji standing for that kind of alert. </returns>
        static string DescribeKindEmoji(NotificationKind kind) => kind switch
        {
            NotificationKind.Like => LikeKindEmoji,
            NotificationKind.Comment => CommentKindEmoji,
            NotificationKind.Follow => FollowKindEmoji,
            NotificationKind.Message => MessageKindEmoji,
            NotificationKind.Mention => MentionKindEmoji,
            NotificationKind.Vouch => VouchKindEmoji,
            _ => UnknownKindEmoji
        };

        /// <summary> The name drawn for an alert's actor, falling back to the readable head of their address. </summary>
        /// <param name="alert"> The alert being drawn. </param>
        /// <returns> The actor's display name, or the fallback name built from their address. </returns>
        string ActorNameFor(NotificationData alert)
        {
            string address = ActorAddressOf(alert);
            if (address.Length == 0) return UnreadableName;

            return _actorProfiles.TryGetValue(address, out ProfileData? profile) && !string.IsNullOrWhiteSpace(profile.DisplayName)
                ? profile.DisplayName
                : ProfileService.FallbackDisplayName(address);
        }

        /// <summary>
        /// The emoji drawn for an alert's actor. An actor with no stored profile still gets the same emoji their
        /// account would have been given, because it is derived from the address.
        /// </summary>
        /// <param name="alert"> The alert being drawn. </param>
        /// <returns> The actor's avatar emoji. </returns>
        string ActorAvatarFor(NotificationData alert)
        {
            string address = ActorAddressOf(alert);
            if (address.Length == 0) return UnreadableEmoji;

            return _actorProfiles.TryGetValue(address, out ProfileData? profile) && !string.IsNullOrWhiteSpace(profile.Avatar)
                ? profile.Avatar
                : ProfileService.PickAvatar(address);
        }

        /// <summary> The row's share of the staggered entrance, as an inline style the CSS animation reads. </summary>
        /// <param name="rowIndex"> Position of the row in the list, counted from zero. </param>
        /// <returns> An <c>animation-delay</c> declaration for that row. </returns>
        static string BuildArrivalDelayStyle(int rowIndex)
            => $"animation-delay:{Math.Min(rowIndex, LastStaggeredRowIndex) * ArrivalStaggerStepMs}ms;";
    }
}
