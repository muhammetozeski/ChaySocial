using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Services;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary> One line of a stranger-chat room, already opened on this device. </summary>
    /// <param name="Envelope"> The stored message. </param>
    /// <param name="Text"> Its words, or the stand-in shown when they could not be opened. </param>
    /// <param name="IsMine"> True when the reader wrote it. </param>
    /// <param name="CouldDecrypt"> True when the words above are real rather than a stand-in. </param>
    /// <param name="IsSenderVerified"> True when the signature matches the key the sender publishes. </param>
    public readonly record struct OpenedRoomMessage(
        MessageData Envelope,
        string Text,
        bool IsMine,
        bool CouldDecrypt,
        bool IsSenderVerified);

    /// <summary>
    /// Meeting somebody at random. The screen is a lobby until there is a room and a room afterwards, and it polls
    /// while it waits because there is nothing to push an update: pairing happens on somebody else's device.
    /// </summary>
    public partial class Strangers
    {
        /// <summary> Route this screen answers on. </summary>
        public const string StrangersRoute = "/strangers";

        /// <summary> Emoji over the invitation. </summary>
        const string LobbyEmoji = "🎲";

        /// <summary> The invitation's heading. </summary>
        const string LobbyHeadline = "Meet somebody";

        /// <summary> Line under it, which is also the promise the room has to keep. </summary>
        const string LobbyDescription =
            "You will be put in a room with one stranger. Nothing said in it is kept: when either of you walks out, "
            + "the whole conversation goes with the room.";

        /// <summary> Emoji on the button that joins the queue. </summary>
        const string StartEmoji = "🚪";

        /// <summary> Label on it. </summary>
        const string StartLabel = "Find somebody";

        /// <summary> Label on the button that gives up waiting. </summary>
        const string StopWaitingLabel = "Never mind";

        /// <summary> Line shown while waiting. </summary>
        const string WaitingLabel = "Looking for somebody who is also looking…";

        /// <summary> Shown when the reader gave up waiting. </summary>
        const string StoppedWaitingNotice = "Left the queue.";

        /// <summary> Shown when the other side walked out. </summary>
        const string PartnerLeftNotice = "They left, and the room went with them.";

        /// <summary> Shown when the reader walked out. </summary>
        const string YouLeftNotice = "You left the room.";

        /// <summary> Invitation in the room's composer. </summary>
        const string RoomComposerPlaceholder = "Say hello…";

        /// <summary> Label on the button that leaves a room. </summary>
        const string LeaveLabel = "Leave";

        /// <summary> Line under the partner's name, saying what kind of room this is. </summary>
        const string RoomNote = "A room that keeps nothing";

        /// <summary> Emoji on the placeholder shown in a room nobody has spoken in. </summary>
        const string QuietRoomEmoji = "👋";

        /// <summary> Headline of that placeholder. </summary>
        const string QuietRoomHeadline = "You found each other";

        /// <summary> Supporting line of it. </summary>
        const string QuietRoomDescription = "Neither of you knows the other. Say the first thing.";

        /// <summary> Shown in place of a message whose words could not be opened. </summary>
        const string UnreadableText = "Can't open this message";

        /// <summary> How often the lobby asks whether somebody has turned up. </summary>
        static readonly TimeSpan QueuePollInterval = TimeSpan.FromSeconds(3);

        /// <summary> How often an open room asks whether the other side is still in it. </summary>
        static readonly TimeSpan RoomPollInterval = TimeSpan.FromSeconds(4);

        /// <summary> Fully rounded corners on this screen's buttons, as a CSS length. </summary>
        static readonly string PillRadiusCss = $"{AppMeasures.Radius.Pill}px";

        /// <summary> Inside spacing of a leading button. </summary>
        static readonly string ActionPaddingCss = $"{AppMeasures.Space.Px12}px {AppMeasures.Space.Px24}px";

        /// <summary> Inside spacing of a quieter one. </summary>
        static readonly string QuietPaddingCss = $"{AppMeasures.Space.Px6}px {AppMeasures.Space.Px14}px";

        /// <summary> Hairline around those quiet buttons. </summary>
        static string QuietButtonBorder => $"{AppMeasures.Border.Thin}px solid {AppColors.BorderSoft.ToRgbaHex(true)}";

        /// <summary> Frosted bar the room's header is painted on. </summary>
        static string HeaderSurfaceStyle => AppStyles.BuildBarSurface(pinnedToBottom: false);

        /// <summary> Diameter of the partner's avatar. </summary>
        const int PartnerAvatarDiameterPx = AppMeasures.Size.Px44;

        /// <summary> The room the reader is in, or null while they are not in one. </summary>
        MatchRoomData? room;

        /// <summary> Profile of the other person, read once the room exists. </summary>
        ProfileData? partnerProfile;

        /// <summary> The room's messages, already opened on this device, oldest first. </summary>
        IReadOnlyList<OpenedRoomMessage> opened = [];

        /// <summary> True while standing in the queue. </summary>
        bool isWaiting;

        /// <summary> When the wait started, so the screen can say how long it has been. </summary>
        DateTimeOffset waitingSince;

        /// <summary> True while a message is being sealed and stored. </summary>
        bool isSending;

        /// <summary> True while the room is being left, which locks the leave button. </summary>
        bool isLeaving;

        /// <summary> What the reader has typed and not sent yet. </summary>
        string draftText = string.Empty;

        /// <summary> A line explaining what just happened, or null when nothing has. </summary>
        string? noticeMessage;

        /// <summary> Drives the polling; disposed when the screen goes away so it stops asking. </summary>
        PeriodicTimer? poller;

        /// <summary> Cancels the polling loop. </summary>
        CancellationTokenSource? pollingStopper;

        /// <summary> Browser tab title, which follows whichever of the two states the screen is in. </summary>
        string PageTitleText => room is null ? LobbyHeadline : PartnerName;

        /// <summary> The other person's chosen name, or the readable head of their address. </summary>
        string PartnerName => string.IsNullOrWhiteSpace(partnerProfile?.DisplayName)
            ? ProfileService.FallbackDisplayName(PartnerAddress)
            : partnerProfile.DisplayName;

        /// <summary> The other person's emoji. </summary>
        string PartnerAvatar => string.IsNullOrWhiteSpace(partnerProfile?.Avatar)
            ? ProfileService.PickAvatar(PartnerAddress)
            : partnerProfile.Avatar;

        /// <summary> The other person's address, or empty while there is no room. </summary>
        string PartnerAddress => room is null ? string.Empty : MatchService.PartnerOf(room, Account.Public.Address);

        /// <summary> How long the reader has been waiting, in the app's short form. </summary>
        string WaitedForLabel => RelativeTimeFormatter.Format(waitingSince.ToUnixTimeMilliseconds());

        /// <summary>
        /// The room's messages newest first, which is the order the thread is laid out in — it is drawn
        /// bottom-upwards so a long exchange opens on its newest line without anybody scrolling.
        /// </summary>
        IEnumerable<OpenedRoomMessage> NewestFirst => opened.Reverse();

        /// <summary>
        /// Picks up a room this account was already in — walking back onto this screen should not lose a
        /// conversation — and starts the polling loop.
        /// </summary>
        /// <returns> A task that completes once the screen knows which of its two states it is in. </returns>
        protected override async Task LoadAsync()
        {
            room = await MatchService.ReadOpenRoomAsync(Account.Public.Address);

            if (room is not null) await LoadRoomAsync();

            StartPolling();
        }

        /// <summary> Joins the queue and starts asking whether somebody has turned up. </summary>
        /// <returns> A task that completes once the ticket is in. </returns>
        async Task StartWaitingAsync()
        {
            if (isWaiting) return;

            noticeMessage = null;
            isWaiting = true;
            waitingSince = DateTimeOffset.UtcNow;

            await PollAsync();
        }

        /// <summary> Steps out of the queue. </summary>
        /// <returns> A task that completes once the ticket is gone. </returns>
        async Task StopWaitingAsync()
        {
            isWaiting = false;
            noticeMessage = StoppedWaitingNotice;

            await MatchService.LeaveQueueAsync(Account.Public);
        }

        /// <summary>
        /// Starts the loop that keeps the screen honest. There is nothing to push an update here: the pairing
        /// happens on somebody else's device, and so does their walking out, so this asks.
        /// </summary>
        void StartPolling()
        {
            pollingStopper?.Cancel();
            pollingStopper = new CancellationTokenSource();
            poller?.Dispose();
            poller = new PeriodicTimer(QueuePollInterval);

            _ = PollForeverAsync(poller, pollingStopper.Token);
        }

        /// <summary> Asks, over and over, until the screen goes away. </summary>
        /// <param name="timer"> The timer driving it. </param>
        /// <param name="cancellationToken"> Stops the loop when the screen is disposed. </param>
        /// <returns> A task that completes when the loop stops. </returns>
        async Task PollForeverAsync(PeriodicTimer timer, CancellationToken cancellationToken)
        {
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    // Nothing to ask about while sitting in the lobby without having joined the queue.
                    if (!isWaiting && room is null) continue;

                    await InvokeAsync(async () =>
                    {
                        await PollAsync();
                        StateHasChanged();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // The screen went away, which is the ordinary way this loop ends.
            }
            catch (Exception error)
            {
                Log($"{nameof(Strangers)} stopped polling.\n{error}", LogLevel.Error);
            }
        }

        /// <summary> One round of asking: has somebody turned up, or has the other side walked out. </summary>
        /// <returns> A task that completes once the screen matches what the store says. </returns>
        async Task PollAsync()
        {
            if (room is not null)
            {
                MatchRoomData? current = await MatchService.ReadRoomAsync(room.RoomId);

                if (current is null || current.IsClosed)
                {
                    // Whoever is still standing here clears the record away, so a closed room does not linger.
                    if (current is not null) await MatchService.ForgetRoomAsync(current);

                    noticeMessage = current?.ClosedByAddress == Account.Public.Address ? YouLeftNotice : PartnerLeftNotice;
                    room = null;
                    opened = [];
                    partnerProfile = null;
                    return;
                }

                await LoadRoomAsync();
                return;
            }

            if (!isWaiting) return;

            MatchRoomData? found = await MatchService.JoinOrPollAsync(Account.Public);
            if (found is null) return;

            room = found;
            isWaiting = false;
            noticeMessage = null;

            await LoadRoomAsync();
        }

        /// <summary> Reads the room's messages and opens each one this device holds a key for. </summary>
        /// <returns> A task that completes once the thread is current. </returns>
        async Task LoadRoomAsync()
        {
            if (room is null) return;

            partnerProfile ??= await ProfileService.ReadAsync(PartnerAddress);

            IReadOnlyList<MessageData> stored = await MatchService.ReadMessagesAsync(room);

            List<OpenedRoomMessage> lines = new(stored.Count);
            foreach (MessageData envelope in stored)
            {
                bool couldDecrypt = MessageService.TryDecrypt(Account, envelope, out string text);

                lines.Add(new OpenedRoomMessage(
                    envelope,
                    couldDecrypt ? text : UnreadableText,
                    envelope.SenderAddress == Account.Public.Address,
                    couldDecrypt,
                    MessageService.VerifySender(envelope, SenderProfileFor(envelope))));
            }

            opened = lines;
        }

        /// <summary> The profile behind one message's sender: the reader's own, or the stranger's. </summary>
        /// <param name="envelope"> The message being drawn. </param>
        /// <returns> That account's profile, or null when it could not be read. </returns>
        ProfileData? SenderProfileFor(MessageData envelope)
            => envelope.SenderAddress == Account.Public.Address ? SessionService.CurrentProfile : partnerProfile;

        /// <summary> Keeps what is half-typed on the screen across every redraw. </summary>
        /// <param name="text"> The field's new contents. </param>
        void HandleDraftChanged(string text) => draftText = text;

        /// <summary> Says the typed line in the room. </summary>
        /// <returns> A task that completes once it is stored. </returns>
        async Task SendAsync()
        {
            if (room is null || partnerProfile is null || isSending) return;
            if (string.IsNullOrWhiteSpace(draftText)) return;

            isSending = true;

            try
            {
                MessageData? sent = await MatchService.SendAsync(Account, room, partnerProfile, draftText);
                if (sent is null) return;

                draftText = string.Empty;
                await LoadRoomAsync();
            }
            catch (Exception error)
            {
                Log($"{nameof(Strangers)} could not send into '{room.RoomId}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                isSending = false;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary> Walks out, which ends the room for both sides and deletes what was said. </summary>
        /// <returns> A task that completes once the room is gone. </returns>
        async Task LeaveRoomAsync()
        {
            if (room is null || isLeaving) return;

            isLeaving = true;

            try
            {
                await MatchService.LeaveRoomAsync(room, Account.Public);

                noticeMessage = YouLeftNotice;
                room = null;
                opened = [];
                partnerProfile = null;
            }
            catch (Exception error)
            {
                Log($"{nameof(Strangers)} could not leave the room.\n{error}", LogLevel.Error);
            }
            finally
            {
                isLeaving = false;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary>
        /// Stops polling and steps out of the queue on the way off the screen. Leaving a ticket behind would pair
        /// somebody with a person who is no longer looking, which is worse than not pairing them at all.
        /// </summary>
        public override void Dispose()
        {
            pollingStopper?.Cancel();
            pollingStopper?.Dispose();
            poller?.Dispose();

            if (isWaiting) _ = MatchService.LeaveQueueAsync(Account.Public);

            base.Dispose();
        }
    }
}
