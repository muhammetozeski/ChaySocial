using System.Net.NetworkInformation;
using System.Net.Sockets;
using ChaySocial.Web.Api;
using ChaySocial.Web.Components;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace ChaySocial
{
    /// <summary>
    /// Startup of the web host. It serves two things: the Blazor application itself, and the document server the
    /// browser build reads and writes through — posts, profiles, likes and everything else the app stores.
    /// </summary>
    public class Program
    {
        /// <summary> Folder under the app's content root where stored documents live. </summary>
        const string StoredDataFolderName = "StoredDocuments";

        /// <summary> Folder under the app's content root where uploaded media lives. </summary>
        const string StoredMediaFolderName = "StoredMedia";

        /// <summary> File under the app's content root listing the accounts that have paid for a writing permit. </summary>
        const string WritingPermitFileName = "WritingPermits.txt";

        /// <summary> Path prefix every API route sits under, used to keep page-oriented middleware away from them. </summary>
        const string ApiPathPrefix = "/api";

        /// <summary>
        /// Name of the rule that lets any copy of this app call this server's API. Only the API carries it: the
        /// pages themselves are served to whoever asks for them and need no such rule.
        /// </summary>
        const string AnyClientCorsPolicy = "any-chay-client";

        /// <summary> Builds the host, registers the document store, maps both the components and the document routes, and runs. </summary>
        /// <param name="args"> Host arguments handed over by the runtime. </param>
        public static void Main(string[] args)
        {
            // Rooted at the folder the executable sits in rather than at whatever directory it happened to be
            // started from. The default is the current directory, so launching the server from a shortcut, a
            // scheduled task, or a shell parked somewhere else served no wwwroot at all — every static file came
            // back as an empty 200 and the app never started — and it wrote its documents into that other folder.
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory
            });

            // Nothing here keeps a trail of who asked for what and when. The hosting layer's own request logging
            // writes a line per request, and those lines are exactly the record this app promises not to hold, so
            // it is turned off rather than merely left unread. Warnings and errors still come through: silencing
            // a fault is not the same as declining to keep a diary of visitors.
            builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
            builder.Logging.AddFilter("Microsoft.AspNetCore.StaticFiles", LogLevel.Warning);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents()
                .AddInteractiveWebAssemblyComponents();

            // One store for the whole host: every browser talking to this server sees the same documents, which is
            // what makes two accounts on two tabs able to read each other's posts. It writes through to a folder
            // next to the app so a restart reloads what was there instead of starting empty.
            string documentDirectory = Path.Combine(builder.Environment.ContentRootPath, StoredDataFolderName);
            builder.Services.AddSingleton(new JsonDocumentStore(new DocumentFileStorage(documentDirectory)));

            // The one cost this app charges is the permit to write, and it is charged in computer time rather than
            // in an identity. One registry hands out the challenges; another remembers who has paid, so a farm
            // wanting a thousand posting accounts pays a thousand times over while a person pays once.
            builder.Services.AddSingleton<ProofChallengeRegistry>();
            builder.Services.AddSingleton(new WritingPermitRegistry(
                Path.Combine(builder.Environment.ContentRootPath, WritingPermitFileName)));

            // Media is stored beside the documents, as opaque encrypted files: the server holds the bytes and
            // cannot tell a picture from a recording, because it never receives either in the clear.
            builder.Services.AddSingleton(new BlobFileStorage(
                Path.Combine(builder.Environment.ContentRootPath, StoredMediaFolderName)));

            // A server no other copy of this app can call is not really a server anybody can move to. A write
            // carries the account header and a JSON body, so the browser sends a preflight first; without this it
            // gets nothing back and the move fails in a way that looks like the new server is empty.
            builder.Services.AddCors(cors => cors.AddPolicy(
                AnyClientCorsPolicy,
                policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            var app = builder.Build();

            int restoredDocuments = app.Services.GetRequiredService<JsonDocumentStore>().RestoreFromDisk();
            app.Logger.LogInformation("Restored {DocumentCount} documents from {Directory}.", restoredDocuments, documentDirectory);

            int restoredPermits = app.Services.GetRequiredService<WritingPermitRegistry>().RestoreFromDisk();
            app.Logger.LogInformation("Restored {PermitCount} writing permits.", restoredPermits);

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            // Only page requests get the friendly not-found screen. Without this guard the middleware also catches
            // API status codes and re-runs them through a GET-only page route, which turns a deliberate 402 from
            // the proof-of-work check into a misleading 405 by the time the client sees it.
            app.UseWhen(
                context => !context.Request.Path.StartsWithSegments(ApiPathPrefix),
                branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
            app.UseHttpsRedirection();

            // Scoped to the API alone, in the same shape as the guard above it.
            app.UseWhen(
                context => context.Request.Path.StartsWithSegments(ApiPathPrefix),
                branch => branch.UseCors(AnyClientCorsPolicy));

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapProofApi();
            app.MapWritingPermitApi();
            app.MapBlobApi();
            app.MapDocumentApi();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode()
                .AddInteractiveWebAssemblyRenderMode()
                .AddAdditionalAssemblies(
                    typeof(ChaySocial.MainProject.UI.Routes).Assembly,
                    typeof(ChaySocial.Web.Client._Imports).Assembly);

            app.Lifetime.ApplicationStarted.Register(() => AnnounceReachableAddresses(app));

            app.Run();
        }

        /// <summary>
        /// Prints every address this server can actually be reached at, once it is listening.
        /// </summary>
        /// <param name="app"> The running application, for the addresses it bound and the log to write to. </param>
        /// <remarks>
        /// Kestrel prints what it was told to bind — "http://0.0.0.0:5000" — which is the one address nothing can
        /// be typed into. Somebody bringing a phone to their own server has to know which address on this machine
        /// to point it at, and hunting for it in ipconfig is not part of running a social app.
        /// </remarks>
        static void AnnounceReachableAddresses(WebApplication app)
        {
            IServerAddressesFeature? bound = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            if (bound is null) return;

            List<string> reachable = [];

            foreach (string address in bound.Addresses)
            {
                if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? parsed)) continue;

                // A wildcard binding is every address this machine has, so it is expanded into the ones somebody
                // can actually type. Anything else was named explicitly and is printed as it stands.
                if (parsed.Host is not (WildcardHost or AnyIPv4Host))
                {
                    reachable.Add(address);
                    continue;
                }

                reachable.Add($"{parsed.Scheme}://localhost:{parsed.Port}");

                foreach (string local in ReadLocalAddresses())
                {
                    reachable.Add($"{parsed.Scheme}://{local}:{parsed.Port}");
                }
            }

            app.Logger.LogInformation("Reachable at: {Addresses}", string.Join(", ", reachable));
        }

        /// <summary> The addresses of this machine on the networks it is actually attached to. </summary>
        /// <returns> One address per usable interface, loopback and disconnected ones left out. </returns>
        static IEnumerable<string> ReadLocalAddresses()
            => NetworkInterface.GetAllNetworkInterfaces()
                .Where(card => card.OperationalStatus == OperationalStatus.Up)
                .Where(card => card.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(card => card.GetIPProperties().UnicastAddresses)
                .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.Address.ToString())
                .Distinct(StringComparer.Ordinal);

        /// <summary> How a binding to every address is written with a star. </summary>
        const string WildcardHost = "*";

        /// <summary> And how it is written as an address. </summary>
        const string AnyIPv4Host = "0.0.0.0";
    }
}
