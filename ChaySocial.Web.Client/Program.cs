using Blazored.LocalStorage;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using static Logger;

namespace ChaySocial.Web.Client
{
    /// <summary>
    /// Startup of the browser host. Everything the app does with data goes through <see cref="AppServices"/>, so the
    /// only wiring here is naming the two stores the WebAssembly build runs against: documents over HTTP against the
    /// site this page was served from, and the master seed in the browser's own storage, where it stays on this
    /// device and is never sent anywhere.
    /// </summary>
    internal class Program
    {
        /// <summary> Configures the stores, reopens any session this device already holds, then starts Blazor. </summary>
        /// <param name="args"> Host arguments handed over by the runtime. </param>
        /// <returns> A task that completes when the host stops. </returns>
        static async Task Main(string[] args)
        {
            WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.Services.AddBlazoredLocalStorage();

            WebAssemblyHost host = builder.Build();

            HttpClient http = new() { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
            AppServices.Configure(
                new HttpDocumentStore(http),
                new BrowserLocalStore(host.Services.GetRequiredService<ILocalStorageService>()));

            // A session that cannot be reopened is not a startup failure: the app opens signed out and the welcome
            // screen asks for the seed again. Swallowing it here is what keeps a bad stored seed from blocking boot.
            try
            {
                await SessionService.RestoreAsync();
            }
            catch (Exception error)
            {
                Log($"Restoring the stored session failed; starting signed out.\n{error}", Logger.LogLevel.Error);
            }

            await host.RunAsync();
        }
    }
}
