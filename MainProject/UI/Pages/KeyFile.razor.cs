using System.Text;
using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// Where a secret becomes something that can be kept somewhere untrusted, and where such a file is opened
    /// again on a machine that has never seen the account.
    /// </summary>
    public partial class KeyFile
    {
        /// <summary> Where this page lives. </summary>
        public const string KeyFileRoute = "/key-file";

        /// <summary> Heading at the top of the page. </summary>
        const string PageHeadline = "Your key, in a file";

        /// <summary> Line under it. </summary>
        const string PageSubtitle = "A secret you cannot store anywhere becomes a file you can.";

        /// <summary> Emoji beside the heading. </summary>
        const string PageEmoji = "🔐";

        /// <summary> Corner radius shared by both cards, written the way the card component wants it. </summary>
        static readonly string CardRadius = $"{AppMeasures.Radius.XLarge}px";

        /// <summary> Inner spacing shared by both cards. </summary>
        static readonly string CardPadding = $"{AppMeasures.Space.Px24}px";

        /// <summary> How tall the copyable and pasteable boxes are: enough to show the file is real, not so tall they take the screen. </summary>
        const int FallbackRows = 6;

        /// <summary> Emoji on the section that locks a key. </summary>
        const string LockSectionEmoji = "🔒";

        /// <summary> Heading of that section. </summary>
        const string LockSectionHeadline = "Lock your key into a file";

        /// <summary> Line under it, saying what the file is and what it is not. </summary>
        const string LockSectionDescription =
            "Your secret, encrypted under a passphrase you choose. This is the one form of it that can sit in a " +
            "cloud drive, an email to yourself or a second device: whoever finds the file finds a puzzle, because " +
            "every guess at the passphrase has to be paid for in computer time.";

        /// <summary> What the passphrase box invites while it is empty. </summary>
        const string LockPassphrasePlaceholder = "A passphrase you will not forget";

        /// <summary> The line under that box, naming the floor and what losing the passphrase costs. </summary>
        static readonly string PassphraseRule =
            $"At least {KeyFileService.ShortestPassphraseLength} characters. Nobody can reset this — a passphrase " +
            "you forget is a file nobody opens again, including us.";

        /// <summary> Text on the button that locks it. </summary>
        const string LockLabel = "Lock my key";

        /// <summary> What that button says while the passphrase is being stretched. </summary>
        const string LockingLabel = "Locking…";

        /// <summary> Line shown once a file exists. </summary>
        const string LockedDescription =
            "Save this somewhere you will still have it when this device is gone. It carries your address in the " +
            "clear so you can tell two of them apart, and nothing else that is readable.";

        /// <summary> Text on the link that hands the file over. </summary>
        const string DownloadLabel = "Save the file";

        /// <summary> Line above the box the file can be copied out of. </summary>
        const string CopyFallbackLabel = "Or copy it out by hand:";

        /// <summary> Emoji on the section that opens a file. </summary>
        const string OpenSectionEmoji = "🔓";

        /// <summary> Heading of that section. </summary>
        const string OpenSectionHeadline = "Open a locked file";

        /// <summary> Line under it. </summary>
        const string OpenSectionDescription =
            "Choose a file you locked earlier and type its passphrase. This works on a machine that has never seen " +
            "your account — opening the file signs you in exactly as pasting the secret would.";

        /// <summary> What the file picker says before a file is chosen. </summary>
        const string PickLabel = "Choose a key file";

        /// <summary> Line above the box a file can be pasted into. </summary>
        const string PasteFallbackLabel = "Or paste one in:";

        /// <summary> What the empty paste box invites. </summary>
        const string PastePlaceholder = "Paste the whole file here";

        /// <summary> Text on the button that reads what was pasted. </summary>
        const string OpenPastedLabel = "Read what I pasted";

        /// <summary> Line above the address of an opened file. </summary>
        const string WhoseFileLabel = "This file belongs to:";

        /// <summary> What the passphrase box invites while opening. </summary>
        const string OpenPassphrasePlaceholder = "The passphrase for this file";

        /// <summary> Text on the button that tries the passphrase. </summary>
        const string UnlockLabel = "Open it";

        /// <summary> What that button says while the passphrase is being stretched. </summary>
        const string OpeningLabel = "Opening…";

        /// <summary> Shown when what was chosen or pasted is not one of these files. </summary>
        const string NotAKeyFileMessage = "That is not a key file. Choose the one you saved from this screen.";

        /// <summary> Shown when the file is too big to be one of these. </summary>
        static readonly string TooLargeMessage =
            $"That file is far bigger than a key file, which is under {LargestKeyFileBytes} bytes. Nothing was read.";

        /// <summary> Shown when the passphrase did not open the file. </summary>
        const string WrongPassphraseMessage =
            "That passphrase did not open this file. A wrong passphrase and a file somebody altered look the same " +
            "from here, which is what keeps a tampered one from opening at all.";

        /// <summary> Shown when the file opened but the secret inside it was refused by the sign-in. </summary>
        const string RefusedSecretMessage = "The file opened, but what came out of it is not an account this app can open.";

        /// <summary> What a saved file is called before the address is written into it. </summary>
        const string FileNamePrefix = "chay-key-";

        /// <summary> And after it. </summary>
        const string FileNameSuffix = ".json";

        /// <summary> What the file is, for the link that hands it over. </summary>
        const string KeyFileContentType = "application/json";

        /// <summary> What the picker offers, so the operating system's dialog does not list everything. </summary>
        const string AcceptList = ".json,application/json";

        /// <summary>
        /// Largest file read. A locked seed is a few hundred bytes; this leaves room for indentation and for a
        /// longer address without letting somebody hand the tab a film.
        /// </summary>
        const int LargestKeyFileBytes = 8 * 1024;

        /// <summary> Passphrase typed into the locking box. </summary>
        string _lockPassphrase = string.Empty;

        /// <summary> The locked file as text, kept so it can be offered for saving and for copying. </summary>
        string _lockedText = string.Empty;

        /// <summary> The locked file as something a link can point at. </summary>
        string _lockedUri = string.Empty;

        /// <summary> True while the passphrase is being stretched into a key. </summary>
        bool _isLocking;

        /// <summary> A file chosen or pasted, once it has proved to be one of these. </summary>
        SealedIdentity? _chosen;

        /// <summary> A file pasted in as text, for when one cannot be handed over. </summary>
        string _pastedText = string.Empty;

        /// <summary> Passphrase typed into the opening box. </summary>
        string _openPassphrase = string.Empty;

        /// <summary> True while a passphrase is being tried. </summary>
        bool _isOpening;

        /// <summary> Why the last file or passphrase was refused, or null when nothing was. </summary>
        string? _openFailure;

        /// <summary> True while the passphrase is long enough and nothing is already being locked. </summary>
        bool CanLock => !_isLocking && _lockPassphrase.Length >= KeyFileService.ShortestPassphraseLength;

        /// <summary> What the locked file is called when it is saved, named after the account it belongs to. </summary>
        string LockedFileName =>
            FileNamePrefix + Elements.Social.AddressChip.Shorten(SessionService.CurrentAddress) + FileNameSuffix;

        /// <summary> Goes back where this page is reached from, which differs by whether anybody is signed in. </summary>
        void GoBack() => NavManager.NavigateTo(SessionService.IsSignedIn ? Settings.SettingsRoute : "/");

        /// <summary> Keeps the locking passphrase in step with its box. </summary>
        /// <param name="args"> The input event carrying the box's new contents. </param>
        void HandleLockPassphraseInput(ChangeEventArgs args) => _lockPassphrase = args.Value?.ToString() ?? string.Empty;

        /// <summary> Keeps the opening passphrase in step with its box. </summary>
        /// <param name="args"> The input event carrying the box's new contents. </param>
        void HandleOpenPassphraseInput(ChangeEventArgs args) => _openPassphrase = args.Value?.ToString() ?? string.Empty;

        /// <summary> Keeps the pasted text in step with its box. </summary>
        /// <param name="args"> The input event carrying the box's new contents. </param>
        void HandlePastedInput(ChangeEventArgs args) => _pastedText = args.Value?.ToString() ?? string.Empty;

        /// <summary> Locks the signed-in account's secret behind the typed passphrase. </summary>
        /// <returns> A task that completes once the file is ready to save. </returns>
        async Task LockAsync()
        {
            if (!CanLock || SessionService.Current is not PrivateIdentity owner) return;

            _isLocking = true;
            StateHasChanged();

            try
            {
                // Yielded to rather than pushed onto another thread: a browser gives this app one thread, and
                // Task.Run buys no parallelism there. The yield lets the busy label reach the screen before the
                // stretching starts; the stretching itself is one call and holds the thread for as long as it
                // takes, which is the price of a passphrase being expensive to guess.
                await Task.Yield();

                byte[] file = KeyFileService.Serialise(KeyFileService.Seal(owner, _lockPassphrase));

                _lockedText = Encoding.UTF8.GetString(file);
                _lockedUri = MediaService.BuildDataUri(file, KeyFileContentType);
            }
            finally
            {
                _isLocking = false;
                StateHasChanged();
            }
        }

        /// <summary> Reads a chosen file and accepts it only if it is one of these. </summary>
        /// <param name="change"> The file that was picked. </param>
        /// <returns> A task that completes once the file has been read and judged. </returns>
        async Task HandleFileChosen(InputFileChangeEventArgs change)
        {
            _chosen = null;
            _openFailure = null;

            IBrowserFile file = change.File;

            if (file.Size > LargestKeyFileBytes)
            {
                _openFailure = TooLargeMessage;
                return;
            }

            using MemoryStream buffer = new();
            await file.OpenReadStream(LargestKeyFileBytes).CopyToAsync(buffer);

            Read(buffer.ToArray());
        }

        /// <summary> Reads whatever was pasted into the box. </summary>
        void OpenPasted() => Read(Encoding.UTF8.GetBytes(_pastedText));

        /// <summary> Takes bytes and keeps them only if they are one of these files. </summary>
        /// <param name="bytes"> What was chosen or pasted. </param>
        void Read(byte[] bytes)
        {
            _chosen = null;
            _openFailure = null;
            _openPassphrase = string.Empty;

            SealedIdentity? file = KeyFileService.Deserialise(bytes);

            if (file is null)
            {
                _openFailure = NotAKeyFileMessage;
                return;
            }

            _chosen = file;
        }

        /// <summary>
        /// Tries the typed passphrase and, when it opens the file, signs in with what came out.
        /// </summary>
        /// <returns> A task that completes once the account is open or the attempt has failed. </returns>
        /// <remarks>
        /// Signing in through the ordinary path rather than a shortcut of its own, so everything that hangs off a
        /// sign-in still happens: the duress secret is still checked, and the account is still carried on this
        /// device the way it would be after pasting a secret.
        /// </remarks>
        async Task UnlockAsync()
        {
            if (_isOpening || _chosen is not SealedIdentity file) return;

            _isOpening = true;
            _openFailure = null;
            StateHasChanged();

            try
            {
                // Same as the locking side: yield so the busy label paints, then pay the cost on this one thread.
                await Task.Yield();

                if (!KeyFileService.TryOpen(file, _openPassphrase, out string secretText))
                {
                    _openFailure = WrongPassphraseMessage;
                    return;
                }

                if (!await SessionService.SignInAsync(secretText))
                {
                    _openFailure = RefusedSecretMessage;
                    return;
                }

                NavManager.NavigateTo(NavigationConstants.Wall.Link);
            }
            finally
            {
                _isOpening = false;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }
    }
}
