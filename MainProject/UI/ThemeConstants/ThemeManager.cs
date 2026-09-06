using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Services;

namespace ChaySocial.MainProject.Constants.ThemeConstants
{
    /// <summary>
    /// Single source of truth for the active <see cref="AppTheme"/>. Holds the running palette, notifies the UI
    /// when it swaps so every render reflects the new colors immediately, and remembers the choice on the device
    /// so it survives a refresh.
    /// </summary>
    public static class ThemeManager
    {
        public static AppTheme Current { get; private set; } = AppThemes.PlayfulStarlight;

        /// <summary>
        /// Replace the active palette. Fires <see cref="Events.MainEvents"/> which the root layout subscribes to
        /// so the entire component tree re-renders against the new theme.
        /// </summary>
        public static void Apply(AppTheme theme)
        {
            if (theme == Current) return;

            Current = theme;
            Events.MainEvents.Trigger(Events.MainEvents.Names.ThemeChanged, theme.Name);
        }

        /// <summary> Applies a palette and writes the choice to the device, so the next visit opens in it. </summary>
        /// <param name="theme"> Palette to switch to. </param>
        /// <returns> A task that completes once the choice has been stored. </returns>
        public static async Task ApplyAndRememberAsync(AppTheme theme)
        {
            // Choosing any palette this way ends whatever identity palette was in force; the account's own colours
            // are put back only by the call that builds them.
            _baseThemeName = null;

            Apply(theme);

            if (AppServices.LocalStore is null) return;

            await AppServices.LocalStore.WriteAsync(LocalStoreKeys.Theme, theme.Name);
        }

        /// <summary>
        /// Applies the palette an account's own address deals, and remembers that it was chosen.
        /// </summary>
        /// <param name="address"> Address of the account wearing it. </param>
        /// <param name="baseTheme"> The shipped palette it is built from. </param>
        /// <returns> A task that completes once the choice has been stored. </returns>
        public static async Task ApplyForAccountAsync(string address, AppTheme baseTheme)
        {
            await ApplyAndRememberAsync(IdentityPalette.BuildFrom(address, baseTheme));

            // Recorded after the store, because storing a palette is also how a shipped one is chosen — and that
            // has to be able to turn this off.
            _baseThemeName = baseTheme.Name;
        }

        /// <summary>
        /// Repaints in the signed-in account's own colours, if that is what this device chose.
        /// </summary>
        /// <returns> A task that completes once the palette is in step with whoever is signed in. </returns>
        /// <remarks>
        /// Called after the session is opened rather than while the stored palette is read back, because the
        /// address is not known at that point — the palette is restored before the account is. Called again
        /// whenever the session changes, so switching between two accounts carried on one device repaints the
        /// whole app rather than leaving the previous account's colours on screen.
        /// </remarks>
        public static async Task ReapplyForCurrentAccountAsync()
        {
            if (_baseThemeName is null || !SessionService.IsSignedIn) return;
            if (AppThemes.FindByName(_baseThemeName) is not AppTheme baseTheme) return;

            await ApplyForAccountAsync(SessionService.CurrentAddress, baseTheme);
        }

        /// <summary>
        /// Puts back the palette this device last chose. Called once at startup; leaves the default in place when
        /// nothing was stored or the stored name no longer names a palette this build ships.
        /// </summary>
        /// <returns> A task that completes once the stored choice has been read and applied. </returns>
        public static async Task RestoreAsync()
        {
            if (AppServices.LocalStore is null) return;

            string? storedName = await AppServices.LocalStore.ReadAsync(LocalStoreKeys.Theme);
            if (storedName is null) return;

            if (AppThemes.FindByName(storedName) is AppTheme stored)
            {
                _baseThemeName = null;
                Apply(stored);
                return;
            }

            // An identity palette cannot be rebuilt here: it needs the address, and the account is opened a moment
            // after this runs. Its base is applied now so the app does not open in the wrong colours entirely, and
            // the account's own turn of them arrives as soon as the session does.
            if (IdentityPalette.BaseThemeOf(storedName) is AppTheme identityBase)
            {
                _baseThemeName = identityBase.Name;
                Apply(identityBase);
            }
        }

        /// <summary> Name of the shipped palette an identity palette is built from, or null while none was chosen. </summary>
        static string? _baseThemeName;
    }
}
