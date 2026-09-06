namespace ChaySocial
{
    /// <summary>
    /// The one line to change before building clients for anybody but yourself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A phone and a desktop have no server of their own and, unlike the browser build, no address they were
    /// served from to fall back on. So a native client is built pointing at one, and this is where that address
    /// lives — on its own, in a file with nothing else in it, so that changing it is one edit and one publish.
    /// </para>
    /// <para>
    /// Whoever installs the client can still change it in Settings, and that choice is kept on their device. This
    /// constant is only where a fresh install starts.
    /// </para>
    /// </remarks>
    public static class ServerAddress
    {
        /// <summary>
        /// Where a freshly installed client looks for its server.
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>
        /// <term>Your own machine</term>
        /// <description>
        /// <c>http://localhost:5000/</c> — the default. Works for a desktop client sitting on the same machine as
        /// the server, and for nothing else: on a phone, localhost is the phone.
        /// </description>
        /// </item>
        /// <item>
        /// <term>The house you are in</term>
        /// <description>
        /// <c>http://192.168.1.20:5000/</c> — whatever the server printed after "Reachable at:" when it started.
        /// Everybody on that Wi-Fi can use it; nobody outside can.
        /// </description>
        /// </item>
        /// <item>
        /// <term>The internet, through a tunnel</term>
        /// <description>
        /// <c>https://something.ngrok-free.app/</c> or <c>https://something.trycloudflare.com/</c> — start the
        /// tunnel against port 5000, paste the address it hands back here, publish, hand the build out.
        /// </description>
        /// </item>
        /// <item>
        /// <term>The internet, through Tor</term>
        /// <description>
        /// <c>http://something.onion/</c> — point a hidden service at port 5000. This is the one that needs no
        /// domain, no certificate and no port forwarding, and the app already knows what a .onion address means:
        /// Settings can be told to refuse anything that is not one.
        /// </description>
        /// </item>
        /// </list>
        /// A trailing slash is required. Without it every route loses its last segment and each read comes back as
        /// a 404, which reads as an empty server rather than a mistyped address.
        /// </remarks>
        public const string Default = "http://localhost:5000/";
    }
}
