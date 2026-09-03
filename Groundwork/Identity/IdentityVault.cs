using System.Text;
using Groundwork.Cryptography;
using Groundwork.Text;

namespace Groundwork.Identity
{
    /// <summary>
    /// A master seed encrypted under a passphrase, together with everything needed to open it again except the
    /// passphrase itself. Safe to store anywhere the seed itself would not be — including on a server, which can
    /// hold this blob and still learn nothing about the account it belongs to beyond the address.
    /// </summary>
    /// <param name="Address"> Address of the account inside, so a person can tell two vaults apart while both are locked. </param>
    /// <param name="DerivationName"> Passphrase derivation that produced the key, e.g. <c>Argon2id</c>. </param>
    /// <param name="CipherName"> Cipher that encrypted the seed, e.g. <c>ChaCha20-Poly1305</c>. </param>
    /// <param name="Salt"> Random per-vault salt, so two people with the same passphrase get different keys. </param>
    /// <param name="Nonce"> Per-vault nonce for the cipher. </param>
    /// <param name="Ciphertext"> The encrypted seed with its authentication tag. </param>
    public sealed record SealedIdentity(
        string Address,
        string DerivationName,
        string CipherName,
        byte[] Salt,
        byte[] Nonce,
        byte[] Ciphertext);

    /// <summary>
    /// Locks a master seed behind a passphrase and opens it again. The passphrase is stretched with a deliberately
    /// expensive derivation, so someone holding a stolen vault has to pay that cost for every single guess.
    /// </summary>
    /// <param name="passphraseDerivation"> Must be passphrase-hardened; a fast expander here would make guessing cheap. </param>
    /// <param name="cipher"> Authenticated cipher protecting the seed. </param>
    public sealed class IdentityVault(IKeyDerivation passphraseDerivation, IAeadCipher cipher)
    {
        static readonly byte[] KeyContext = "Groundwork/IdentityVault/key/v1"u8.ToArray();

        const int SaltSize = 16;

        /// <summary> Encrypts a master seed under a passphrase. </summary>
        /// <param name="masterSeed"> The seed to protect. </param>
        /// <param name="passphrase"> What the owner will have to type to get it back. Losing it loses the account. </param>
        /// <param name="address"> Address of the account, stored in the clear so the vault is identifiable while locked. </param>
        /// <returns> The sealed vault, safe to write to disk or upload. </returns>
        public SealedIdentity Seal(ReadOnlySpan<byte> masterSeed, string passphrase, string address)
        {
            RequireHardenedDerivation();

            byte[] salt = RandomSource.Next(SaltSize);
            byte[] nonce = RandomSource.Next(cipher.NonceSize);
            byte[] header = BuildHeader(address, salt, nonce);

            byte[] ciphertext = cipher.Encrypt(masterSeed, DeriveKey(passphrase, salt), nonce, header);
            return new SealedIdentity(address, passphraseDerivation.Name, cipher.Name, salt, nonce, ciphertext);
        }

        /// <summary>
        /// Tries to open a vault. A wrong passphrase is an ordinary outcome, not an error, so this reports it as
        /// false — and it is indistinguishable from a tampered vault, which is what the authentication tag is for.
        /// </summary>
        /// <param name="sealedIdentity"> The vault to open. </param>
        /// <param name="passphrase"> Passphrase to try. </param>
        /// <param name="masterSeed"> Receives the recovered seed, or an empty array when the attempt failed. </param>
        /// <returns> True when the passphrase was right and the vault was untouched. </returns>
        public bool TryOpen(SealedIdentity sealedIdentity, string passphrase, out byte[] masterSeed)
        {
            RequireHardenedDerivation();

            masterSeed = [];
            if (sealedIdentity.DerivationName != passphraseDerivation.Name || sealedIdentity.CipherName != cipher.Name)
            {
                return false;
            }

            byte[] header = BuildHeader(sealedIdentity.Address, sealedIdentity.Salt, sealedIdentity.Nonce);

            return cipher.TryDecrypt(
                sealedIdentity.Ciphertext,
                DeriveKey(passphrase, sealedIdentity.Salt),
                sealedIdentity.Nonce,
                header,
                out masterSeed);
        }

        /// <summary> Stretches the passphrase into the encryption key. </summary>
        /// <param name="passphrase"> The owner's passphrase. </param>
        /// <param name="salt"> This vault's salt. </param>
        /// <returns> A key of exactly the cipher's key size. </returns>
        byte[] DeriveKey(string passphrase, byte[] salt)
            => passphraseDerivation.Derive(Encoding.UTF8.GetBytes(passphrase), salt, KeyContext, cipher.KeySize);

        /// <summary>
        /// Builds the authenticated-but-readable header. Because the address, salt and nonce are authenticated,
        /// swapping a vault's header for another's makes it fail to open instead of silently decrypting under the
        /// wrong assumptions.
        /// </summary>
        /// <param name="address"> Address stored with the vault. </param>
        /// <param name="salt"> This vault's salt. </param>
        /// <param name="nonce"> This vault's nonce. </param>
        /// <returns> The bytes handed to the cipher as associated data. </returns>
        byte[] BuildHeader(string address, byte[] salt, byte[] nonce)
        {
            TranscriptWriter header = new();
            header.WriteText(passphraseDerivation.Name);
            header.WriteText(cipher.Name);
            header.WriteText(address);
            header.WriteBytes(salt);
            header.WriteBytes(nonce);
            return header.ToArray();
        }

        /// <summary> Refuses to run with a fast expander, which would make passphrase guessing cheap. </summary>
        void RequireHardenedDerivation()
        {
            if (passphraseDerivation.IsPassphraseHardened) return;

            throw new InvalidOperationException(
                $"'{passphraseDerivation.Name}' is not passphrase-hardened and must not protect a vault. Use Argon2id or another slow derivation.");
        }
    }
}
