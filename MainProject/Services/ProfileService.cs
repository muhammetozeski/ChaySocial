using System.Text;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Reads and writes the public profile that sits behind an address. A profile is only a label: it is stored by
    /// the account itself, and the address — not the name — is what actually identifies anyone.
    /// </summary>
    public static class ProfileService
    {
        /// <summary> Emoji a new account is given, picked from its address so the same account always gets the same one. </summary>
        static readonly string[] AvatarPalette =
        [
            "🫖", "🌸", "🍡", "🐣", "🍋", "🪼", "🌙", "🧁", "🐧", "🍄",
            "🪷", "🍑", "🐳", "☁️", "🌻", "🧸", "🍵", "🦊", "🫧", "🌈"
        ];

        /// <summary> How many address characters become the fallback display name. </summary>
        const int FallbackNameLength = 8;

        /// <summary> Fetches the profile stored for an address. </summary>
        /// <param name="address"> Account address. </param>
        /// <returns> The profile, or null when that account has never published one. </returns>
        public static Task<ProfileData?> ReadAsync(string address)
            => AppServices.Documents.ReadAsync(new DocumentId<ProfileData>(address));

        /// <summary> Stores a profile and tells the app it changed. </summary>
        /// <param name="profile"> Profile to store. </param>
        public static async Task SaveAsync(ProfileData profile)
        {
            await AppServices.Documents.WriteAsync(profile.Id, profile);
            MainEvents.Trigger(MainEvents.Names.ProfileChanged, profile.Address);
        }

        /// <summary>
        /// Returns the account's profile, creating a starter one the first time. Signing in on a second device
        /// therefore finds the existing profile instead of overwriting it.
        /// </summary>
        /// <param name="identity"> The account whose profile is needed. </param>
        /// <param name="onProofAttempt"> Reports proof-of-work attempts while an account is being brought into being, for a progress display. </param>
        /// <returns> The stored profile, or the freshly created one. </returns>
        public static async Task<ProfileData> EnsureExistsAsync(PublicIdentity identity, Action<long>? onProofAttempt = null)
        {
            ProfileData? existing = await ReadAsync(identity.Address);
            if (existing is not null) return existing;

            // Publishing a profile for an address the server has never seen is what creates an account, and the
            // server charges the heavier proof for it. This runs here rather than at the call site so every path
            // that brings an account into being pays — signing in on a second device as much as creating one.
            if (AppServices.ProofOfWork is not null)
            {
                await AppServices.ProofOfWork.ReserveAccountAnswerAsync(onProofAttempt);
            }

            ProfileData created = new()
            {
                Address = identity.Address,
                DisplayName = FallbackDisplayName(identity.Address),
                Avatar = PickAvatar(identity.Address),
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SigningPublicKey = Convert.ToBase64String(identity.SigningPublicKey),
                EncryptionPublicKey = Convert.ToBase64String(identity.EncryptionPublicKey)
            };

            await SaveAsync(created);
            return created;
        }

        /// <summary> Names a brand new account after the readable part of its address. </summary>
        /// <param name="address"> Account address. </param>
        /// <returns> A short starter name the owner can change. </returns>
        public static string FallbackDisplayName(string address)
        {
            string withoutPrefix = address.StartsWith(Cryptography.AppCryptography.AddressPrefix, StringComparison.Ordinal)
                ? address[Cryptography.AppCryptography.AddressPrefix.Length..]
                : address;

            return withoutPrefix[..Math.Min(FallbackNameLength, withoutPrefix.Length)];
        }

        /// <summary> Picks the starter emoji for an address, deterministically so it never changes under the owner. </summary>
        /// <param name="address"> Account address. </param>
        /// <returns> One emoji from <see cref="AvatarPalette"/>. </returns>
        public static string PickAvatar(string address)
        {
            int total = 0;
            foreach (byte value in Encoding.UTF8.GetBytes(address)) total += value;

            return AvatarPalette[total % AvatarPalette.Length];
        }
    }
}
