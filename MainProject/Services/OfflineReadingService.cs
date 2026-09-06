using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Whether the app is reading a sealed file instead of a server, and how to go back.
    /// </summary>
    /// <remarks>
    /// This is the only state in the app that says "there is nothing to talk to". The screens ask it so they can
    /// stop offering what cannot happen: a post that cannot be published, an image whose bytes stayed on a server,
    /// a like nobody would ever see. Saying so plainly is better than a button that fails quietly.
    /// </remarks>
    public static class OfflineReadingService
    {
        /// <summary> True while the app is reading an archive rather than a server. </summary>
        public static bool IsReading { get; private set; }

        /// <summary> When the archive being read was sealed, or zero while a server is being talked to. </summary>
        public static long SealedAtUnixMs { get; private set; }

        /// <summary>
        /// Points the whole app at a sealed archive. Nothing is asked of any server after this: the proof of work
        /// and the blob store are left unwired, because there is nothing to prove anything to and no bytes to fetch.
        /// </summary>
        /// <param name="archive"> The archive to read, already checked against the seal its owner signed. </param>
        public static void Read(AccountArchive archive)
        {
            AppServices.Configure(new ArchiveDocumentStore(archive), AppServices.LocalStore);

            IsReading = true;
            SealedAtUnixMs = archive.SealedAtUnixMs;

            MainEvents.Trigger(MainEvents.Names.OfflineReadingChanged);
        }

        /// <summary> Points the app back at the server it was talking to before. </summary>
        /// <returns> True once the stores are wired to that server again. </returns>
        public static async Task<bool> TalkToAServerAgainAsync()
        {
            bool wired = await StoreWiring.ApplyAsync(HomeServerService.Current);
            if (!wired) return false;

            IsReading = false;
            SealedAtUnixMs = 0;

            MainEvents.Trigger(MainEvents.Names.OfflineReadingChanged);
            return true;
        }
    }
}
