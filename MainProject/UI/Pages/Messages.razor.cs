using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;
using Microsoft.AspNetCore.Components;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// Direct messages, on one component answering two routes: the postbox listing every conversation this account
    /// takes part in, and one conversation opened on its own. Nothing on the postbox is decrypted — a row shows who
    /// wrote and when, never a word of what they wrote — because opening every thread's newest letter only to shrink
    /// it into a preview would spend the whole inbox's decryption work on text nobody asked to read yet. Letters are
    /// turned back into words only inside the conversation the reader actually opened.
    /// </summary>
    public partial class Messages
    {
        /// <summary>
        /// Route one conversation lives at. The parameter segment is spelled from <see cref="Address"/> itself, so
        /// renaming the property moves the route with it rather than silently breaking every link into this page.
        /// </summary>
        public const string ConversationRoute = NavigationConstants.Messages.Link + "/{" + nameof(Address) + "}";

        /// <summary> Builds the address that opens one account's conversation. </summary>
        /// <param name="address"> Account to open the conversation with. </param>
        /// <returns> A path another page can hand to its navigation manager. </returns>
        public static string LinkTo(string address)
            => $"{NavigationConstants.Messages.Link}/{Uri.EscapeDataString(address)}";

        /// <summary>
        /// Account whose conversation is open, taken from the route. Empty on the postbox route, which is what the
        /// whole page switches on.
        /// </summary>
        [Parameter]
        public string Address
        {
            get => routeAddress;
            // Blazor assigns null to an unmatched route parameter, overwriting any field initializer, so the
            // postbox route would otherwise leave this null and every read of it would throw.
            set => routeAddress = value ?? string.Empty;
        }

        /// <summary> Backing value for <see cref="Address"/>, never null so the rest of the page can read it plainly. </summary>
        string routeAddress = string.Empty;

        /// <summary>
        /// One row of the postbox: a conversation, plus the profile of whoever is on the other side of it. The
        /// profile is optional because an account may have written before it ever published one, and a row that
        /// cannot draw a name still has an address to fall back on.
        /// </summary>
        /// <param name="Summary"> The conversation this row stands for, newest letter included but never opened. </param>
        /// <param name="OtherProfile"> Profile of the other participant, or null when that account published none. </param>
        sealed record InboxRow(ConversationSummary Summary, ProfileData? OtherProfile)
        {
            /// <summary> Address of the account on the other side of this conversation. </summary>
            public string Address => Summary.OtherAddress;

            /// <summary> Name drawn on the row, falling back to the readable head of the address. </summary>
            public string Name => NameFor(OtherProfile, Address);

            /// <summary> Emoji drawn on the row, falling back to the one that account's address would have been given. </summary>
            public string Avatar => AvatarFor(OtherProfile, Address);

            /// <summary> How long ago the newest letter of this conversation was sent, in the app's short form. </summary>
            public string Ago => RelativeTimeFormatter.Format(Summary.NewestAtUnixMs);
        }

        /// <summary>
        /// One stored letter after this device has tried to open it. Both attempts — decrypting the body and checking
        /// the sender's signature — are made once while the conversation loads, so a repaint never redoes the
        /// cryptography and a bubble only draws what it is handed.
        /// </summary>
        /// <param name="Envelope"> The stored message, still carrying its ciphertext. </param>
        /// <param name="Text"> The decrypted body, or an empty string when this device could not open it. </param>
        /// <param name="CouldDecrypt"> True when the ciphertext opened on this device. </param>
        /// <param name="IsSenderVerified"> True when the signature checked out against the sender's published key. </param>
        /// <param name="IsMine"> True when the signed-in account sent it, which puts the bubble on the right. </param>
        /// <param name="RevealedMedia"> Media recovered from a vanishing letter this reader opened; empty for every other letter. </param>
        sealed record OpenedMessage(
            MessageData Envelope,
            string Text,
            bool CouldDecrypt,
            bool IsSenderVerified,
            bool IsMine)
        {
            public IReadOnlyList<RevealedMedia> RevealedMedia { get; init; } = [];
        }

        /// <summary> Text on the browser tab while the postbox is showing. </summary>
        const string PageTitleText = "Messages";

        /// <summary> Heading at the top of the postbox. </summary>
        const string InboxHeadline = "Letters";

        /// <summary> Emoji beside that heading. </summary>
        const string InboxEmoji = "💌";

        /// <summary> Line under the heading when this account has never exchanged a letter. </summary>
        const string NoConversationsSubtitle = "Nothing in the postbox yet";

        /// <summary> Line under the heading for exactly one conversation, where a count would read oddly. </summary>
        const string SingleConversationSubtitle = "1 conversation";

        /// <summary> Line under the heading for two or more conversations; the placeholder takes the count. </summary>
        const string ManyConversationsSubtitleFormat = "{0} conversations";

        /// <summary> Line under the throbber while the postbox is being read. </summary>
        const string InboxLoadingLabel = "Sorting the post…";

        /// <summary> Line under the throbber while one conversation is being read and opened. </summary>
        const string ConversationLoadingLabel = "Unsealing your letters…";

        /// <summary> Emoji on the placeholder shown when a load failed. </summary>
        const string LoadFailureEmoji = "🌧️";

        /// <summary> Headline of that placeholder; its supporting line is the message the page base supplies. </summary>
        const string LoadFailureHeadline = "The post didn't get through";

        /// <summary> Label on the button that runs a failed load again. </summary>
        const string RetryLabel = "Try again";

        /// <summary> Emoji on the placeholder shown when this account has no conversations at all. </summary>
        const string EmptyInboxEmoji = "📭";

        /// <summary> Headline of that placeholder. </summary>
        const string EmptyInboxHeadline = "No letters yet";

        /// <summary> Supporting line of that placeholder, which says both where to start and what sending costs in privacy. </summary>
        const string EmptyInboxDescription =
            "Find somebody in Search and write to them. What you send is sealed on this device and opens only on theirs.";

        /// <summary> Label on the button that takes the reader from the empty postbox to Search. </summary>
        const string GoToSearchLabel = "Find someone to write to";

        /// <summary> Emoji on the placeholder shown when two accounts have never written to each other. </summary>
        const string EmptyConversationEmoji = "🕊️";

        /// <summary> Headline of that placeholder. </summary>
        const string EmptyConversationHeadline = "Nothing between you yet";

        /// <summary> Supporting line of that placeholder, which is also the invitation to use the composer below it. </summary>
        const string EmptyConversationDescription = "Say hello. Only the two of you will ever be able to read it.";

        /// <summary> Emoji beside the line shown in place of the composer when nothing can be encrypted to this account. </summary>
        const string UnwritableEmoji = "🔒";

        /// <summary>
        /// Shown in place of the composer when the other account's profile could not be read. Sending needs the key
        /// that profile publishes, so there is nothing to seal a letter to until it arrives.
        /// </summary>
        const string UnwritableNotice = "This account hasn't published a profile here yet, so there's no key to seal a letter to.";

        /// <summary> Spoken name of a postbox row; the placeholder takes the other participant's name. </summary>
        const string OpenConversationHintFormat = "Open your conversation with {0}";

        /// <summary> Hover text on the back action, so the arrow says where it leads before it is pressed. </summary>
        const string BackToInboxHint = "Back to your letters";

        /// <summary> Diameter of the avatar on a postbox row: large enough to carry the emoji beside a name and a time. </summary>
        const int InboxRowAvatarDiameterPx = AppMeasures.Size.Px48;

        /// <summary> Diameter of the avatar in the conversation header, sized down so the bar stays a bar. </summary>
        const int ConversationAvatarDiameterPx = AppMeasures.Size.Px40;

        /// <summary> Diameter of the small throbber that sits in the conversation header while an open thread refreshes. </summary>
        const int HeaderSpinnerDiameterPx = AppMeasures.Size.Px20;

        /// <summary> Ring thickness of that throbber, thinned from the app default so a disc this small still reads as a ring. </summary>
        const int HeaderSpinnerBorderPx = AppMeasures.Border.Medium;

        /// <summary> Milliseconds each postbox row waits beyond the one above it, so the list fans in instead of appearing at once. </summary>
        const int ArrivalStaggerStepMs = 40;

        /// <summary>
        /// Rows that still get their own stagger step. Past this the delay is held flat, so a long postbox never ends
        /// with rows sitting blank for a noticeable beat.
        /// </summary>
        const int LastStaggeredRowIndex = 8;

        /// <summary> Fully rounded corners on the buttons offered under a placeholder. </summary>
        static readonly string ActionButtonRadiusCss = $"{AppMeasures.Radius.Pill}px";

        /// <summary> Padding inside those buttons: wide across and shallow down, so they read as pills. </summary>
        static readonly string ActionButtonPaddingCss = $"{AppMeasures.Space.Px12}px {AppMeasures.Space.Px24}px";

        /// <summary> Reloads on a letter sent from anywhere, and on a sign-in or sign-out that changes whose post this is. </summary>
        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.MessagesChanged,
            MainEvents.Names.SessionChanged
        ];

        /// <summary> Conversations of the signed-in account, newest first. Empty while a conversation is open. </summary>
        IReadOnlyList<InboxRow> inbox = [];

        /// <summary> The open conversation's letters, oldest first, each already decrypted and checked. </summary>
        IReadOnlyList<OpenedMessage> conversation = [];

        /// <summary>
        /// The same letters newest first. The thread is laid out bottom-upwards, which is what opens a long
        /// conversation on its newest letter rather than its oldest without asking the browser to scroll anywhere,
        /// and that layout reads its children in this order.
        /// </summary>
        IEnumerable<OpenedMessage> NewestFirst => conversation.Reverse();

        /// <summary> Profile of the account the open conversation is with, or null when it could not be read. </summary>
        ProfileData? otherProfile;

        /// <summary> What the reader has typed into the composer and not sent yet. </summary>
        string draftText = string.Empty;

        /// <summary> True while a letter is being sealed and stored, which locks the composer. </summary>
        bool isSending;

        /// <summary> True while the next letter should be sent to be read exactly once. </summary>
        bool isDraftVanishing;

        /// <summary> Media already uploaded for the letter being written but not sent yet. </summary>
        IReadOnlyList<MediaAttachment> draftAttachments = [];

        /// <summary> Id of the letter being replied to, or empty while a plain letter is being written. </summary>
        string replyingToMessageId = string.Empty;

        /// <summary> Longest quoted line drawn on a bubble or in the composer; past this the line is cut and ellipsised. </summary>
        const int QuotedSummaryLength = 90;

        /// <summary> Stands in for a quoted letter whose words this device cannot show — a vanishing one, or one meant for somebody else. </summary>
        const string UnreadableQuoteSummary = "a message that cannot be shown";

        /// <summary>
        /// Vanishing letters this reader has opened, kept only for as long as the page is on screen. The server no
        /// longer holds them, so leaving the conversation is what finally loses them.
        /// </summary>
        readonly Dictionary<string, OpenedMessage> openedVanishing = [];

        /// <summary> Id of the vanishing letter currently being fetched, or null when none is. </summary>
        string? openingMessageId;

        /// <summary> Shown in place of a vanishing letter that had already been opened, or could not be decrypted. </summary>
        const string VanishingAlreadyGoneText = "This message was already opened, and it is gone.";

        /// <summary> Route value the last load ran for; a different one means the reader moved between the postbox and a conversation. </summary>
        string loadedAddress = string.Empty;

        /// <summary> True once a load has finished, so a later reload refreshes what is drawn instead of blanking it. </summary>
        bool hasContent;

        /// <summary> True while the route names an account, which is what puts one conversation on screen instead of the postbox. </summary>
        bool IsConversation => Address.Length > 0;

        /// <summary> True while a load runs with nothing on screen worth keeping, which is what the full-page throbber answers. </summary>
        bool IsFirstLoad => IsLoading && !hasContent;

        /// <summary> True while a reload refreshes what is already drawn; only the header throbber reacts to it. </summary>
        bool IsRefreshing => IsLoading && hasContent;

        /// <summary> Text on the browser tab: the postbox's own name, or whoever the open conversation is with. </summary>
        string BrowserTitle => IsConversation ? OtherName : PageTitleText;

        /// <summary> Line under the postbox heading, counting what is waiting. </summary>
        string InboxSubtitle => inbox.Count switch
        {
            0 => NoConversationsSubtitle,
            1 => SingleConversationSubtitle,
            _ => string.Format(ManyConversationsSubtitleFormat, inbox.Count)
        };

        /// <summary> Name of the account the open conversation is with. </summary>
        string OtherName => NameFor(otherProfile, Address);

        /// <summary> Emoji of the account the open conversation is with. </summary>
        string OtherAvatar => AvatarFor(otherProfile, Address);

        /// <summary>
        /// True when a letter can actually be sealed to this account. Encrypting needs the key the recipient's
        /// profile publishes, so a conversation whose profile is missing is readable but not writable.
        /// </summary>
        bool CanWrite => otherProfile is not null;

        /// <summary> Frosted bar the conversation header is painted on, so letters scroll under glass rather than under nothing. </summary>
        static string HeaderSurfaceStyle => AppStyles.BuildBarSurface(pinnedToBottom: false);

        /// <summary>
        /// Loads whatever the route asks for when the reader moves between the postbox and a conversation without
        /// leaving the page. The first pass is already covered by the page base, which is why a route value matching
        /// the one just loaded is left alone.
        /// </summary>
        /// <returns> A task that completes once the new route has been read, or immediately when nothing changed. </returns>
        protected override async Task OnParametersSetAsync()
        {
            await base.OnParametersSetAsync();

            if (!SessionService.IsSignedIn || loadedAddress == Address) return;

            await ReloadAsync();
        }

        /// <summary> Reads the postbox, or the one conversation the route names. </summary>
        /// <returns> A task that completes once the page has everything it draws. </returns>
        protected override async Task LoadAsync()
        {
            if (!SessionService.IsSignedIn)
            {
                NavManager.NavigateTo(WelcomeRoute);
                return;
            }

            // Moving between the postbox and a conversation shows a different screen, so what the previous one left
            // behind is dropped rather than sitting under the new screen's throbber.
            if (loadedAddress != Address) Forget();

            loadedAddress = Address;

            if (IsConversation) await LoadConversationAsync();
            else await LoadInboxAsync();

            hasContent = true;
        }

        /// <summary>
        /// Reads every conversation this account takes part in and the profile behind each other participant. The
        /// profiles are read together rather than one after another, because no row's profile depends on another's.
        /// </summary>
        /// <returns> A task that completes once the postbox is ready to draw. </returns>
        async Task LoadInboxAsync()
        {
            IReadOnlyList<ConversationSummary> summaries = await MessageService.ReadInboxAsync(Account.Public.Address);

            if (summaries.Count == 0)
            {
                inbox = [];
                return;
            }

            ProfileData?[] profiles = await Task.WhenAll(summaries.Select(summary => ProfileService.ReadAsync(summary.OtherAddress)));

            InboxRow[] rows = new InboxRow[summaries.Count];
            for (int index = 0; index < summaries.Count; index++)
            {
                rows[index] = new InboxRow(summaries[index], profiles[index]);
            }

            inbox = rows;
        }

        /// <summary>
        /// Reads one conversation and opens every letter in it: the other participant's profile first, because both
        /// halves of a letter — whether it decrypts and whether its signature holds — need a key that profile carries.
        /// </summary>
        /// <returns> A task that completes once every letter has been opened and checked. </returns>
        async Task LoadConversationAsync()
        {
            Task<ProfileData?> profileRead = ProfileService.ReadAsync(Address);
            Task<IReadOnlyList<MessageData>> lettersRead = MessageService.ReadConversationAsync(
                MessageData.ConversationIdFor(Account.Public.Address, Address));

            await Task.WhenAll(profileRead, lettersRead);

            otherProfile = await profileRead;

            List<OpenedMessage> letters = [.. (await lettersRead).Select(Open)];

            // A vanishing letter is deleted the moment it is opened, so a later reload no longer finds it. Keeping
            // the ones this reader already opened means an unrelated refresh — a new letter arriving, say — does not
            // wipe a message off the screen while they are still reading it.
            letters.AddRange(openedVanishing.Values.Where(opened =>
                letters.All(letter => letter.Envelope.MessageId != opened.Envelope.MessageId)));

            conversation = [.. letters.OrderBy(letter => letter.Envelope.CreatedAtUnixMs)];
        }

        /// <summary>
        /// Turns one stored envelope into what a bubble draws. A letter this account did not receive stays sealed —
        /// the secret was encapsulated to the recipient's key, so the sender's own copy of what they wrote is
        /// unreadable to them once it has left — and that is drawn as a padlocked line rather than as a failure.
        /// </summary>
        /// <param name="envelope"> The stored letter. </param>
        /// <returns> The letter with its body and its seal already worked out. </returns>
        OpenedMessage Open(MessageData envelope)
        {
            bool isMine = envelope.SenderAddress == Account.Public.Address;
            ProfileData? senderProfile = isMine ? Profile : otherProfile;

            // Every field below came out of storage, so it is exactly the kind of input that carries a wrong-length
            // key or a truncated envelope. Both calls reject the ordinary failures themselves; this catch is for the
            // malformed ones they throw on, so one unreadable letter is drawn as unreadable instead of emptying the
            // whole conversation.
            try
            {
                bool couldDecrypt = MessageService.TryDecrypt(Account, envelope, out string text);
                bool isSenderVerified = MessageService.VerifySender(envelope, senderProfile);

                return new OpenedMessage(envelope, text, couldDecrypt, isSenderVerified, isMine);
            }
            catch (Exception error)
            {
                Log($"Letter '{envelope.MessageId}' could not be opened and is drawn sealed.\n{error}", LogLevel.Warning);
                return new OpenedMessage(envelope, string.Empty, false, false, isMine);
            }
        }

        /// <summary> Drops what the previous screen left behind, so the next one starts from nothing. </summary>
        void Forget()
        {
            inbox = [];
            conversation = [];
            otherProfile = null;
            draftText = string.Empty;
            hasContent = false;
        }

        /// <summary> Keeps the composer's text on the page, so what is half-written survives every redraw. </summary>
        /// <param name="text"> The field's new contents. </param>
        void HandleDraftChanged(string text) => draftText = text;

        /// <summary> Keeps the read-once choice on the page, so it survives a redraw and resets after a send. </summary>
        /// <param name="isVanishing"> True when the next letter should be readable exactly once. </param>
        void HandleDraftVanishingChanged(bool isVanishing) => isDraftVanishing = isVanishing;

        /// <summary> Keeps the composer's attached media on the page alongside its text. </summary>
        /// <param name="attachments"> The media currently attached. </param>
        void HandleDraftAttachmentsChanged(IReadOnlyList<MediaAttachment> attachments) => draftAttachments = attachments;

        /// <summary> Points the composer at a letter to answer. </summary>
        /// <param name="letter"> Letter being replied to. </param>
        void StartReplying(OpenedMessage letter) => replyingToMessageId = letter.Envelope.MessageId;

        /// <summary> Drops the reply so the composer writes a plain letter again. </summary>
        void StopReplying() => replyingToMessageId = string.Empty;

        /// <summary> The line shown in the composer for the letter being answered, or null when none is. </summary>
        string? ReplyingToSummary => replyingToMessageId.Length == 0 ? null : SummaryOf(replyingToMessageId);

        /// <summary> The line a bubble shows for the letter it answers, or null when it answers nothing. </summary>
        /// <param name="letter"> The letter being drawn. </param>
        /// <returns> The quoted line, or null. </returns>
        string? QuotedSummaryFor(OpenedMessage letter)
            => letter.Envelope.IsQuoting ? SummaryOf(letter.Envelope.QuotedMessageId) : null;

        /// <summary>
        /// Shortens one letter of this conversation into the single line a quote shows. The words come from the
        /// already-decrypted conversation rather than from the server, so quoting costs no second fetch and puts
        /// no second copy of the text anywhere.
        /// </summary>
        /// <param name="messageId"> Letter to summarise. </param>
        /// <returns> The line to draw. </returns>
        string SummaryOf(string messageId)
        {
            OpenedMessage? quoted = conversation.FirstOrDefault(letter => letter.Envelope.MessageId == messageId);

            if (quoted is null) return UnreadableQuoteSummary;

            string text = Current(quoted).Text;
            if (text.Length == 0) return UnreadableQuoteSummary;

            return text.Length <= QuotedSummaryLength ? text : text[..QuotedSummaryLength] + "…";
        }

        /// <summary>
        /// Opens a vanishing letter, which destroys it on the server as it is read. The body is kept only on this
        /// page, in memory: there is nowhere left to fetch it from, so leaving the conversation loses it — which
        /// is what the sender asked for.
        /// </summary>
        /// <param name="letter"> The vanishing letter the reader tapped. </param>
        async Task OpenVanishingAsync(OpenedMessage letter)
        {
            if (letter.IsMine || openingMessageId is not null || openedVanishing.ContainsKey(letter.Envelope.MessageId)) return;

            openingMessageId = letter.Envelope.MessageId;

            try
            {
                RevealedMessage? revealed = await MessageService.ConsumeVanishingAsync(Account, letter.Envelope);

                // The load marked this letter unreadable because its body was not in the document. Now that the
                // body is in hand, the bubble has to be told it may draw text instead of the padlocked line.
                openedVanishing[letter.Envelope.MessageId] = letter with
                {
                    Text = revealed?.Text ?? VanishingAlreadyGoneText,
                    CouldDecrypt = true,
                    RevealedMedia = revealed?.Media ?? []
                };
            }
            catch (Exception error)
            {
                openedVanishing[letter.Envelope.MessageId] = letter with { Text = VanishingAlreadyGoneText, CouldDecrypt = true };
                Log($"{nameof(Messages)} could not open a vanishing letter.\n{error}", LogLevel.Error);
            }
            finally
            {
                openingMessageId = null;
            }
        }

        /// <summary> True once this reader has opened that vanishing letter on this page. </summary>
        /// <param name="letter"> The letter being drawn. </param>
        /// <returns> True when its body is already in hand. </returns>
        bool IsOpened(OpenedMessage letter) => openedVanishing.ContainsKey(letter.Envelope.MessageId);

        /// <summary> The body a bubble should draw: the opened vanishing text when there is one, otherwise what the load decrypted. </summary>
        /// <param name="letter"> The letter being drawn. </param>
        /// <returns> The text to show. </returns>
        string TextFor(OpenedMessage letter) => Current(letter).Text;

        /// <summary> Whether a bubble may draw text rather than the padlocked line, which changes once a vanishing letter is opened. </summary>
        /// <param name="letter"> The letter being drawn. </param>
        /// <returns> True when its body is in hand. </returns>
        bool CanDrawText(OpenedMessage letter) => Current(letter).CouldDecrypt;

        /// <summary>
        /// The version of a letter the screen should trust: the opened one when this reader has already consumed
        /// it, otherwise the one the last load produced.
        /// </summary>
        /// <param name="letter"> The letter being drawn. </param>
        /// <returns> The letter to read the body and the seal from. </returns>
        OpenedMessage Current(OpenedMessage letter)
            => openedVanishing.TryGetValue(letter.Envelope.MessageId, out OpenedMessage opened) ? opened : letter;

        /// <summary>
        /// Attachments a bubble should fetch for itself. A vanishing letter's media is never fetched — it was
        /// handed over once and destroyed — so only an ordinary letter's attachments are listed here.
        /// </summary>
        /// <param name="letter"> The letter being drawn. </param>
        /// <returns> Attachments still sitting in the blob store, or nothing for a vanishing letter. </returns>
        static IReadOnlyList<MediaAttachment> StoredAttachmentsFor(OpenedMessage letter)
            => letter.Envelope.IsVanishing ? [] : letter.Envelope.Attachments;

        /// <summary>
        /// Seals what the reader wrote to the other account and stores it. The conversation is not re-read here:
        /// sending raises the messages event this page already reloads on, so the new letter arrives the same way one
        /// sent from another device would. The draft is only cleared once a letter was actually stored, so text the
        /// service refused stays on screen to be fixed.
        /// </summary>
        /// <returns> A task that completes once the letter has been stored and the composer has been unlocked. </returns>
        async Task SendLetterAsync()
        {
            if (otherProfile is null || isSending) return;
            if (string.IsNullOrWhiteSpace(draftText) && draftAttachments.Count == 0) return;

            isSending = true;

            try
            {
                MessageData? sent = await MessageService.SendAsync(
                    Account, otherProfile, draftText, isDraftVanishing, draftAttachments, replyingToMessageId);

                if (sent is null)
                {
                    Log($"Letter to '{Address}' was refused at {draftText.Trim().Length} characters.", LogLevel.Warning);
                    return;
                }

                draftText = string.Empty;
                isDraftVanishing = false;
                draftAttachments = [];
                StopReplying();
            }
            catch (Exception error)
            {
                Log($"{nameof(Messages)} could not send a letter to '{Address}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                isSending = false;
            }
        }

        /// <summary> Opens one conversation from the postbox. </summary>
        /// <param name="address"> Account whose conversation to open. </param>
        void OpenConversation(string address) => NavManager.NavigateTo(LinkTo(address));

        /// <summary> Leaves an open conversation for the postbox it was opened from. </summary>
        void GoBackToInbox() => NavManager.NavigateTo(NavigationConstants.Messages.Link);

        /// <summary> Sends the reader to Search, from the placeholder shown on an empty postbox. </summary>
        void GoToSearch() => NavManager.NavigateTo(NavigationConstants.Search.Link);

        /// <summary> Spoken name of one postbox row, so a screen reader announces who the row is about. </summary>
        /// <param name="row"> The row being drawn. </param>
        /// <returns> The row's accessible label. </returns>
        static string OpenHintFor(InboxRow row) => string.Format(OpenConversationHintFormat, row.Name);

        /// <summary> The row's share of the staggered entrance, as an inline style the CSS animation reads. </summary>
        /// <param name="rowIndex"> Position of the row in the postbox, counted from zero. </param>
        /// <returns> An <c>animation-delay</c> declaration for that row. </returns>
        static string BuildArrivalDelayStyle(int rowIndex)
            => $"animation-delay:{Math.Min(rowIndex, LastStaggeredRowIndex) * ArrivalStaggerStepMs}ms;";

        /// <summary> The name drawn for an account, wherever this page draws one. </summary>
        /// <param name="profile"> That account's profile, or null when it could not be read. </param>
        /// <param name="address"> That account's address, which the fallback name is built from. </param>
        /// <returns> The display name, or the readable head of the address. </returns>
        static string NameFor(ProfileData? profile, string address)
            => profile is not null && !string.IsNullOrWhiteSpace(profile.DisplayName)
                ? profile.DisplayName
                : ProfileService.FallbackDisplayName(address);

        /// <summary>
        /// The emoji drawn for an account. One with no stored profile still gets the emoji its account would have
        /// been given, because that is derived from the address rather than stored.
        /// </summary>
        /// <param name="profile"> That account's profile, or null when it could not be read. </param>
        /// <param name="address"> That account's address, which the fallback emoji is picked from. </param>
        /// <returns> The avatar emoji. </returns>
        static string AvatarFor(ProfileData? profile, string address)
            => profile is not null && !string.IsNullOrWhiteSpace(profile.Avatar)
                ? profile.Avatar
                : ProfileService.PickAvatar(address);
    }
}
