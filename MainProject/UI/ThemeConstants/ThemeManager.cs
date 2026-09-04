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
        /// <summary> Key the chosen palette's name is kept under on this device. </summary>
        const string StorageKey = "chay.theme";

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
            Apply(theme);

            if (AppServices.LocalStore is null) return;

            await AppServices.LocalStore.WriteAsync(StorageKey, theme.Name);
        }

        /// <summary>
        /// Puts back the palette this device last chose. Called once at startup; leaves the default in place when
        /// nothing was stored or the stored name no longer names a palette this build ships.
        /// </summary>
        /// <returns> A task that completes once the stored choice has been read and applied. </returns>
        public static async Task RestoreAsync()
        {
            if (AppServices.LocalStore is null) return;

            string? storedName = await AppServices.LocalStore.ReadAsync(StorageKey);
            if (storedName is null) return;

            if (AppThemes.FindByName(storedName) is AppTheme stored) Apply(stored);
        }
    }
}
