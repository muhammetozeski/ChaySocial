namespace Groundwork.Cryptography
{
    /// <summary>
    /// What a KEM produces for one recipient: the value only that recipient can open, and the secret both sides end
    /// up holding.
    /// </summary>
    /// <param name="Encapsulation"> Travels with the message; the recipient feeds it back into <see cref="IKeyEncapsulation.Decapsulate"/>. </param>
    /// <param name="SharedSecret"> The secret the sender uses immediately and the recipient recovers. Never transmitted. </param>
    public readonly record struct EncapsulationResult(byte[] Encapsulation, byte[] SharedSecret);

    /// <summary>
    /// Signing and verification, where the private half is never stored expanded — it is always the seed it was
    /// derived from. One seed in, one key pair out, every time, on every device.
    /// </summary>
    public interface ISignatureScheme
    {
        /// <summary> Algorithm name as it is written into a stored identity, e.g. <c>Ed25519</c> or <c>ML-DSA-65</c>. </summary>
        string Name { get; }

        /// <summary> Exact number of seed bytes this scheme derives a key pair from. </summary>
        int SeedSize { get; }

        /// <summary> Exact size of the public key this scheme derives. </summary>
        int PublicKeySize { get; }

        /// <summary> Exact size of the signatures this scheme produces. </summary>
        int SignatureSize { get; }

        /// <summary> Derives the public half of the key pair a seed stands for. </summary>
        /// <param name="seed"> Exactly <see cref="SeedSize"/> bytes. </param>
        /// <returns> The public key, safe to publish. </returns>
        byte[] DerivePublicKey(ReadOnlySpan<byte> seed);

        /// <summary> Signs a message with the key pair a seed stands for. </summary>
        /// <param name="message"> Bytes to sign. </param>
        /// <param name="seed"> Exactly <see cref="SeedSize"/> bytes. </param>
        /// <returns> The signature. </returns>
        byte[] Sign(ReadOnlySpan<byte> message, ReadOnlySpan<byte> seed);

        /// <summary> Checks a signature against a public key. </summary>
        /// <param name="message"> Bytes that were supposedly signed. </param>
        /// <param name="signature"> Signature to check. </param>
        /// <param name="publicKey"> Public key of the claimed signer. </param>
        /// <returns> True only when the signature was produced by that key over that message. </returns>
        bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey);
    }

    /// <summary>
    /// Establishing a shared secret with someone who is not online: the sender needs only the recipient's public key,
    /// and the recipient recovers the same secret later from their seed. Classical Diffie-Hellman and post-quantum
    /// KEMs both fit this shape, which is what lets them be combined.
    /// </summary>
    public interface IKeyEncapsulation
    {
        /// <summary> Algorithm name as it is written into a stored identity, e.g. <c>X25519</c> or <c>ML-KEM-768</c>. </summary>
        string Name { get; }

        /// <summary> Exact number of seed bytes this scheme derives a key pair from. </summary>
        int SeedSize { get; }

        /// <summary> Exact size of the public key this scheme derives. </summary>
        int PublicKeySize { get; }

        /// <summary> Exact size of the <see cref="EncapsulationResult.Encapsulation"/> this scheme produces. </summary>
        int EncapsulationSize { get; }

        /// <summary> Exact size of the <see cref="EncapsulationResult.SharedSecret"/> this scheme produces. </summary>
        int SharedSecretSize { get; }

        /// <summary> Derives the public half of the key pair a seed stands for. </summary>
        /// <param name="seed"> Exactly <see cref="SeedSize"/> bytes. </param>
        /// <returns> The public key, safe to publish. </returns>
        byte[] DerivePublicKey(ReadOnlySpan<byte> seed);

        /// <summary> Produces a fresh shared secret for one recipient. Every call returns a different secret. </summary>
        /// <param name="recipientPublicKey"> Public key of the recipient. </param>
        /// <returns> The secret to use now, and the encapsulation to send along with the message. </returns>
        EncapsulationResult Encapsulate(ReadOnlySpan<byte> recipientPublicKey);

        /// <summary> Recovers the secret a sender encapsulated for this seed's key pair. </summary>
        /// <param name="encapsulation"> Value that travelled with the message. </param>
        /// <param name="seed"> Exactly <see cref="SeedSize"/> bytes. </param>
        /// <returns> The same secret the sender held. </returns>
        byte[] Decapsulate(ReadOnlySpan<byte> encapsulation, ReadOnlySpan<byte> seed);
    }

    /// <summary>
    /// Authenticated encryption: content is both hidden and tamper-evident, and the associated data is authenticated
    /// without being hidden, so a message cannot be replayed under a different envelope.
    /// </summary>
    public interface IAeadCipher
    {
        /// <summary> Algorithm name as it is written into an encrypted envelope, e.g. <c>ChaCha20-Poly1305</c>. </summary>
        string Name { get; }

        /// <summary> Exact key size this cipher takes. </summary>
        int KeySize { get; }

        /// <summary> Exact nonce size this cipher takes. A nonce must never repeat under the same key. </summary>
        int NonceSize { get; }

        /// <summary> Encrypts and authenticates, appending the authentication tag to the ciphertext. </summary>
        /// <param name="plaintext"> Content to hide. </param>
        /// <param name="key"> Exactly <see cref="KeySize"/> bytes. </param>
        /// <param name="nonce"> Exactly <see cref="NonceSize"/> bytes, never reused with this key. </param>
        /// <param name="associatedData"> Authenticated but left readable — e.g. the envelope's sender and recipient ids. </param>
        /// <returns> Ciphertext with the authentication tag appended. </returns>
        byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData);

        /// <summary> Verifies and decrypts. Returns false rather than throwing when the content or the associated data was altered, which is a normal outcome on hostile input. </summary>
        /// <param name="ciphertext"> Ciphertext with the tag appended, as produced by <see cref="Encrypt"/>. </param>
        /// <param name="key"> The same key that encrypted it. </param>
        /// <param name="nonce"> The same nonce that encrypted it. </param>
        /// <param name="associatedData"> The same associated data that was authenticated. </param>
        /// <param name="plaintext"> Receives the recovered content, or an empty array when verification failed. </param>
        /// <returns> True when the tag verified and the content is authentic. </returns>
        bool TryDecrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> associatedData, out byte[] plaintext);
    }

    /// <summary>
    /// Turning one secret into many independent keys. Two flavours implement this: a fast expander for material that
    /// is already high-entropy (a master seed), and a deliberately slow one for material that is not (a passphrase).
    /// </summary>
    public interface IKeyDerivation
    {
        /// <summary> Algorithm name as it is written into an encrypted blob's header, e.g. <c>HKDF-SHA512</c> or <c>Argon2id</c>. </summary>
        string Name { get; }

        /// <summary> True when this derivation is intentionally expensive and therefore suitable for passphrases; false when it only expands existing entropy. </summary>
        bool IsPassphraseHardened { get; }

        /// <summary> Derives key material. </summary>
        /// <param name="inputKeyMaterial"> The secret to derive from. </param>
        /// <param name="salt"> Value that separates derivations of the same secret; may be empty for an expander, must be unique per user for a passphrase. </param>
        /// <param name="context"> Label naming what the output is for, so the same secret and salt still yield unrelated keys for unrelated purposes. </param>
        /// <param name="outputLength"> Number of bytes to produce. </param>
        /// <returns> Exactly <paramref name="outputLength"/> derived bytes. </returns>
        byte[] Derive(ReadOnlySpan<byte> inputKeyMaterial, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> context, int outputLength);
    }
}
