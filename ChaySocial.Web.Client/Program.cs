using Blazored.LocalStorage;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Protection;
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
            ProofOfWorkClient proofOfWork = new(http);

            AppServices.Configure(
                new HttpDocumentStore(http, proofOfWork),
                new BrowserLocalStore(host.Services.GetRequiredService<ILocalStorageService>()),
                proofOfWork,
                new HttpBlobStore(http, proofOfWork));

            // The stored session is read by MainLayout on its first render, not here: reaching browser storage
            // needs the app to be running, and doing it before RunAsync leaves every visitor looking signed out.
            await host.RunAsync();
        }
    }
}
