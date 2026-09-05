using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Services;
using Microsoft.AspNetCore.Components;

namespace ChaySocial.MainProject.UI.Pages
{
    /// <summary>
    /// The screen the app opens on, and the only one that works without an account. It draws a brand new account
    /// from a locally generated secret, shows that secret once so its owner can keep it, and takes a secret back
    /// from anyone returning on another device. Nothing here registers anybody anywhere: there is no email field,
    /// no password field, and no request that a server could refuse.
    /// </summary>
    public partial class Welcome
    {
        /// <summary> What the welcome card is showing. </summary>
        public enum WelcomeStage
        {
            /// <summary> The opening choice: make a new account, or hand back a secret that already exists. </summary>
            Choose,

            /// <summary> An account was just made and its secret is on screen, waiting to be written down. </summary>
            KeepSecret
        }

        /// <summary> Which of the two flows is running, so only the button that was pressed shows a throbber. </summary>
        public enum WelcomeWork
        {
            /// <summary> Nothing is running; every button accepts a tap. </summary>
            None,

            /// <summary> A brand new account is being drawn. </summary>
            Creating,

            /// <summary> A secret the reader pasted is being opened. </summary>
            Recalling
        }

        /// <summary> Where someone who already has an account belongs, and where "Take me in" leads. </summary>
        const string WallRoute = NavigationConstants.Wall.Link;

        /// <summary> The app's mark: the glass of tea its name comes from. </summary>
        const string BrandEmoji = "🍵";

        /// <summary> Line under the app name, setting the tone before the card explains anything. </summary>
        const string Tagline = "A small, warm place to think out loud.";

        /// <summary> The one sentence that explains what an account here actually is. </summary>
        const string PromiseSentence = "An account here is just a secret you keep. Nothing is registered — no email, no password, nobody to ask.";

        /// <summary> The three reassurances drawn as chips under <see cref="PromiseSentence"/>. </summary>
        static readonly (string Icon, string Text)[] Promises =
        [
            ("📭", "No email"),
            ("🔓", "No password"),
            ("🗂️", "Nothing registered")
        ];

        /// <summary> Marks the button that makes a new account. </summary>
        const string CreateEmoji = "✨";

        /// <summary> Text on the button that makes a new account. </summary>
        const string CreateLabel = "Create my account";

        /// <summary> Marks the quieter path for someone who already has an account. </summary>
        const string RecallEmoji = "🔑";

        /// <summary> Text on the quieter path for someone who already has an account. </summary>
        const string RecallToggleLabel = "I already have a secret";

        /// <summary> Arrow drawn beside <see cref="RecallToggleLabel"/>; it turns over when the panel opens. </summary>
        const string RecallCaretGlyph = "▾";

        /// <summary> Invitation shown in the empty secret box. </summary>
        const string RecallPlaceholder = "Paste your secret here — stray spaces and dashes are fine";

        /// <summary> Text on the button that opens a pasted secret. </summary>
        const string RecallSubmitLabel = "Continue";

        /// <summary> Shown when the pasted text is simply not a secret this app ever produced. </summary>
        const string UnrecognisedSecretMessage = "That does not read as a Chay Social secret. Check for a missing group of characters and try again.";

        /// <summary> Shown when opening a pasted secret threw rather than merely failing to match. </summary>
        const string RecallFailureMessage = "Something went wrong opening that account. Give it another try?";

        /// <summary> Shown when drawing a brand new account threw. </summary>
        const string CreateFailureMessage = "We couldn't draw a new account just now. Give it another try?";

        /// <summary> Marks the panel holding the freshly drawn secret. </summary>
        const string SecretEmoji = "🔐";

        /// <summary> Headline over the freshly drawn secret. </summary>
        const string SecretHeadline = "This secret is your account";

        /// <summary> Line telling the owner what to do with the secret before leaving the screen. </summary>
        const string SecretLead = "Write it down, or keep it wherever you keep the things you cannot lose.";

        /// <summary> Marks the warning beside the freshly drawn secret. </summary>
        const string SecretWarningEmoji = "⚠️";

        /// <summary> The warning itself: what the secret is worth and what losing or sharing it costs. </summary>
        const string SecretWarning = "It is the only way back into this account, and it never reaches our server. Lose it and the account is gone; share it and the account is theirs.";

        /// <summary> Text on the button that leaves this screen for the wall. </summary>
        const string EnterLabel = "Take me in";

        /// <summary> Text lines both secret boxes show before they start scrolling. </summary>
        const int SecretRowCount = 3;

        /// <summary> Diameter of the throbber that replaces a button's label while its flow runs. </summary>
        const int BusySpinnerSizePx = AppMeasures.Size.Px16;

        /// <summary> Ring thickness of that throbber, thinned so a small circle still reads as a ring. </summary>
        const int BusySpinnerBorderWidthPx = AppMeasures.Border.Medium;

        /// <summary> Inside spacing of the two large pill buttons: wide enough to stay comfortable to tap on a phone. </summary>
        static readonly string LargeButtonPadding = $"{AppMeasures.Space.Px14}px {AppMeasures.Space.Px24}px";

        /// <summary> What the card is currently showing. </summary>
        WelcomeStage _stage = WelcomeStage.Choose;

        /// <summary> Which flow is running, or <see cref="WelcomeWork.None"/> while the screen is idle. </summary>
        WelcomeWork _work = WelcomeWork.None;

        /// <summary> True while the panel holding the returning owner's secret box is open. </summary>
        bool _isRecallOpen;

        /// <summary> The secret of the account just created, shown once and never stored by this page. </summary>
        string _secretText = string.Empty;

        /// <summary> What a returning owner has pasted so far; kept as-is when a sign-in attempt fails. </summary>
        string _recalledSecret = string.Empty;

        /// <summary> Message drawn under the secret box, or null when the last attempt did not fail. </summary>
        string? _errorMessage;

        /// <summary> True while either flow is running, which locks every control on the screen. </summary>
        bool IsBusy => _work != WelcomeWork.None;

        /// <summary> Proof-of-work attempts made so far while creating an account, shown so the wait is explained rather than mysterious. </summary>
        long _proofAttempts;

        /// <summary> Line drawn under the create button while the account's proof of work is being solved. </summary>
        string ProofProgressText => string.Format(ProofProgressFormat, _proofAttempts);

        /// <summary> True while the screen should show how far the account's proof of work has come. </summary>
        bool IsSolvingProof => IsBusy && _proofAttempts > 0;

        /// <summary>
        /// Wording of the progress line; the placeholder takes the attempt count. It describes what is actually
        /// happening — drawing accounts until one is named the way somebody asked — rather than the proof of work
        /// this line originally advertised and never once appeared for.
        /// </summary>
        const string ProofProgressFormat = "Drawing accounts — {0} so far, until one is named the way you asked.";

        /// <summary> Letters the reader would like their address to begin with; empty for an ordinary account. </summary>
        string _chosenLetters = string.Empty;

        /// <summary> Label above the field asking for those letters. </summary>
        const string ChosenLettersLabel = "Would you like your address to start with something?";

        /// <summary> What the empty field invites. </summary>
        const string ChosenLettersPlaceholder = "up to two letters, optional";

        /// <summary> Line under it, saying what asking costs and what it does not. </summary>
        const string ChosenLettersNote =
            "Your device draws accounts until one is named that way. One letter takes seconds, two takes a few " +
            "minutes, and nothing is sent anywhere while it looks — leave it empty and your account is instant.";

        /// <summary> Shown when the letters could never begin an address. </summary>
        const string ChosenLettersUnusableMessage = "An address can only use a–z and 2–7, and at most two letters can be chosen.";

        /// <summary> Ties the label to the field, so tapping the label lands in the box. </summary>
        const string ChosenLettersFieldId = "welcome-chosen-letters";

        /// <summary> True when something has been typed that could not begin any address. </summary>
        bool HasUnusableLetters => _chosenLetters.Trim().Length > 0 && !ChosenAddressSearch.IsSearchable(_chosenLetters);

        /// <summary> Keeps <see cref="_chosenLetters"/> in step with the field on every keystroke. </summary>
        /// <param name="args"> The input event carrying the field's new contents. </param>
        void HandleChosenLettersInput(ChangeEventArgs args)
            => _chosenLetters = args.Value?.ToString() ?? string.Empty;

        /// <summary> Redraws the progress line as the search runs, hopping back onto the render thread to do it. </summary>
        /// <param name="attempts"> Attempts made so far. </param>
        void ReportProofProgress(long attempts)
        {
            _proofAttempts = attempts;
            InvokeAsync(StateHasChanged);
        }

        /// <summary> True when the pasted text is worth sending to <see cref="SessionService.SignInAsync"/>. </summary>
        bool CanRecall => !IsBusy && !string.IsNullOrWhiteSpace(_recalledSecret);

        /// <summary> Sends anyone who is already signed in straight to the wall, so this screen only ever greets newcomers. </summary>
        protected override void OnInitialized()
        {
            base.OnInitialized();

            if (SessionService.IsSignedIn) NavManager.NavigateTo(WallRoute);
        }

        /// <summary>
        /// Draws a brand new account and moves to the stage that shows its secret. The screen deliberately does not
        /// navigate here: the owner has not seen the secret yet, and it cannot be shown again.
        /// </summary>
        /// <returns> A task that completes once the account exists or the attempt has failed. </returns>
        async Task CreateAccountAsync()
        {
            if (IsBusy) return;

            _work = WelcomeWork.Creating;
            _errorMessage = null;

            try
            {
                // A search runs before the account exists, so nothing has been created if it is given up on and no
                // server ever learns one was attempted.
                byte[]? chosen = null;
                if (ChosenAddressSearch.IsSearchable(_chosenLetters))
                {
                    _proofAttempts = 0;
                    chosen = await ChosenAddressSearch.SearchAsync(_chosenLetters, ReportProofProgress);
                }

                _secretText = await SessionService.CreateAccountAsync(chosen);
                _stage = WelcomeStage.KeepSecret;
            }
            catch (Exception error)
            {
                _errorMessage = CreateFailureMessage;
                Log($"{nameof(Welcome)} could not create an account.\n{error}", LogLevel.Error);
            }
            finally
            {
                _work = WelcomeWork.None;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary>
        /// Opens the account behind the pasted secret and leaves for the wall. A secret that does not decode leaves
        /// the text exactly where it is, with a line saying so, so nobody has to paste it a second time.
        /// </summary>
        /// <returns> A task that completes once the session is open or the attempt has failed. </returns>
        async Task RecallAccountAsync()
        {
            if (!CanRecall) return;

            _work = WelcomeWork.Recalling;
            _errorMessage = null;

            try
            {
                if (await SessionService.SignInAsync(_recalledSecret))
                {
                    NavManager.NavigateTo(WallRoute);
                    return;
                }

                _errorMessage = UnrecognisedSecretMessage;
            }
            catch (Exception error)
            {
                _errorMessage = RecallFailureMessage;
                Log($"{nameof(Welcome)} could not open the pasted secret.\n{error}", LogLevel.Error);
            }
            finally
            {
                _work = WelcomeWork.None;
                if (!HasNavigatedAway) StateHasChanged();
            }
        }

        /// <summary> Leaves the secret stage for the wall, once the owner says they have kept their secret. </summary>
        void EnterWall() => NavManager.NavigateTo(WallRoute);

        /// <summary> Opens or closes the returning owner's panel, clearing any error left over from a previous attempt. </summary>
        void ToggleRecallPanel()
        {
            if (IsBusy) return;

            _isRecallOpen = !_isRecallOpen;
            _errorMessage = null;
        }

        /// <summary> Keeps <see cref="_recalledSecret"/> in step with the secret box on every keystroke. </summary>
        /// <param name="args"> The input event carrying the box's new contents. </param>
        void HandleRecalledSecretInput(ChangeEventArgs args)
            => _recalledSecret = args.Value?.ToString() ?? string.Empty;
    }
}
