using System.Text;
using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Services;
using Microsoft.AspNetCore.Components.Forms;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// Where an account picks its work up and carries it somewhere else. The secret already moves an identity
    /// between machines; this moves everything the identity has written, which is what turns "you could leave" into
    /// something a person can actually do.
    /// </summary>
    public partial class Archive
    {
        /// <summary> Where this page lives. </summary>
        public const string ArchiveRoute = "/archive";

        /// <summary> Heading at the top of the page. </summary>
        const string PageHeadline = "Your archive";

        /// <summary> Line under it. </summary>
        const string PageSubtitle = "An account is a secret you keep. This makes it a secret and a file.";

        /// <summary> Emoji beside the heading. </summary>
        const string PageEmoji = "🧳";

        /// <summary> Corner radius shared by both cards, written the way the card component wants it. </summary>
        static readonly string CardRadius = $"{AppMeasures.Radius.XLarge}px";

        /// <summary> Inner spacing shared by both cards. </summary>
        static readonly string CardPadding = $"{AppMeasures.Space.Px24}px";

        /// <summary> How tall the copyable box is: enough to show the file is real, not so tall it takes the screen. </summary>
        const int FallbackRows = 6;

        /// <summary> What the file picker says before a file is chosen. </summary>
        const string PickLabel = "Choose an archive file";

        /// <summary> Emoji on the section that seals an archive. </summary>
        const string TakeSectionEmoji = "📦";

        /// <summary> Heading of that section. </summary>
        const string TakeSectionHeadline = "Take everything with you";

        /// <summary> Line under it. </summary>
        const string TakeSectionDescription =
            "Everything this account has written, sealed into one file with your signature on it. Your secret is not " +
            "in there — that is the part you carry yourself — and neither is anything the server did not already hold.";

        /// <summary> Emoji on the section that brings an archive back. </summary>
        const string BringSectionEmoji = "📥";

        /// <summary> Heading of that section. </summary>
        const string BringSectionHeadline = "Bring an archive back";

        /// <summary> Line under it. </summary>
        const string BringSectionDescription =
            "Open a file you sealed earlier and write it back into whichever server this app is pointed at. Only " +
            "documents belonging to the account you are signed in as are written; everything else is refused.";

        /// <summary> What the button that gathers everything says. </summary>
        const string SealLabel = "Seal my archive";

        /// <summary> What that button says while it is gathering. </summary>
        const string SealingLabel = "Gathering…";

        /// <summary> What the download link says. </summary>
        const string DownloadLabel = "Save the file";

        /// <summary> Introduces the copyable text, for whenever saving a file is not offered. </summary>
        const string CopyFallbackLabel = "Or copy it out by hand:";

        /// <summary> What the button that writes an archive back says. </summary>
        const string RestoreLabel = "Write it back";

        /// <summary> What that button says while it is writing. </summary>
        const string RestoringLabel = "Writing…";

        /// <summary> Shown when a chosen file is not an archive at all. </summary>
        const string NotAnArchiveMessage = "That file is not a ChaySocial archive.";

        /// <summary> Shown when a file's seal does not verify against the signed-in account. </summary>
        const string SealFailedMessage =
            "This file's seal does not match this account. It was either sealed by somebody else or changed after it was sealed.";

        /// <summary> Shown when the file is bigger than any real archive would be. </summary>
        const string TooLargeMessage = "That file is too large to be an archive.";

        /// <summary> What a sealed file is called when it is saved. </summary>
        const string FileNamePrefix = "chay-archive-";

        /// <summary> Extension of a sealed file. </summary>
        const string FileNameSuffix = ".json";

        /// <summary> Content type a sealed archive is offered as. </summary>
        const string ArchiveContentType = "application/json";

        /// <summary> File types the picker offers. </summary>
        const string AcceptList = ".json,application/json";

        /// <summary> Largest file accepted, well above any real archive but far below anything that would hurt the tab. </summary>
        const int LargestArchiveBytes = 32 * 1024 * 1024;

        /// <summary> The archive this account has sealed in this visit, or null before the button is pressed. </summary>
        AccountArchive? _sealed;

        /// <summary> The sealed file as text, kept so it can be offered for saving and for copying. </summary>
        string _sealedText = string.Empty;

        /// <summary> The sealed file as something a link can point at. </summary>
        string _sealedUri = string.Empty;

        /// <summary> True while the archive is being gathered. </summary>
        bool _isSealing;

        /// <summary> The archive read out of a chosen file, once it has proved to be one. </summary>
        AccountArchive? _opened;

        /// <summary> True while an opened archive is being written back. </summary>
        bool _isRestoring;

        /// <summary> What came of the last restore, or null when none has run. </summary>
        ArchiveRestoreResult? _restored;

        /// <summary> Why the last chosen file was refused, or null when nothing was refused. </summary>
        string? _openFailure;

        /// <summary> Nothing is fetched before this page can draw: both halves start from a button. </summary>
        /// <returns> A completed task. </returns>
        protected override Task LoadAsync() => Task.CompletedTask;

        /// <summary> What the sealed file is called when it is saved, named after the account it belongs to. </summary>
        string SealedFileName => FileNamePrefix + Elements.Social.AddressChip.Shorten(Account.Public.Address) + FileNameSuffix;

        /// <summary> Goes back to the screen this page is reached from. </summary>
        void GoBackToSettings() => NavManager.NavigateTo(Settings.SettingsRoute);

        /// <summary> Says in plain words what an archive holds, so nobody saves or writes back something unseen. </summary>
        /// <param name="archive"> The archive being described. </param>
        /// <returns> One English line naming the counts. </returns>
        static string SummaryOf(AccountArchive archive) =>
            $"{Counted(archive.DocumentCount, "document")} — {Counted(archive.Posts.Count, "post")}, " +
            $"{Counted(archive.Comments.Count, "comment")}, {Counted(archive.Reposts.Count, "repost")}, " +
            $"{Counted(archive.Follows.Count, "follow")}, {Counted(archive.Likes.Count, "chay")}, " +
            $"{Counted(archive.Messages.Count, "letter")}.";

        /// <summary> A count and the thing it counts, in the number English would use. </summary>
        /// <param name="count"> How many there are. </param>
        /// <param name="singular"> The word for one of them. </param>
        /// <returns> The count and the word, pluralised when it should be. </returns>
        static string Counted(int count, string singular) => count == 1 ? $"1 {singular}" : $"{count} {singular}s";

        /// <summary> Says what a restore actually did, including what it would not do. </summary>
        /// <param name="result"> What came back from the restore. </param>
        /// <returns> One English line. </returns>
        static string ResultOf(ArchiveRestoreResult result) => result.Refused == 0
            ? $"{Counted(result.Written, "document")} written back."
            : $"{Counted(result.Written, "document")} written back. {result.Refused} refused: they belong to another account.";

        /// <summary> Gathers everything this account has written and seals it. </summary>
        /// <returns> A task that completes once the archive is ready to save. </returns>
        async Task SealAsync()
        {
            if (_isSealing) return;

            _isSealing = true;
            StateHasChanged();

            try
            {
                AccountArchive archive = await AccountArchiveService.BuildAsync(Account);
                byte[] file = AccountArchiveService.Serialise(archive);

                _sealed = archive;
                _sealedText = Encoding.UTF8.GetString(file);
                _sealedUri = MediaService.BuildDataUri(file, ArchiveContentType);
            }
            finally
            {
                _isSealing = false;
                StateHasChanged();
            }
        }

        /// <summary>
        /// Reads a chosen file and accepts it only if it is an archive this account itself sealed.
        /// </summary>
        /// <param name="change"> The file the reader picked. </param>
        /// <returns> A task that completes once the file has been read and judged. </returns>
        async Task HandleFileChosen(InputFileChangeEventArgs change)
        {
            _opened = null;
            _restored = null;
            _openFailure = null;

            IBrowserFile file = change.File;

            if (file.Size > LargestArchiveBytes)
            {
                _openFailure = TooLargeMessage;
                return;
            }

            using MemoryStream buffer = new();
            await file.OpenReadStream(LargestArchiveBytes).CopyToAsync(buffer);

            AccountArchive? archive = AccountArchiveService.Deserialise(buffer.ToArray());
            if (archive is null)
            {
                _openFailure = NotAnArchiveMessage;
                return;
            }

            // Checked before anything is offered, so nobody is ever invited to write back a file that was tampered
            // with or that belongs to somebody else.
            if (!AccountArchiveService.VerifySeal(archive, Account.Public))
            {
                _openFailure = SealFailedMessage;
                return;
            }

            _opened = archive;
        }

        /// <summary> Writes the opened archive back into whichever store this app is pointed at. </summary>
        /// <returns> A task that completes once every document has been written or refused. </returns>
        async Task RestoreAsync()
        {
            if (_opened is null || _isRestoring) return;

            _isRestoring = true;
            StateHasChanged();

            try
            {
                _restored = await AccountArchiveService.RestoreAsync(_opened, Account);
                Events.MainEvents.Trigger(Events.MainEvents.Names.WallChanged);
            }
            finally
            {
                _isRestoring = false;
                StateHasChanged();
            }
        }
    }
}
