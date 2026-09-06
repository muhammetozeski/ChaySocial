using System.Text.Json;
using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.Identity;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// An account's secret, locked behind a passphrase and written out as a file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only backup an account has today is the seed itself, shown as plain text. Anybody who sees it owns the
    /// account, so it cannot go into a cloud drive, an email, a password manager or a second device — there is no
    /// form of it that can be kept anywhere but on paper. A passphrase-locked file is the one backup that can sit
    /// somewhere untrusted: whoever finds it has found a puzzle, not an account, because
    /// <see cref="IdentityVault"/> stretches every guess through Argon2id.
    /// </para>
    /// <para>
    /// The address inside the file is deliberately in the clear. It is what lets somebody tell two locked files
    /// apart, and it gives away nothing — an address is public by design.
    /// </para>
    /// </remarks>
    public static class KeyFileService
    {
        /// <summary>
        /// Shortest passphrase accepted. Not a password rule so much as a floor under the one thing standing
        /// between a found file and an account: below this, the Argon2id cost stops being what decides.
        /// </summary>
        public const int ShortestPassphraseLength = 8;

        /// <summary> Written out indented, because a person may well open this file and look at it. </summary>
        static readonly JsonSerializerOptions KeyFileJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        /// <summary> Locks one account's seed behind a passphrase. </summary>
        /// <param name="owner"> The unlocked account whose seed is being locked. </param>
        /// <param name="passphrase"> What will have to be typed to get it back; losing it loses the account. </param>
        /// <returns> The locked file, safe to keep anywhere the seed itself could not be. </returns>
        public static SealedIdentity Seal(PrivateIdentity owner, string passphrase)
            => AppCryptography.Vault.Seal(owner.ExportMasterSeed(), passphrase, owner.Public.Address);

        /// <summary> Writes a locked file out as bytes. </summary>
        /// <param name="file"> The locked file. </param>
        /// <returns> Its bytes, for saving or for showing. </returns>
        public static byte[] Serialise(SealedIdentity file) => JsonSerializer.SerializeToUtf8Bytes(file, KeyFileJson);

        /// <summary> Reads a locked file back, or reports that those bytes were not one. </summary>
        /// <param name="bytes"> What was chosen or pasted. </param>
        /// <returns> The locked file, or null when the bytes are not one. </returns>
        /// <remarks>
        /// Bytes handed in by a person are not a promise: a wrong file, a truncated download and a text editor's
        /// helpful reformatting all arrive here the same way, and all of them are an ordinary "that is not one of
        /// these" rather than something to throw over.
        /// </remarks>
        public static SealedIdentity? Deserialise(ReadOnlySpan<byte> bytes)
        {
            try
            {
                SealedIdentity? file = JsonSerializer.Deserialize<SealedIdentity>(bytes, KeyFileJson);

                return file is null || file.Address.Length == 0 || file.Ciphertext.Length == 0 ? null : file;
            }
            catch (JsonException error)
            {
                Log($"{nameof(KeyFileService)} was handed bytes that are not a key file.\n{error}", LogLevel.Warning);
                return null;
            }
        }

        /// <summary>
        /// Tries a passphrase against a locked file and hands back the secret in the form the sign-in box takes.
        /// </summary>
        /// <param name="file"> The locked file. </param>
        /// <param name="passphrase"> Passphrase to try. </param>
        /// <param name="secretText"> Receives the secret, or an empty string when the passphrase was wrong. </param>
        /// <returns> True when the passphrase opened it. </returns>
        /// <remarks>
        /// A wrong passphrase is an ordinary outcome rather than an error, and it is indistinguishable from a file
        /// somebody altered — which is what the cipher's authentication tag is for.
        /// </remarks>
        public static bool TryOpen(SealedIdentity file, string passphrase, out string secretText)
        {
            secretText = string.Empty;

            if (!AppCryptography.Vault.TryOpen(file, passphrase, out byte[] masterSeed)) return false;

            secretText = MasterSeedText.Format(masterSeed);
            return true;
        }
    }
}
