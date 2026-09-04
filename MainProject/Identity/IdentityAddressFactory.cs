using ChaySocial.MainProject.Text;
using Org.BouncyCastle.Crypto.Digests;

namespace ChaySocial.MainProject.Identity
{
    /// <summary>
    /// Turns a pair of public keys into the short text that names an account, the way a Tor v3 address names a
    /// service: the name <em>is</em> a commitment to the keys, so nobody can hand out someone else's name. The keys
    /// themselves are kilobytes long, so the address commits to their hash rather than carrying them, and a built-in
    /// checksum catches a mistyped address before it reaches the network.
    /// </summary>
    /// <param name="prefix"> Short label written in front of the encoded part so an address is recognizable at a glance. Never enters the hash. </param>
    public sealed class IdentityAddressFactory(string prefix)
    {
        /// <summary> Bumped when the address layout changes, so old and new addresses can never be confused. </summary>
        public const byte AddressVersion = 1;

        /// <summary> 160 bits of key fingerprint — the same margin Tor v3 checksums protect, far past any collision an attacker could search for. </summary>
        const int FingerprintBytes = 20;

        const int ChecksumBytes = 2;
        const int EncodedBytes = FingerprintBytes + ChecksumBytes + 1;

        static readonly byte[] FingerprintDomain = "Groundwork/IdentityAddress/fingerprint/v1"u8.ToArray();
        static readonly byte[] ChecksumDomain = "Groundwork/IdentityAddress/checksum/v1"u8.ToArray();

        /// <summary> The label this factory writes in front of every address it produces. </summary>
        public string Prefix => prefix;

        /// <summary> Builds the address that names this pair of public keys. </summary>
        /// <param name="signingPublicKey"> Public key that verifies the account's signatures. </param>
        /// <param name="encryptionPublicKey"> Public key others encapsulate to when sending to the account. </param>
        /// <returns> The full address, prefix included. </returns>
        public string Create(ReadOnlySpan<byte> signingPublicKey, ReadOnlySpan<byte> encryptionPublicKey)
        {
            byte[] fingerprint = Fingerprint(signingPublicKey, encryptionPublicKey);

            byte[] encoded = new byte[EncodedBytes];
            fingerprint.CopyTo(encoded, 0);
            Checksum(fingerprint).CopyTo(encoded, FingerprintBytes);
            encoded[^1] = AddressVersion;

            return prefix + Base32.Encode(encoded);
        }

        /// <summary>
        /// Checks that an address is one this factory could have produced: right prefix, right length, right version,
        /// and a checksum that matches. Catches typos without needing the keys, but says nothing about who owns it.
        /// </summary>
        /// <param name="address"> Address to check. </param>
        /// <returns> True when the address is structurally valid. </returns>
        public bool IsWellFormed(string address)
            => TryReadFingerprint(address, out _);

        /// <summary>
        /// Checks that an address really names this pair of public keys. This is what lets a server accept a login
        /// without ever holding a secret: it recomputes the address from the keys the client presented and compares.
        /// </summary>
        /// <param name="address"> Address the client claims. </param>
        /// <param name="signingPublicKey"> Signing key the client presented. </param>
        /// <param name="encryptionPublicKey"> Encryption key the client presented. </param>
        /// <returns> True when the keys hash to the address. </returns>
        public bool Matches(string address, ReadOnlySpan<byte> signingPublicKey, ReadOnlySpan<byte> encryptionPublicKey)
            => TryReadFingerprint(address, out byte[] fingerprint)
               && fingerprint.AsSpan().SequenceEqual(Fingerprint(signingPublicKey, encryptionPublicKey));

        /// <summary> Decodes an address and verifies its checksum and version. </summary>
        /// <param name="address"> Address to decode. </param>
        /// <param name="fingerprint"> Receives the key fingerprint, or an empty array when the address is malformed. </param>
        /// <returns> True when the address decoded and its checksum matched. </returns>
        bool TryReadFingerprint(string address, out byte[] fingerprint)
        {
            fingerprint = [];

            if (!address.StartsWith(prefix, StringComparison.Ordinal)) return false;
            if (!Base32.TryDecode(address[prefix.Length..], out byte[] decoded)) return false;
            if (decoded.Length != EncodedBytes || decoded[^1] != AddressVersion) return false;

            byte[] candidate = decoded[..FingerprintBytes];
            if (!decoded.AsSpan(FingerprintBytes, ChecksumBytes).SequenceEqual(Checksum(candidate))) return false;

            fingerprint = candidate;
            return true;
        }

        /// <summary> Hashes both public keys and the version into the fingerprint the address encodes. </summary>
        /// <param name="signingPublicKey"> Public key that verifies signatures. </param>
        /// <param name="encryptionPublicKey"> Public key others encapsulate to. </param>
        /// <returns> Exactly <see cref="FingerprintBytes"/> bytes. </returns>
        static byte[] Fingerprint(ReadOnlySpan<byte> signingPublicKey, ReadOnlySpan<byte> encryptionPublicKey)
        {
            ShakeDigest digest = new(256);
            digest.BlockUpdate(FingerprintDomain, 0, FingerprintDomain.Length);
            digest.BlockUpdate(signingPublicKey);
            digest.BlockUpdate(encryptionPublicKey);
            digest.Update(AddressVersion);

            byte[] fingerprint = new byte[FingerprintBytes];
            digest.OutputFinal(fingerprint, 0, fingerprint.Length);
            return fingerprint;
        }

        /// <summary> Derives the typo-detecting checksum carried inside the address. </summary>
        /// <param name="fingerprint"> The key fingerprint being protected. </param>
        /// <returns> Exactly <see cref="ChecksumBytes"/> bytes. </returns>
        static byte[] Checksum(ReadOnlySpan<byte> fingerprint)
        {
            ShakeDigest digest = new(256);
            digest.BlockUpdate(ChecksumDomain, 0, ChecksumDomain.Length);
            digest.BlockUpdate(fingerprint);
            digest.Update(AddressVersion);

            byte[] checksum = new byte[ChecksumBytes];
            digest.OutputFinal(checksum, 0, checksum.Length);
            return checksum;
        }
    }
}

