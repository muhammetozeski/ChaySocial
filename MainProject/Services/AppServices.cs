using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Where the app is wired to the outside world. Everything else asks these two fields instead of constructing a
    /// store, so moving from the in-memory server to Firestore, or from browser storage to a file, is a change in
    /// the host's startup and nowhere else.
    /// </summary>
    public static class AppServices
    {
        /// <summary> Where posts, profiles and likes live. </summary>
        public static IDocumentStore Documents = default!;

        /// <summary> Where this device keeps what must never be sent anywhere — the master seed. </summary>
        public static ILocalStore LocalStore = default!;

        /// <summary> True once the host has supplied both stores. </summary>
        public static bool IsConfigured => Documents is not null && LocalStore is not null;

        /// <summary> Supplies the stores the app runs against. Called once, by the host, before the first page renders. </summary>
        /// <param name="documents"> Store holding posts, profiles and likes. </param>
        /// <param name="localStore"> Device storage for the master seed. </param>
        public static void Configure(IDocumentStore documents, ILocalStore localStore)
        {
            Documents = documents;
            LocalStore = localStore;
        }
    }
}
