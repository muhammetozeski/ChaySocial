using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Identity
{
    /// <summary>
    /// The arithmetic behind an address, worked out again from a profile's own published keys so a reader can watch
    /// it happen rather than take it on faith.
    /// </summary>
    /// <param name="SigningKeyByteCount"> How long the published signing key is. </param>
    /// <param name="EncryptionKeyByteCount"> How long the published encryption key is. </param>
    /// <param name="FingerprintByteCount"> How many bytes of hash the address carries. </param>
    /// <param name="ChecksumByteCount"> How many bytes of typo check sit after the fingerprint. </param>
    /// <param name="VersionByteCount"> How many bytes name the address layout; the last byte before encoding. </param>
    /// <param name="AddressVersion"> Which layout this address was built to. </param>
    /// <param name="RebuiltAddress"> The address those two keys produce, computed on this device. </param>
    /// <param name="ClaimedAddress"> The address the profile is stored under. </param>
    public readonly record struct AddressBindingProof(
        int SigningKeyByteCount,
        int EncryptionKeyByteCount,
        int FingerprintByteCount,
        int ChecksumByteCount,
        int VersionByteCount,
        byte AddressVersion,
        string RebuiltAddress,
        string ClaimedAddress)
    {
        /// <summary> How many bytes go through Base32 to become the readable part of the address. </summary>
        public int EncodedByteCount => FingerprintByteCount + ChecksumByteCount + VersionByteCount;

        /// <summary> True when the keys really do produce the address the profile is stored under. </summary>
        public bool Holds =>
            RebuiltAddress.Length > 0 && string.Equals(RebuiltAddress, ClaimedAddress, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rebuilding an account's address from the two public keys its profile publishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole identity story in this app is one sentence: the address is a commitment to the keys. Every
    /// signature check already runs it — <c>AppCryptography.Identities.Verify</c> matches the address against the
    /// keys before it looks at a signature at all — but running it is not the same as showing it, and a reader has
    /// no way to watch a check that happens inside a method.
    /// </para>
    /// <para>
    /// Nothing here is a second gate. It is the same arithmetic, laid out with its numbers so somebody can follow
    /// it: two keys of a stated length, a fingerprint of a stated length, a checksum, a version byte, Base32.
    /// </para>
    /// </remarks>
    public static class AddressBinding
    {
        /// <summary> The one byte at the end of the encoded part that names the address layout. </summary>
        const int VersionByteCount = 1;

        /// <summary> Works the address out again from what a profile publishes. </summary>
        /// <param name="profile"> The profile to read, or null when none was found. </param>
        /// <returns> The arithmetic and its outcome, or null when the profile carries nothing that can be read as keys. </returns>
        /// <remarks>
        /// A profile whose keys are not base64, or whose address is not one this app could have produced, yields
        /// null rather than a failing proof: there is nothing to show the reader the working of.
        /// </remarks>
        public static AddressBindingProof? Read(ProfileData? profile)
        {
            if (profile is null) return null;

            try
            {
                byte[] signingKey = Convert.FromBase64String(profile.SigningPublicKey);
                byte[] encryptionKey = Convert.FromBase64String(profile.EncryptionPublicKey);

                if (signingKey.Length == 0 || encryptionKey.Length == 0) return null;

                string rebuilt = AppCryptography.Addresses.Create(signingKey, encryptionKey);

                // Read back out of the rebuilt address rather than out of the factory's own constants: what the
                // panel shows is then the address the reader is looking at, not a number written down beside it.
                if (!AppCryptography.Addresses.TryGetFingerprint(rebuilt, out byte[] fingerprint)) return null;
                if (!Base32.TryDecode(rebuilt[AppCryptography.Addresses.Prefix.Length..], out byte[] encoded)) return null;

                return new AddressBindingProof(
                    signingKey.Length,
                    encryptionKey.Length,
                    fingerprint.Length,
                    encoded.Length - fingerprint.Length - VersionByteCount,
                    VersionByteCount,
                    encoded[^1],
                    rebuilt,
                    profile.Address);
            }
            catch (FormatException error)
            {
                Log($"{nameof(AddressBinding)} was handed a profile with malformed base64.\n{error}", LogLevel.Warning);
                return null;
            }
        }
    }
}
