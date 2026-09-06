using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Protection;
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

            // Wired the same way the browser build wires itself, and for the same reason: one answer to "what is
            // this app talking to". The proof-of-work client and the blob store come along, so a native build can
            // earn a writing permit and send a picture rather than only read.
            HomeServerService.NoteServedFrom(ServerAddress.Default);

            HttpClient http = new() { BaseAddress = new Uri(ServerAddress.Default) };
            ProofOfWorkClient proofOfWork = new(http);

            // The device store keeps the master seed between launches. An in-memory one threw it away on every
            // close, so a native install asked for the secret again every single time it was opened.
            AppServices.Configure(
                new HttpDocumentStore(http, proofOfWork),
                new DeviceLocalStore(),
                proofOfWork,
                new HttpBlobStore(http, proofOfWork));

            return builder.Build();
        }
    }
}
