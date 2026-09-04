using ChaySocial.Web.Api;
using ChaySocial.Web.Components;

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

        /// <summary> Builds the host, registers the document store, maps both the components and the document routes, and runs. </summary>
        /// <param name="args"> Host arguments handed over by the runtime. </param>
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

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

            app.Run();
        }
    }
}
