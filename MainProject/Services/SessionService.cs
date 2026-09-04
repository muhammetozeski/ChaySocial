using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Who is signed in on this device. Creating an account is one call and reaches no server for permission: the
    /// seed is drawn locally, the keys and address come out of it, and the only thing that ever leaves is the public
    /// profile. Signing in is the same seed producing the same address again — there is no password to check.
    /// </summary>
    public static class SessionService
    {
        /// <summary> Key the master seed is kept under on this device. </summary>
        const string SeedStorageKey = "chay.master-seed";

        /// <summary> The unlocked account, or null when nobody is signed in on this device. </summary>
        public static PrivateIdentity? Current { get; private set; }

        /// <summary> Profile of the signed-in account, kept alongside so the UI does not re-read it on every render. </summary>
        public static ProfileData? CurrentProfile { get; private set; }

        /// <summary> True while an account is unlocked. </summary>
        public static bool IsSignedIn => Current is not null;

        /// <summary> Address of the signed-in account, or empty when nobody is signed in. </summary>
        public static string CurrentAddress => Current?.Public.Address ?? string.Empty;

        /// <summary>
        /// Restores the session from the seed this device already holds. Called once when the app starts; does
        /// nothing when no seed was stored.
        /// </summary>
        /// <returns> True when a session was restored. </returns>
        public static async Task<bool> RestoreAsync()
        {
            string? storedSeed = await AppServices.LocalStore.ReadAsync(SeedStorageKey);
            return MasterSeedText.TryParse(storedSeed, out byte[] masterSeed) && await AdoptAsync(masterSeed, remember: false);
        }

        /// <summary> Creates a brand new account and signs into it. </summary>
        /// <returns> The seed text to show the owner once, so they can keep it. </returns>
        public static async Task<string> CreateAccountAsync()
        {
            byte[] masterSeed = IdentityScheme.CreateMasterSeed();
            await AdoptAsync(masterSeed, remember: true);
            return MasterSeedText.Format(masterSeed);
        }

        /// <summary> Signs in with a seed the owner kept from an earlier session or another device. </summary>
        /// <param name="secretText"> The seed text, in any spacing or letter case. </param>
        /// <returns> True when the text was a valid seed and the session opened. </returns>
        public static async Task<bool> SignInAsync(string secretText)
            => MasterSeedText.TryParse(secretText, out byte[] masterSeed) && await AdoptAsync(masterSeed, remember: true);

        /// <summary> Forgets the account on this device. The account itself is untouched and reopens with the same seed. </summary>
        public static async Task SignOutAsync()
        {
            Current = null;
            CurrentProfile = null;

            await AppServices.LocalStore.DeleteAsync(SeedStorageKey);
            MainEvents.Trigger(MainEvents.Names.SessionChanged, null);
        }

        /// <summary>
        /// Hands back the signed-in account's seed so it can be shown or written down. Anyone holding these
        /// characters holds the account.
        /// </summary>
        /// <returns> The seed text, or empty when nobody is signed in. </returns>
        public static string ExportSecretText()
            => Current is null ? string.Empty : MasterSeedText.Format(Current.ExportMasterSeed());

        /// <summary> Replaces the current profile after the owner edits it, without re-reading it from the server. </summary>
        /// <param name="profile"> The profile that was just saved. </param>
        public static void UpdateCurrentProfile(ProfileData profile)
        {
            if (Current?.Public.Address != profile.Address) return;

            CurrentProfile = profile;
            MainEvents.Trigger(MainEvents.Names.SessionChanged, Current.Public);
        }

        /// <summary> Opens an account from a seed, makes sure it has a profile, and optionally keeps the seed on this device. </summary>
        /// <param name="masterSeed"> The account's master seed. </param>
        /// <param name="remember"> True writes the seed to device storage so a refresh keeps the session. </param>
        /// <returns> True once the session is open. </returns>
        static async Task<bool> AdoptAsync(byte[] masterSeed, bool remember)
        {
            PrivateIdentity identity = AppCryptography.Identities.Open(masterSeed);

            Current = identity;
            CurrentProfile = await ProfileService.EnsureExistsAsync(identity.Public);

            if (remember) await AppServices.LocalStore.WriteAsync(SeedStorageKey, MasterSeedText.Format(masterSeed));

            MainEvents.Trigger(MainEvents.Names.SessionChanged, identity.Public);
            return true;
        }
    }
}
