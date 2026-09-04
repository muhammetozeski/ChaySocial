using ChaySocial.MainProject.Cryptography;

namespace ChaySocial.MainProject.Identity
{
    /// <summary>
    /// Everything about an account that may be published: the address that names it and the two public keys the
    /// address commits to. A server can hold this and still not be able to read a message or sign as the account.
    /// </summary>
    /// <param name="Address"> The account's name, derived from both keys. </param>
    /// <param name="SigningPublicKey"> Verifies signatures the account produced. </param>
    /// <param name="EncryptionPublicKey"> Others encapsulate to this key to send the account something only it can open. </param>
    public sealed record PublicIdentity(string Address, byte[] SigningPublicKey, byte[] EncryptionPublicKey);

    /// <summary>
    /// An unlocked account: the master seed plus the operations only its owner can perform. Instances exist only in
    /// memory on the owner's device — the seed is never sent anywhere, which is what makes the account
    /// self-sovereign rather than merely private.
    /// </summary>
    public sealed class PrivateIdentity
    {
        readonly IdentityScheme _scheme;
        readonly byte[] _masterSeed;

        internal PrivateIdentity(IdentityScheme scheme, byte[] masterSeed, PublicIdentity publicIdentity)
        {
            _scheme = scheme;
            _masterSeed = masterSeed;
            Public = publicIdentity;
        }

        /// <summary> The publishable half of this identity. </summary>
        public PublicIdentity Public { get; }

        /// <summary> Signs as this account. </summary>
        /// <param name="message"> Bytes to sign — a login transcript, a post, a key announcement. </param>
        /// <returns> The signature anyone can check against <see cref="PublicIdentity.SigningPublicKey"/>. </returns>
        public byte[] Sign(ReadOnlySpan<byte> message)
            => _scheme.SignatureScheme.Sign(message, _scheme.DeriveSigningSeed(_masterSeed));

        /// <summary> Recovers the secret someone encapsulated to this account. </summary>
        /// <param name="encapsulation"> Value that travelled with the message. </param>
        /// <returns> The same secret the sender held. </returns>
        public byte[] Decapsulate(ReadOnlySpan<byte> encapsulation)
            => _scheme.KeyEncapsulation.Decapsulate(encapsulation, _scheme.DeriveEncryptionSeed(_masterSeed));

        /// <summary>
        /// Hands back the master seed so it can be shown once for the owner to write down, or sealed into a vault.
        /// Every other capability of the account is derived from these bytes, so treat a copy of them as the account.
        /// </summary>
        /// <returns> A copy of the master seed. </returns>
        public byte[] ExportMasterSeed() => (byte[])_masterSeed.Clone();
    }

    /// <summary>
    /// Builds accounts out of a single random seed. Creating an account is one call and touches no network: the seed
    /// is generated locally, both key pairs are derived from it, and the address falls out of the keys. There is
    /// nothing to register and no password to send, because the server never learns anything it could leak.
    /// </summary>
    /// <param name="signatureScheme"> Scheme the account signs with. </param>
    /// <param name="keyEncapsulation"> Scheme others use to encrypt to the account. </param>
    /// <param name="seedExpander"> Expands the master seed into one seed per scheme. </param>
    /// <param name="addressFactory"> Turns the derived public keys into the account's name. </param>
    public sealed class IdentityScheme(
        ISignatureScheme signatureScheme,
        IKeyEncapsulation keyEncapsulation,
        IKeyDerivation seedExpander,
        IdentityAddressFactory addressFactory)
    {
        static readonly byte[] SigningSeedContext = "Groundwork/Identity/signing/v1"u8.ToArray();
        static readonly byte[] EncryptionSeedContext = "Groundwork/Identity/encryption/v1"u8.ToArray();

        /// <summary> Bytes of randomness behind an account. 256 bits: the amount an attacker cannot search even with a quantum speed-up. </summary>
        public const int MasterSeedSize = 32;

        internal ISignatureScheme SignatureScheme => signatureScheme;
        internal IKeyEncapsulation KeyEncapsulation => keyEncapsulation;

        /// <summary> Names the algorithms behind this scheme, for display and for stamping into stored identities. </summary>
        public string Name => $"{signatureScheme.Name} / {keyEncapsulation.Name}";

        /// <summary> Draws the randomness a brand new account is built from. </summary>
        /// <returns> A fresh master seed of <see cref="MasterSeedSize"/> bytes. </returns>
        public static byte[] CreateMasterSeed() => RandomSource.Next(MasterSeedSize);

        /// <summary> Rebuilds the full account a master seed stands for. </summary>
        /// <param name="masterSeed"> Exactly <see cref="MasterSeedSize"/> bytes. </param>
        /// <returns> The unlocked identity, ready to sign and decrypt. </returns>
        public PrivateIdentity Open(ReadOnlySpan<byte> masterSeed)
            => new(this, masterSeed.ToArray(), DerivePublicIdentity(masterSeed));

        /// <summary> Derives only the publishable half, for when the private operations are not needed. </summary>
        /// <param name="masterSeed"> Exactly <see cref="MasterSeedSize"/> bytes. </param>
        /// <returns> The address and both public keys. </returns>
        public PublicIdentity DerivePublicIdentity(ReadOnlySpan<byte> masterSeed)
        {
            byte[] signingPublicKey = signatureScheme.DerivePublicKey(DeriveSigningSeed(masterSeed));
            byte[] encryptionPublicKey = keyEncapsulation.DerivePublicKey(DeriveEncryptionSeed(masterSeed));

            return new PublicIdentity(
                addressFactory.Create(signingPublicKey, encryptionPublicKey),
                signingPublicKey,
                encryptionPublicKey);
        }

        /// <summary> Encapsulates a fresh secret to another account. </summary>
        /// <param name="recipient"> The account being written to. </param>
        /// <returns> The secret to encrypt with, and the encapsulation to send along. </returns>
        public EncapsulationResult EncapsulateTo(PublicIdentity recipient)
            => keyEncapsulation.Encapsulate(recipient.EncryptionPublicKey);

        /// <summary> Checks a signature against an account's published key. </summary>
        /// <param name="message"> Bytes that were supposedly signed. </param>
        /// <param name="signature"> Signature to check. </param>
        /// <param name="signer"> The account claimed as signer. </param>
        /// <returns> True only when that account really produced the signature. </returns>
        public bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, PublicIdentity signer)
            => addressFactory.Matches(signer.Address, signer.SigningPublicKey, signer.EncryptionPublicKey)
               && signatureScheme.Verify(message, signature, signer.SigningPublicKey);

        /// <summary> Derives the signing scheme's seed from the master seed. </summary>
        /// <param name="masterSeed"> The account's master seed. </param>
        /// <returns> A seed of exactly the signing scheme's required length. </returns>
        internal byte[] DeriveSigningSeed(ReadOnlySpan<byte> masterSeed)
            => seedExpander.Derive(masterSeed, [], SigningSeedContext, signatureScheme.SeedSize);

        /// <summary> Derives the encapsulation scheme's seed from the master seed. </summary>
        /// <param name="masterSeed"> The account's master seed. </param>
        /// <returns> A seed of exactly the encapsulation scheme's required length. </returns>
        internal byte[] DeriveEncryptionSeed(ReadOnlySpan<byte> masterSeed)
            => seedExpander.Derive(masterSeed, [], EncryptionSeedContext, keyEncapsulation.SeedSize);
    }
}

