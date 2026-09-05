namespace ChaySocial.MainProject.Persistence
{
    /// <summary>
    /// Every key this application writes to the device, named in one place.
    /// </summary>
    /// <remarks>
    /// These used to be private constants scattered across the classes that owned them, and nothing could count
    /// them. That was fine until something needed to erase all of them: a key added later would have slipped
    /// quietly out of the erasing without anybody noticing. The registry exists so that adding a key and forgetting
    /// to wipe it is a change to this file rather than an omission nobody sees — one was already added between
    /// noticing the problem and fixing it.
    /// </remarks>
    public static class LocalStoreKeys
    {
        /// <summary> The master seed of the account currently open on this device. </summary>
        public const string MasterSeed = "chay.master-seed";

        /// <summary> Secrets of every account this device carries, so switching between them costs one tap. </summary>
        public const string CarriedAccounts = "chay.accounts";

        /// <summary> Whether this device refuses anything that is not Tor. </summary>
        public const string TorOnly = "chay.tor-only";

        /// <summary> The palette this device reads in. </summary>
        public const string Theme = "chay.theme";

        /// <summary> How far a line has to go before this device draws a curtain over it. </summary>
        public const string ContentGuard = "chay.content-guard";

        /// <summary> The server this device talks to. </summary>
        public const string HomeServer = "chay.home-server";

        /// <summary> Digest of the secret that empties this device instead of opening it. </summary>
        public const string DuressMark = "chay.duress";

        /// <summary> All of them, for anything that has to act on every trace this application leaves. </summary>
        public static readonly IReadOnlyList<string> All =
            [MasterSeed, CarriedAccounts, TorOnly, Theme, ContentGuard, HomeServer, DuressMark];
    }
}
