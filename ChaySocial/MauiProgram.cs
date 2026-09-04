using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Services;
using Microsoft.Extensions.Logging;

namespace ChaySocial
{
    /// <summary>
    /// Startup of the native host. Alongside the usual MAUI wiring it names the two stores the app runs against,
    /// because the first page rendered inside the web view reads a profile through <see cref="AppServices"/> and
    /// would fault on an unconfigured store.
    /// </summary>
    public static class MauiProgram
    {
        /// <summary>
        /// Where the native build reads and writes documents. The device has no server of its own, so it talks to the
        /// same web host the browser build is served from.
        /// </summary>
        const string DocumentServerBaseAddress = "https://localhost:7189/";

        /// <summary> Builds the MAUI application and configures the stores before any page can render. </summary>
        /// <returns> The application to run. </returns>
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            HttpClient http = new() { BaseAddress = new Uri(DocumentServerBaseAddress) };
            AppServices.Configure(new HttpDocumentStore(http), new InMemoryLocalStore());

            return builder.Build();
        }
    }
}
