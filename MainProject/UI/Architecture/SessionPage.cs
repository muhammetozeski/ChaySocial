using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Services;

namespace ChaySocial.MainProject.UI.Layout.Architecture
{
    /// <summary>
    /// A page that only makes sense for someone who has an account. Sends anyone without one to the welcome screen
    /// before loading anything, so no subclass has to null-check the session, and re-draws itself when the signed-in
    /// account changes.
    /// </summary>
    public abstract class SessionPage : LoadablePage
    {
        /// <summary> Where someone without an account is sent. </summary>
        protected const string WelcomeRoute = "/";

        /// <summary> The signed-in account. Only read after the guard has run, which is why it is not nullable here. </summary>
        protected PrivateIdentity Account => SessionService.Current!;

        /// <summary> Profile of the signed-in account. </summary>
        protected ProfileData Profile => SessionService.CurrentProfile!;

        protected override string[] ReloadOnEvents => [MainEvents.Names.SessionChanged];

        protected override async Task OnInitializedAsync()
        {
            if (!SessionService.IsSignedIn)
            {
                NavManager.NavigateTo(WelcomeRoute);
                return;
            }

            await base.OnInitializedAsync();
        }
    }
}
