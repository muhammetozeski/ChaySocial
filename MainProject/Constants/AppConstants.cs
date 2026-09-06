namespace ChaySocial.MainProject.Constants
{
    public static class AppConstants
    {
        /// <summary> Turns the developer mode and the test tooling on. Must be false in anything that ships. </summary>
        public const bool TestBuild = false;

        public const string AppName = "ChaySocial";
        public const string AppNameHumanReadable = "Chay Social";

        public const string GuestDisplayName = "Guest";

        public static class Avatar
        {
            public const string Guest = "👤";
            public const string DefaultUser = "🙂";
        }

        public const string LoginMenuTitle = AppNameHumanReadable;
        public const string LoginMenuSubtitle = AppTagline;
        public const string AppTagline = "Welcome to " + AppNameHumanReadable;
        public const string ShareTextHeader = "Check out " + AppNameHumanReadable + "! Join me! 🚀";

        public static class WebViewErrors
        {
            public const string Title = "WebView Engine Error";
            public const string Description = "A valid WebView engine was not found or is outdated. Please update or install a compatible browser to continue.";
            public const string UpdateAndroidWebView = "Install or Update Android System WebView to the latest version";
            public const string ChromeFallbackText = "If the issue persists, try installing Google Chrome:";
            public const string UpdateChrome = "Install or Update Google Chrome to the latest version";
        }

        public static class Urls
        {
            public const string PlayStoreWebView = "https://play.google.com/store/apps/details?id=com.google.android.webview";
            public const string PlayStoreChrome = "https://play.google.com/store/apps/details?id=com.android.chrome";
        }
    }
}
