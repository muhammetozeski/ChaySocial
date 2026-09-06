using System.Text;
using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Services;
using ChaySocial.MainProject.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// Where an address becomes something showable elsewhere: a sentence its owner signed, and a place to check
    /// one somebody else signed.
    /// </summary>
    public partial class Statement
    {
        /// <summary> Where this page lives. </summary>
        public const string StatementRoute = "/statement";

        /// <summary> Heading at the top of the page. </summary>
        const string PageHeadline = "Say it with your key";

        /// <summary> Line under it. </summary>
        const string PageSubtitle = "One sentence, signed, that anybody can check without trusting anybody.";

        /// <summary> Emoji beside the heading. </summary>
        const string PageEmoji = "🖋️";

        /// <summary> Corner radius shared by both cards, written the way the card component wants it. </summary>
        static readonly string CardRadius = $"{AppMeasures.Radius.XLarge}px";

        /// <summary> Inner spacing shared by both cards. </summary>
        static readonly string CardPadding = $"{AppMeasures.Space.Px24}px";

        /// <summary> Lines the claim box shows before it starts scrolling. </summary>
        const int ClaimRows = 3;

        /// <summary> Lines the copyable and pasteable boxes show; a block is a few kilobytes, so this is a window onto it. </summary>
        const int FallbackRows = 8;

        /// <summary> Diameter of the mark drawn beside a checked block. </summary>
        const int VerdictAvatarDiameterPx = AppMeasures.Size.Px64;

        /// <summary> Emoji on the section that signs a claim. </summary>
        const string WriteSectionEmoji = "✍️";

        /// <summary> Heading of that section. </summary>
        const string WriteSectionHeadline = "Sign a sentence";

        /// <summary> Line under it, saying what the block proves and what it does not. </summary>
        const string WriteSectionDescription =
            "Write what you want somebody to be able to check, and this signs it with your key. What it proves is " +
            "that this address signed this sentence — who you are, the sentence itself has to say, which is why " +
            "you write it rather than us.";

        /// <summary> What the empty claim box invites. </summary>
        const string ClaimPlaceholder = "e.g. The account at this address is the same person as @deniz on the forum.";

        /// <summary> How the counter reads; the placeholder takes the characters left. </summary>
        const string RemainingClaimFormat = "{0} left";

        /// <summary> Text on the button that signs it. </summary>
        const string SignLabel = "Sign this";

        /// <summary> Line shown once a block exists. </summary>
        const string SignedDescription =
            "This is the whole block. It carries your two public keys and the signature, which is why it runs to a " +
            "few kilobytes — and why whoever checks it needs nothing else.";

        /// <summary> Text on the link that hands the block over. </summary>
        const string DownloadLabel = "Save the block";

        /// <summary> Line above the box the block can be copied out of. </summary>
        const string CopyFallbackLabel = "Or copy it out:";

        /// <summary> Emoji on the section that checks a block. </summary>
        const string CheckSectionEmoji = "🔍";

        /// <summary> Heading of that section. </summary>
        const string CheckSectionHeadline = "Check somebody's block";

        /// <summary> Line under it. </summary>
        const string CheckSectionDescription =
            "Paste a block anybody handed you. It is checked here on this device against nothing but itself: the " +
            "keys inside it have to hash down to the address inside it, and the signature has to hold under them. " +
            "No server is asked and no account is needed.";

        /// <summary> What the file picker says before a file is chosen. </summary>
        const string PickLabel = "Choose a block file";

        /// <summary> Line above the box a block can be pasted into. </summary>
        const string PasteFallbackLabel = "Or paste one in:";

        /// <summary> What the empty paste box invites. </summary>
        const string PastePlaceholder = "Paste the whole block here";

        /// <summary> Text on the button that checks what was pasted. </summary>
        const string CheckLabel = "Check it";

        /// <summary> Shown when what was chosen or pasted is not one of these blocks. </summary>
        const string NotAStatementMessage = "That is not a signed block. Paste the whole thing, braces and all.";

        /// <summary> Shown when the file is too big to be one. </summary>
        static readonly string TooLargeMessage =
            $"That file is far bigger than a block, which is under {LargestStatementBytes} bytes. Nothing was read.";

        /// <summary> What a block that checks out is called. </summary>
        const string HeldVerdict = "This address signed this sentence.";

        /// <summary> And one that does not. </summary>
        const string BrokenVerdict =
            "This block does not check out. Either it was altered, or the keys in it do not belong to the address in it.";

        /// <summary> Class on the line for a block that held. </summary>
        const string HeldClass = "statement-held";

        /// <summary> Class on the line for a block that did not. </summary>
        const string BrokenClass = "statement-broken";

        /// <summary> How the signing time reads; the placeholder takes the exact moment. </summary>
        const string SignedAtFormat = "Signed {0}";

        /// <summary> What a saved block is called before the address is written into it. </summary>
        const string FileNamePrefix = "chay-statement-";

        /// <summary> And after it. </summary>
        const string FileNameSuffix = ".json";

        /// <summary> What the block is, for the link that hands it over. </summary>
        const string StatementContentType = "application/json";

        /// <summary> What the picker offers, so the operating system's dialog does not list everything. </summary>
        const string AcceptList = ".json,application/json";

        /// <summary>
        /// Largest file read. A block is a few kilobytes of keys and signature; this leaves generous room without
        /// letting somebody hand the tab a film.
        /// </summary>
        const int LargestStatementBytes = 64 * 1024;

        /// <summary> The claim being typed. </summary>
        string _claim = string.Empty;

        /// <summary> The signed block as text, kept so it can be offered for saving and for copying. </summary>
        string _signedText = string.Empty;

        /// <summary> The signed block as something a link can point at. </summary>
        string _signedUri = string.Empty;

        /// <summary> A block pasted in as text, for when one cannot be handed over. </summary>
        string _pastedText = string.Empty;

        /// <summary> The block that was read, once it proved to be one. </summary>
        SignedStatement? _checked;

        /// <summary> Whether that block's signature held. </summary>
        bool _isChecked;

        /// <summary> Why the last block was refused outright, or null when none was. </summary>
        string? _checkFailure;

        /// <summary> True when there is a usable claim to sign. </summary>
        bool CanSign => _claim.Trim().Length > 0;

        /// <summary> Characters left in the claim. </summary>
        string RemainingClaimCharacters =>
            string.Format(RemainingClaimFormat, SignedStatementService.MaximumClaimLength - _claim.Trim().Length);

        /// <summary> What the saved block is called, named after the account it belongs to. </summary>
        string StatementFileName =>
            FileNamePrefix + Elements.Social.AddressChip.Shorten(SessionService.CurrentAddress) + FileNameSuffix;

        /// <summary> Goes back where this page is reached from, which differs by whether anybody is signed in. </summary>
        void GoBack() => NavManager.NavigateTo(SessionService.IsSignedIn ? Settings.SettingsRoute : "/");

        /// <summary> Keeps the claim in step with its box. </summary>
        /// <param name="args"> The input event carrying the box's new contents. </param>
        void HandleClaimInput(ChangeEventArgs args) => _claim = args.Value?.ToString() ?? string.Empty;

        /// <summary> Keeps the pasted text in step with its box. </summary>
        /// <param name="args"> The input event carrying the box's new contents. </param>
        void HandlePastedInput(ChangeEventArgs args) => _pastedText = args.Value?.ToString() ?? string.Empty;

        /// <summary> When one block was signed, in words. </summary>
        /// <param name="statement"> The block being drawn. </param>
        /// <returns> The line under its claim. </returns>
        static string SignedAtFor(SignedStatement statement)
            => string.Format(SignedAtFormat, RelativeTimeFormatter.FormatExact(statement.SignedAtUnixMs));

        /// <summary> Signs the typed claim. </summary>
        /// <returns> A completed task; signing is fast enough not to need a busy state. </returns>
        Task SignAsync()
        {
            if (!CanSign || SessionService.Current is not PrivateIdentity owner) return Task.CompletedTask;

            if (SignedStatementService.Write(owner, _claim) is not SignedStatement statement) return Task.CompletedTask;

            byte[] file = SignedStatementService.Serialise(statement);

            _signedText = Encoding.UTF8.GetString(file);
            _signedUri = MediaService.BuildDataUri(file, StatementContentType);

            return Task.CompletedTask;
        }

        /// <summary> Reads a chosen file and checks it if it is a block. </summary>
        /// <param name="change"> The file that was picked. </param>
        /// <returns> A task that completes once the file has been read and judged. </returns>
        async Task HandleFileChosen(InputFileChangeEventArgs change)
        {
            _checked = null;
            _checkFailure = null;

            IBrowserFile file = change.File;

            if (file.Size > LargestStatementBytes)
            {
                _checkFailure = TooLargeMessage;
                return;
            }

            using MemoryStream buffer = new();
            await file.OpenReadStream(LargestStatementBytes).CopyToAsync(buffer);

            Check(buffer.ToArray());
        }

        /// <summary> Checks whatever was pasted into the box. </summary>
        void CheckPasted() => Check(Encoding.UTF8.GetBytes(_pastedText));

        /// <summary>
        /// Reads bytes as a block and checks it.
        /// </summary>
        /// <param name="bytes"> What was chosen or pasted. </param>
        /// <remarks>
        /// A block that reads but does not verify is kept and shown with its verdict rather than refused. Somebody
        /// checking a claim has more use for "here is what it says, and it does not hold" than for silence.
        /// </remarks>
        void Check(byte[] bytes)
        {
            _checked = null;
            _isChecked = false;
            _checkFailure = null;

            if (SignedStatementService.Deserialise(bytes) is not SignedStatement statement)
            {
                _checkFailure = NotAStatementMessage;
                return;
            }

            _checked = statement;
            _isChecked = SignedStatementService.Verify(statement);
        }
    }
}
