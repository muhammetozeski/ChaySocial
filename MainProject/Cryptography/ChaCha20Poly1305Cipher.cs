using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;

namespace ChaySocial.MainProject.Cryptography
{
    /// <summary>
    /// ChaCha20-Poly1305 authenticated encryption. Chosen over AES-GCM because it needs no hardware acceleration to
    /// be fast, which matters when the same code runs inside a browser's WebAssembly sandbox and on a phone.
    /// </summary>
    public sealed class ChaCha20Poly1305Cipher : IAeadCipher
    {
        const int KeyBytes = 32;
        const int NonceBytes = 12;

        /// <summary> Poly1305 tag length in bits, as the AEAD parameters express it. </summary>
        const int TagBits = 128;

        public string Name => "ChaCha20-Poly1305";
        public int KeySize => KeyBytes;
        public int NonceSize => NonceBytes;

        public byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData)
        {
            CryptographicGuard.RequireLength(key, KeySize, nameof(key), Name);
            CryptographicGuard.RequireLength(nonce, NonceSize, nameof(nonce), Name);

            Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305 cipher = new();
            cipher.Init(true, BuildParameters(key, nonce, associatedData));

            byte[] output = new byte[cipher.GetOutputSize(plaintext.Length)];
            int written = cipher.ProcessBytes(plaintext.ToArray(), 0, plaintext.Length, output, 0);
            cipher.DoFinal(output, written);
            return output;
        }

        public bool TryDecrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData, out byte[] plaintext)
        {
            plaintext = [];

            if (key.Length != KeySize || nonce.Length != NonceSize) return false;

            try
            {
                Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305 cipher = new();
                cipher.Init(false, BuildParameters(key, nonce, associatedData));

                byte[] output = new byte[cipher.GetOutputSize(ciphertext.Length)];
                int written = cipher.ProcessBytes(ciphertext.ToArray(), 0, ciphertext.Length, output, 0);
                written += cipher.DoFinal(output, written);

                plaintext = output.Length == written ? output : output[..written];
                return true;
            }
            catch (InvalidCipherTextException)
            {
                // A failed tag check is the expected outcome for altered or forged input, not an error to report.
                return false;
            }
        }

        /// <summary> Packs key, nonce and associated data into the parameter object both directions need. </summary>
        /// <param name="key"> Encryption key. </param>
        /// <param name="nonce"> Per-message nonce. </param>
        /// <param name="associatedData"> Data authenticated but not encrypted. </param>
        /// <returns> Parameters ready for <c>Init</c>. </returns>
        static AeadParameters BuildParameters(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData)
            => new(new KeyParameter(key.ToArray()), TagBits, nonce.ToArray(), associatedData.ToArray());
    }
}

