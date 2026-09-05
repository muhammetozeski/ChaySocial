using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Which server this device talks to. The archive already lets somebody carry their work off a server and the
    /// secret already carries who they are; this is the last link in the chain — the client itself, which until now
    /// could only ever speak to the machine that served it.
    /// </summary>
    /// <remarks>
    /// The choice lives on the device and reaches no server, like every other choice in this application. Nothing
    /// about the account changes with it: the same secret opens the same address anywhere, so moving is a matter of
    /// pointing at somewhere else rather than of taking anything with you.
    /// </remarks>
    public static class HomeServerService
    {
        /// <summary> The address this app was served from, which is where it goes when no other has been chosen. </summary>
        public static string ServedFrom { get; private set; } = string.Empty;

        /// <summary> The address currently in use. </summary>
        public static string Current { get; private set; } = string.Empty;

        /// <summary> True when this device is pointed somewhere other than where the page came from. </summary>
        public static bool IsAwayFromHome => Current.Length > 0 && Current != ServedFrom;

        /// <summary> Records where this copy of the app was served from, before anything else is decided. </summary>
        /// <param name="address"> The host address the page came from. </param>
        public static void NoteServedFrom(string address)
        {
            ServedFrom = Normalise(address);
            if (Current.Length == 0) Current = ServedFrom;
        }

        /// <summary>
        /// Reads back the address this device last chose.
        /// </summary>
        /// <returns> The stored address, or empty when none was stored or what was stored is no longer usable. </returns>
        public static async Task<string> ReadAsync()
        {
            if (AppServices.LocalStore is null) return string.Empty;

            string? stored = await AppServices.LocalStore.ReadAsync(LocalStoreKeys.HomeServer);

            return stored is null ? string.Empty : Normalise(stored);
        }

        /// <summary> Notes which address is in use now, without deciding whether to keep it. </summary>
        /// <param name="address"> Address now in use, already usable. </param>
        public static void NoteInUse(string address) => Current = Normalise(address);

        /// <summary> Remembers an address for the next time this device opens the app. </summary>
        /// <param name="address"> Address to keep. </param>
        /// <returns> The address as it was stored, or empty when it was not a usable one. </returns>
        public static async Task<string> SetAsync(string address)
        {
            string usable = Normalise(address);
            if (usable.Length == 0) return string.Empty;

            Current = usable;
            if (AppServices.LocalStore is not null) await AppServices.LocalStore.WriteAsync(LocalStoreKeys.HomeServer, usable);

            return usable;
        }

        /// <summary> Forgets the chosen address, sending this device back to whoever served the page. </summary>
        /// <returns> A task that completes once the choice is gone. </returns>
        public static async Task ForgetAsync()
        {
            Current = ServedFrom;
            if (AppServices.LocalStore is not null) await AppServices.LocalStore.DeleteAsync(LocalStoreKeys.HomeServer);
        }

        /// <summary> The form an address would be used in, for a caller that wants to judge it before acting. </summary>
        /// <param name="address"> Address as it was typed. </param>
        /// <returns> Its usable form, or empty when this app cannot talk to it. </returns>
        public static string Usable(string address) => Normalise(address);

        /// <summary>
        /// The form an address is kept and used in, or empty when the text is not one this app can talk to.
        /// </summary>
        /// <param name="address"> Address as it was typed or stored. </param>
        /// <returns> An absolute http or https address ending in a slash. </returns>
        /// <remarks>
        /// The trailing slash is not tidiness. Every route in this app is relative and carries no leading slash —
        /// <c>api/documents</c> — so a base address without one silently loses its last path segment and every
        /// read comes back as 404, which looks like an empty server rather than a mistyped address.
        /// </remarks>
        static string Normalise(string address)
        {
            string trimmed = address.Trim();
            if (trimmed.Length == 0) return string.Empty;

            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed)) return string.Empty;
            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return string.Empty;

            string text = parsed.ToString();
            return text.EndsWith('/') ? text : text + '/';
        }
    }
}
