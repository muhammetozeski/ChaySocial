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
        /// Publishes where somebody can send this account a little money, signed so nobody can put their own
        /// address there instead. Passing an empty address takes the offer down again.
        /// </summary>
        /// <param name="owner"> The unlocked account whose profile it is. </param>
        /// <param name="currency"> Which chain the address belongs to. </param>
        /// <param name="tipAddress"> The payment address; trimmed, and refused when it is too long to be one. </param>
        /// <returns> The stored profile, or null when there was no profile to change or the address was unusable. </returns>
        public static async Task<ProfileData?> SetTipAddressAsync(PrivateIdentity owner, string currency, string tipAddress)
        {
            ProfileData? profile = await ReadAsync(owner.Public.Address);
            if (profile is null) return null;

            string trimmedAddress = tipAddress.Trim();
            string trimmedCurrency = currency.Trim();

            if (trimmedAddress.Length > ProfileData.MaximumTipAddressLength) return null;

            // Taking the offer down clears the signature with it, so nothing is left that could be replayed.
            bool takingItDown = trimmedAddress.Length == 0 || trimmedCurrency.Length == 0;

            ProfileData updated = takingItDown
                ? profile with { TipCurrency = string.Empty, TipAddress = string.Empty, TipSignature = string.Empty }
                : profile with
                {
                    TipCurrency = trimmedCurrency,
                    TipAddress = trimmedAddress,
                    TipSignature = Convert.ToBase64String(
                        owner.Sign(BuildTipTranscript(owner.Public.Address, trimmedCurrency, trimmedAddress)))
                };

            await SaveAsync(updated);
            return updated;
        }

        /// <summary>
        /// Checks that a payment address really was published by the account whose profile it sits in. A profile
        /// that fails this is drawn without any way to send money, because the alternative is sending it to
        /// whoever tampered with the record.
        /// </summary>
        /// <param name="profile"> Profile to check, or null. </param>
        /// <returns> True when the address is present and its signature holds. </returns>
        public static bool VerifyTipAddress(ProfileData? profile)
        {
            if (profile is null || !profile.AcceptsTips || profile.TipSignature.Length == 0) return false;

            try
            {
                byte[] signingKey = Convert.FromBase64String(profile.SigningPublicKey);
                byte[] encryptionKey = Convert.FromBase64String(profile.EncryptionPublicKey);

                // The address commits to the keys, so checking that first is what stops somebody publishing a
                // profile full of their own keys under another account's name.
                if (!Cryptography.AppCryptography.Addresses.Matches(profile.Address, signingKey, encryptionKey)) return false;

                PublicIdentity owner = new(profile.Address, signingKey, encryptionKey);
                byte[] transcript = BuildTipTranscript(profile.Address, profile.TipCurrency, profile.TipAddress);

                return Cryptography.AppCryptography.Identities.Verify(
                    transcript, Convert.FromBase64String(profile.TipSignature), owner);
            }
            catch (FormatException error)
            {
                Log($"Profile '{profile.Address}' carries a malformed payment address.\n{error}", LogLevel.Warning);
                return false;
            }
        }

        /// <summary> Separates this signature from every other one the app produces. </summary>
        static readonly byte[] TipSignatureDomain = "ChaySocial/Tip/v1"u8.ToArray();

        /// <summary> Builds the exact bytes an owner signs and a reader verifies. </summary>
        /// <param name="accountAddress"> The account publishing the offer. </param>
        /// <param name="currency"> Which chain the address belongs to. </param>
        /// <param name="tipAddress"> The payment address. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTipTranscript(string accountAddress, string currency, string tipAddress)
        {
            Text.TranscriptWriter transcript = new();
            transcript.WriteBytes(TipSignatureDomain);
            transcript.WriteText(accountAddress);
            transcript.WriteText(currency);
            transcript.WriteText(tipAddress);
            return transcript.ToArray();
        }

        /// <summary>
        /// Returns the account's profile, creating a starter one the first time. Signing in on a second device
        /// therefore finds the existing profile instead of overwriting it.
        /// </summary>
        /// <param name="identity"> The account whose profile is needed. </param>
        /// <returns> The stored profile, or the freshly created one. </returns>
        public static async Task<ProfileData> EnsureExistsAsync(PublicIdentity identity)
        {
            ProfileData? existing = await ReadAsync(identity.Address);
            if (existing is not null) return existing;

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
