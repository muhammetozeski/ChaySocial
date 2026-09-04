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
            // what makes two accounts on two tabs able to read each other's posts.
            builder.Services.AddSingleton<JsonDocumentStore>();

            var app = builder.Build();

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

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
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
