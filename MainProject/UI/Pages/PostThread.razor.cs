using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Services;
using ChaySocial.MainProject.Text;
using Microsoft.AspNetCore.Components;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// The page behind a single post: the post itself, the replies under it, and the composer that adds another.
    /// Everything it draws is re-read whenever a comment, a post or the session changes, so a reply written here and
    /// a reply written on another device both land on screen without the reader doing anything.
    /// </summary>
    public partial class PostThread
    {
        /// <summary> Start of every thread address. <see cref="ThreadRoute"/> and <see cref="LinkTo"/> are both built from it. </summary>
        public const string ThreadRoutePrefix = "/post";

        /// <summary>
        /// Route this page answers on. The parameter segment is spelled from <see cref="PostId"/> itself, so renaming
        /// the property moves the route with it instead of silently breaking every link.
        /// </summary>
        public const string ThreadRoute = ThreadRoutePrefix + "/{" + nameof(PostId) + "}";

        /// <summary> Builds the address that opens one post's thread. </summary>
        /// <param name="postId"> Id of the post to open. </param>
        /// <returns> A path another page can hand to its navigation manager. </returns>
        public static string LinkTo(string postId) => $"{ThreadRoutePrefix}/{postId}";

        /// <summary> Id of the post to show, taken from the route. </summary>
        [Parameter] public string PostId { get; set; } = string.Empty;

        /// <summary> Header title, and the browser tab's title. </summary>
        const string ThreadTitle = "Conversation";

        /// <summary> Header line shown while the post id resolves to nothing. </summary>
        const string MissingPostSubtitle = "nothing to read here";

        /// <summary> Header line for a thread holding exactly one reply, where the plural would read wrong. </summary>
        const string SingleReplySubtitle = "1 reply";

        /// <summary> Header line for any other reply count; the placeholder takes the count. </summary>
        const string ManyRepliesSubtitleFormat = "{0} replies";

        /// <summary> Heading over the list of replies. </summary>
        const string RepliesHeading = "Replies";

        /// <summary> Emoji beside that heading. </summary>
        const string RepliesEmoji = "💬";

        /// <summary> Emoji for the placeholder shown when the post id resolves to nothing. </summary>
        const string MissingPostEmoji = "🫧";

        /// <summary> Headline of that placeholder. Phrased as something that happened, not as something the reader did wrong. </summary>
        const string MissingPostHeadline = "This post floated away";

        /// <summary> Supporting line of that placeholder, naming both ways a thread ends up empty. </summary>
        const string MissingPostDescription = "Whoever wrote it may have taken it down, or the link lost a character on its way here.";

        /// <summary> Emoji over the placeholder shown where the composer would be when the reader is outside the circle. </summary>
        const string ReplyLimitedEmoji = "🚪";

        /// <summary> Headline of that placeholder, said about the conversation rather than about the reader. </summary>
        const string ReplyLimitedHeadline = "This conversation is narrower";

        /// <summary> What the reader is told when only the writer's own people may answer. </summary>
        const string ReplyLimitedToFollowedDescription =
            "Whoever wrote this left it open to the people they follow. You can still read every word of it.";

        /// <summary> What they are told when only the accounts named in the post may answer. </summary>
        const string ReplyLimitedToNamedDescription =
            "Whoever wrote this left it open to the people they named in it. You can still read every word of it.";

        /// <summary> What they are told when nobody may answer. </summary>
        const string ReplyLimitedToNobodyDescription =
            "Whoever wrote this said it rather than asked it, and left it closed to replies. You can still read every word of it.";

        /// <summary> True when the reader may write under this post; false while the writer's circle shuts them out. </summary>
        bool mayReply = true;

        /// <summary> True when a block stands between this reader and the post's writer, whichever of them made it. </summary>
        bool isShutOut;

        /// <summary>
        /// What the reader is told when a block is why the box is gone. It names neither direction: telling
        /// somebody they were blocked is a message from the person who wanted no more messages.
        /// </summary>
        const string ReplyBlockedDescription =
            "There is a block between you and whoever wrote this. You can still read every word of it.";

        /// <summary> True while the count receipt is open. </summary>
        bool isReceiptOpen;

        /// <summary> What the numbers under this post were counted from, or null while it has not been read. </summary>
        PostCountReceipt? receipt;

        /// <summary>
        /// Opens or closes the count receipt, reading it the first time it is opened.
        /// </summary>
        /// <returns> A task that completes once the receipt is held, or at once when it already is. </returns>
        /// <remarks>
        /// Read on opening rather than alongside the post. Checking every signature under a post costs a read per
        /// account named by a record, and a reader who never asks should not pay for an answer they did not want.
        /// </remarks>
        async Task ToggleReceiptAsync()
        {
            isReceiptOpen = !isReceiptOpen;

            if (!isReceiptOpen || receipt is not null || post is null) return;

            receipt = await CountReceipt.ReadAsync(post, Account.Public.Address);
        }

        /// <summary> What to tell the reader about the door they are outside of. </summary>
        /// <remarks>
        /// A block is checked first because it is the reason that overrides the others: a reader who is blocked
        /// would be shut out whatever circle the post carried, and telling them about the circle instead would be
        /// naming the wrong reason.
        /// </remarks>
        string ReplyLimitedDescription => isShutOut
            ? ReplyBlockedDescription
            : post?.ReplyCircle switch
            {
                ReplyCircle.FollowedByAuthor => ReplyLimitedToFollowedDescription,
                ReplyCircle.NamedOnly => ReplyLimitedToNamedDescription,
                _ => ReplyLimitedToNobodyDescription
            };

        /// <summary> Emoji for the placeholder shown while a post has no replies. </summary>
        const string NoRepliesEmoji = "🌱";

        /// <summary> Headline of that placeholder. </summary>
        const string NoRepliesHeadline = "No replies yet";

        /// <summary> Supporting line of that placeholder, which is also the invitation to use the composer below it. </summary>
        const string NoRepliesDescription = "Say the first kind thing under this post — one line is plenty.";

        /// <summary> Emoji for the placeholder shown when the thread could not be read at all. </summary>
        const string LoadFailedEmoji = "🌧️";

        /// <summary> Headline of that placeholder; the supporting line is the failure message the page base supplies. </summary>
        const string LoadFailedHeadline = "The thread didn't come through";

        /// <summary> Label on the button that runs the failed load again. </summary>
        const string TryAgainLabel = "Try again";

        /// <summary> Label on the button that leaves a thread with nothing in it. </summary>
        const string BackToWallLabel = "Back to the wall";

        /// <summary> Diameter of the small spinner that sits in the header while an already-drawn thread refreshes. </summary>
        const int RefreshSpinnerDiameterPx = AppMeasures.Size.Px20;

        /// <summary> Ring thickness of that spinner, thinned from the app default so a disc this small still reads as a ring. </summary>
        const int RefreshSpinnerBorderPx = AppMeasures.Border.Medium;

        /// <summary> Fully rounded corners on the buttons offered under a placeholder. </summary>
        static readonly string ActionButtonRadiusCss = $"{AppMeasures.Radius.Pill}px";

        /// <summary> Padding inside those buttons: wide across and shallow down, so they read as pills. </summary>
        static readonly string ActionButtonPaddingCss = $"{AppMeasures.Space.Px12}px {AppMeasures.Space.Px24}px";

        /// <summary> The post this thread hangs under, or null when nothing is stored under <see cref="PostId"/>. </summary>
        PostData? post;

        /// <summary> The post's replies, oldest first, the way a conversation is read. </summary>
        IReadOnlyList<CommentData> replies = [];

        /// <summary> The same replies in reading order: each remark, then the answers written to it. </summary>
        IReadOnlyList<ThreadedComment> thread = [];

        /// <summary> Block of the piece the reader is reading the notes beside, counting from one, or zero for the whole thread. </summary>
        int chosenBlockIndex;

        /// <summary> How many notes sit beside each block of the piece, in the piece's own order. </summary>
        IReadOnlyList<int> noteCountsPerBlock = [];

        /// <summary> The piece's blocks, kept so the composer can say which paragraph a note is landing beside. </summary>
        IReadOnlyList<ProseBlock> blocks = [];

        /// <summary> True when this post is a piece somebody can leave notes down the side of. </summary>
        bool CanAnchorNotes => post is { IsLongForm: true } && blocks.Count > 0;

        /// <summary> How the chosen paragraph is named in the composer's bar; the placeholder takes its number. </summary>
        const string AnchoredBlockFormat = "paragraph {0}";

        /// <summary> Heading over the replies while the reader is reading one paragraph's notes; the placeholder takes its number. </summary>
        const string NotesOnBlockHeadingFormat = "Notes beside paragraph {0}";

        /// <summary> Label on the control that drops the chosen paragraph and shows the whole thread again. </summary>
        const string ShowWholeThreadLabel = "Show every reply";

        /// <summary> Emoji for the placeholder shown while a chosen paragraph has no notes beside it yet. </summary>
        const string NoNotesEmoji = "🔖";

        /// <summary> Headline of that placeholder. </summary>
        const string NoNotesHeadline = "Nothing beside this one yet";

        /// <summary> Supporting line of that placeholder, which is also the invitation to write the first note. </summary>
        const string NoNotesDescription = "Whatever you write now lands beside this paragraph rather than under the whole piece.";

        /// <summary> The replies actually listed: everything, or only the notes beside the paragraph the reader picked. </summary>
        IReadOnlyList<ThreadedComment> ShownThread => chosenBlockIndex == 0
            ? thread
            : [.. thread.Where(entry => entry.Comment.AnchorBlockIndex == chosenBlockIndex)];

        /// <summary> Which paragraph the composer is writing beside, or null while the draft speaks to the whole piece. </summary>
        string? AnchoredBlockLabel => chosenBlockIndex == 0
            ? null
            : string.Format(AnchoredBlockFormat, chosenBlockIndex);

        /// <summary> Heading over the replies: the whole thread's, or the chosen paragraph's. </summary>
        string RepliesSectionHeading => chosenBlockIndex == 0
            ? RepliesHeading
            : string.Format(NotesOnBlockHeadingFormat, chosenBlockIndex);

        /// <summary>
        /// Takes the paragraph the reader tapped in the margin, or drops the choice when they tapped the same one
        /// again. Both the list and the composer follow it, which is what makes a note land where it was read.
        /// </summary>
        /// <param name="index"> The block's place in the piece, counting from one, or zero for none. </param>
        void ChooseBlock(int index) => chosenBlockIndex = index;

        /// <summary> Drops the chosen paragraph, so the whole thread is listed again. </summary>
        void ShowWholeThread() => chosenBlockIndex = 0;

        /// <summary> Reply the composer is answering, or null while the draft speaks to the post itself. </summary>
        CommentData? answeringReply;

        /// <summary> Reply the report sheet is open for, or null while it is closed. </summary>
        CommentData? reportedReply;

        /// <summary> True while a report is being written, which swaps the reasons for a throbber. </summary>
        bool isReportInFlight;

        /// <summary> Every reason the sheet offers, in the order the enum declares them. </summary>
        static readonly ReportReason[] OfferedReportReasons = Enum.GetValues<ReportReason>();

        /// <summary> Emoji at the top of the report sheet. </summary>
        const string ReportEmoji = "🚩";

        /// <summary> Heading of that sheet. </summary>
        const string ReportTitle = "Report this comment";

        /// <summary> Line under it, saying plainly what filing a report does with the words. </summary>
        const string ReportSubtitle =
            "Pick what is wrong with it. Your report hands this comment's text to the moderators — it is the only way "
            + "anybody but its readers sees it.";

        /// <summary> Line under the throbber while a report is going out. </summary>
        const string ReportSendingLabel = "Sending your report…";

        /// <summary> Label on the button that closes the sheet without reporting. </summary>
        const string ReportCancelLabel = "Never mind";

        /// <summary> True while the report sheet belongs on screen. </summary>
        bool IsReportOpen => reportedReply is not null;

        /// <summary> Opens the report sheet for one reply. </summary>
        /// <param name="reply"> The reply being reported. </param>
        void OpenReplyReport(CommentData reply) => reportedReply = reply;

        /// <summary> Closes it, leaving a report already going out to finish. </summary>
        void CloseReplyReport()
        {
            if (isReportInFlight) return;

            reportedReply = null;
        }

        /// <summary> Files the report under the chosen reason and closes the sheet. </summary>
        /// <param name="reason"> The category the reader picked. </param>
        /// <returns> A task that completes once the report is stored. </returns>
        async Task SubmitReplyReportAsync(ReportReason reason)
        {
            if (reportedReply is null || isReportInFlight) return;

            isReportInFlight = true;

            try
            {
                await ModerationService.ReportCommentAsync(Account, reportedReply, reason);
            }
            catch (Exception error)
            {
                Log($"{nameof(PostThread)} could not report '{reportedReply.CommentId}'.\n{error}", LogLevel.Error);
            }
            finally
            {
                isReportInFlight = false;
                reportedReply = null;
            }
        }

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

        /// <summary> Emoji beside one reason, so the sheet can be read at a glance. </summary>
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

        /// <summary>
        /// Profiles of everyone the thread names, keyed by address. An address is read once and then answered from
        /// here, so an account that replied five times costs one fetch and a reload only asks for faces it has not
        /// seen yet.
        /// </summary>
        readonly Dictionary<string, ProfileData> profilesByAddress = [];

        /// <summary> How many accounts have liked the post. </summary>
        int likeCount;

        /// <summary> True when the signed-in account is one of them. </summary>
        bool isLikedByViewer;

        /// <summary> What the reader has typed into the composer and not sent yet. </summary>
        string draftText = string.Empty;

        /// <summary> True while a reply is being signed and stored, which locks the composer. </summary>
        bool isPublishing;

        /// <summary> True once a load has finished, so later reloads refresh the thread in place instead of blanking it. </summary>
        bool hasLoadedOnce;

        /// <summary> Post id the last load ran for; a different one in the route means the reader opened another thread. </summary>
        string loadedPostId = string.Empty;

        protected override string[] ReloadOnEvents =>
        [
            MainEvents.Names.CommentsChanged,
            MainEvents.Names.WallChanged,
            MainEvents.Names.SessionChanged
        ];

        /// <summary> True while the very first load runs and there is nothing on screen to keep. </summary>
        bool IsFirstLoad => IsLoading && !hasLoadedOnce;

        /// <summary> True while a reload refreshes a thread that is already drawn; only the header spinner reacts to it. </summary>
        bool IsRefreshing => IsLoading && hasLoadedOnce;

        /// <summary> Header line under the title: what the reader is looking at, in one short phrase. </summary>
        string ThreadSubtitle => post is null
            ? MissingPostSubtitle
            : replies.Count == 1
                ? SingleReplySubtitle
                : string.Format(ManyRepliesSubtitleFormat, replies.Count);

        /// <summary> Frosted bar the sticky header is painted on, so the thread scrolls under glass instead of under nothing. </summary>
        static string HeaderSurfaceStyle => AppStyles.BuildBarSurface(pinnedToBottom: false);

        /// <summary>
        /// Loads whatever the route asks for when the reader opens a second thread without leaving the page — going
        /// from an alert straight to another post, for instance. The first pass is already covered by the page base,
        /// which is why an id that matches the one just loaded is left alone.
        /// </summary>
        /// <returns> A task that completes once the new thread has been read, or immediately when nothing changed. </returns>
        protected override async Task OnParametersSetAsync()
        {
            await base.OnParametersSetAsync();

            if (!SessionService.IsSignedIn || loadedPostId == PostId) return;

            await ReloadAsync();
        }

        /// <summary> Reads the post, its replies, its likes, and the profile behind every address the thread draws. </summary>
        /// <returns> A task that completes once the page has everything it needs to render. </returns>
        protected override async Task LoadAsync()
        {
            if (!SessionService.IsSignedIn)
            {
                NavManager.NavigateTo(WelcomeRoute);
                return;
            }

            // A paragraph belongs to the piece it was picked in. Opening another thread on the same page has to
            // start it whole, or the reader lands on one post's third paragraph in another post's essay.
            if (loadedPostId != PostId) chosenBlockIndex = 0;

            loadedPostId = PostId;
            post = await AppServices.Documents.ReadAsync(new DocumentId<PostData>(PostId));

            // A receipt belongs to the post it was counted from. Reading another thread on the same page, or the
            // same one again after a reply, must not leave last time's numbers under this time's post.
            receipt = null;
            isReceiptOpen = false;

            if (post is null)
            {
                replies = [];
                thread = [];
                blocks = [];
                noteCountsPerBlock = [];
                chosenBlockIndex = 0;
                likeCount = 0;
                isLikedByViewer = false;
                hasLoadedOnce = true;
                return;
            }

            // Read once for the whole thread rather than once per writer, and used for both halves of the page:
            // which replies are drawn, and whether this reader gets a box to write in.
            IReadOnlySet<string> shutOut = await ModerationService.ReadShutOutAddressesAsync(Account.Public.Address);

            // Filtered on the way in rather than trusted: a reply written by a client that ignored the limit is
            // still in the store, and this is what keeps it off every screen.
            replies = await CommentService.KeepAllowedAsync(
                post, await CommentService.ReadForPostAsync(post.PostId), shutOut);
            thread = CommentService.ArrangeThread(replies);

            // The piece is split once here rather than by every part of the page that needs to know how long it is.
            blocks = post.IsLongForm ? WrittenProse.Read(post.LongBody) : [];
            noteCountsPerBlock = CommentService.CountNotesPerBlock(replies, blocks.Count);

            // A piece that was rewritten shorter, or a post the reader arrived at from another thread, can leave a
            // choice pointing at a paragraph that is no longer there.
            if (chosenBlockIndex > blocks.Count) chosenBlockIndex = 0;

            isShutOut = shutOut.Contains(post.AuthorAddress);
            mayReply = await CommentService.MayReplyAsync(post, Account.Public.Address);

            // A reply that was deleted while the reader was writing an answer to it has nothing left to answer.
            if (answeringReply is not null && replies.All(reply => reply.CommentId != answeringReply.CommentId))
            {
                answeringReply = null;
            }

            IReadOnlyList<string> likerAddresses = await WallService.ReadLikersAsync(post.PostId);
            likeCount = likerAddresses.Count;
            isLikedByViewer = likerAddresses.Contains(Account.Public.Address);

            await ReadMissingProfilesAsync();
            hasLoadedOnce = true;
        }

        /// <summary>
        /// Fills <see cref="profilesByAddress"/> with the profiles this thread needs and nothing more: the post's
        /// author plus each account that replied, skipping every address already answered from an earlier load.
        /// </summary>
        /// <returns> A task that completes once every missing profile has been read. </returns>
        async Task ReadMissingProfilesAsync()
        {
            if (post is null) return;

            List<string> namedAddresses = [post.AuthorAddress, .. replies.Select(reply => reply.AuthorAddress)];

            foreach (string address in namedAddresses.Distinct(StringComparer.Ordinal))
            {
                if (profilesByAddress.ContainsKey(address)) continue;

                ProfileData? profile = await ProfileService.ReadAsync(address);
                if (profile is not null) profilesByAddress[address] = profile;
            }
        }

        /// <summary> The profile behind an address, for the cards and rows that draw a name and a face. </summary>
        /// <param name="address"> Address to look up. </param>
        /// <returns> The stored profile, or null when that account has never published one — both callers fall back on their own. </returns>
        ProfileData? ProfileFor(string address) => profilesByAddress.GetValueOrDefault(address);

        /// <summary> True when the signed-in account wrote this reply, which is the only case a delete button is offered in. </summary>
        /// <param name="reply"> Reply being drawn. </param>
        /// <returns> True when the reply is the reader's own. </returns>
        bool IsOwnReply(CommentData reply) => reply.AuthorAddress == Account.Public.Address;

        /// <summary> Keeps the composer's text on the page, so what is half-typed survives every redraw. </summary>
        /// <param name="text"> The field's new contents. </param>
        void HandleDraftChanged(string text) => draftText = text;

        /// <summary> Points the composer at a reply to answer. </summary>
        /// <param name="reply"> Reply being answered. </param>
        void StartAnswering(CommentData reply) => answeringReply = reply;

        /// <summary> Drops the answer so the composer speaks to the post again. </summary>
        void StopAnswering() => answeringReply = null;

        /// <summary> Name shown in the composer's answer bar, or null while the draft speaks to the post. </summary>
        string? AnsweringName => answeringReply is null ? null : NameFor(answeringReply.AuthorAddress);

        /// <summary> Name shown above an answer, naming who it was written to; null for a remark on the post itself. </summary>
        /// <param name="entry"> The thread line being drawn. </param>
        /// <returns> The answered account's name, or null. </returns>
        string? RepliedToNameFor(ThreadedComment entry)
            => entry.RepliedTo is null ? null : NameFor(entry.RepliedTo.AuthorAddress);

        /// <summary> The readable name behind an address: what that account chose, or the head of the address itself. </summary>
        /// <param name="address"> Address to name. </param>
        /// <returns> A name fit to show. </returns>
        string NameFor(string address)
        {
            ProfileData? profile = ProfileFor(address);
            return string.IsNullOrWhiteSpace(profile?.DisplayName)
                ? ProfileService.FallbackDisplayName(address)
                : profile.DisplayName;
        }

        /// <summary>
        /// Signs the draft as the reader and stores it. The thread is not re-read here: publishing raises the
        /// comments event this page already reloads on, so the new reply arrives the same way one written elsewhere
        /// would.
        /// </summary>
        /// <returns> A task that completes once the reply has been stored and the composer has been unlocked. </returns>
        async Task PublishReplyAsync()
        {
            if (post is null || isPublishing || string.IsNullOrWhiteSpace(draftText)) return;

            isPublishing = true;

            try
            {
                CommentData? published = await CommentService.PublishAsync(
                    Account, post, draftText, answeringReply, chosenBlockIndex);

                if (published is null)
                {
                    Log($"Reply under post '{post.PostId}' was refused at {draftText.Trim().Length} characters.", LogLevel.Warning);
                    return;
                }

                draftText = string.Empty;
                StopAnswering();
            }
            finally
            {
                isPublishing = false;
            }
        }

        /// <summary> Removes one of the reader's own replies; anyone else's is refused by the service. </summary>
        /// <param name="reply"> Reply to remove. </param>
        /// <returns> A task that completes once the reply is gone and the thread has been told to redraw. </returns>
        Task DeleteReplyAsync(CommentData reply) => CommentService.DeleteAsync(reply, Account.Public);

        /// <summary>
        /// Pours the reader's chay onto the post, or takes it back. The count is moved here as well as re-read by
        /// the reload the service triggers, so the glass answers the tap immediately instead of after a round trip.
        /// </summary>
        /// <returns> A task that completes once the like has been stored or removed. </returns>
        async Task ToggleLikeAsync()
        {
            if (post is null) return;

            isLikedByViewer = await WallService.ToggleLikeAsync(post, Account.Public);
            likeCount = Math.Max(0, likeCount + (isLikedByViewer ? 1 : -1));
        }

        /// <summary> Leaves the thread for the wall it was opened from. </summary>
        void GoBackToWall() => NavManager.NavigateTo(NavigationConstants.Wall.Link);

        /// <summary> Opens the profile behind the post's author, built from the same navigation constant the profile tab uses. </summary>
        void OpenAuthorProfile()
        {
            if (post is null) return;

            NavManager.NavigateTo($"{NavigationConstants.Profile.Link}/{post.AuthorAddress}");
        }
    }
}
