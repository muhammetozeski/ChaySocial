using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Kems;
using Org.BouncyCastle.Crypto.Parameters;

namespace Groundwork.Cryptography
{
    /// <summary>
    /// X25519 Diffie-Hellman presented as a KEM: the sender makes a throwaway key pair, agrees with the recipient's
    /// published key, and ships the throwaway public key as the encapsulation. The raw agreement is never used as a
    /// key directly — it is expanded together with both public keys, so the secret is bound to this exact pair of
    /// parties and cannot be replayed against a different recipient.
    /// </summary>
    /// <param name="keyDerivation"> Expander used to turn the raw agreement into the shared secret. </param>
    public sealed class X25519KeyEncapsulation(IKeyDerivation keyDerivation) : IKeyEncapsulation
    {
        /// <summary> Label separating this scheme's derivations from every other use of the same expander. </summary>
        static readonly byte[] DerivationContext = "Groundwork/X25519-KEM/v1"u8.ToArray();

        const int SharedSecretBytes = 32;

        public string Name => "X25519";
        public int SeedSize => X25519PrivateKeyParameters.KeySize;
        public int PublicKeySize => X25519PublicKeyParameters.KeySize;
        public int EncapsulationSize => X25519PublicKeyParameters.KeySize;
        public int SharedSecretSize => SharedSecretBytes;

        public byte[] DerivePublicKey(ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);
            return new X25519PrivateKeyParameters(seed).GeneratePublicKey().GetEncoded();
        }

        public EncapsulationResult Encapsulate(ReadOnlySpan<byte> recipientPublicKey)
        {
            CryptographicGuard.RequireLength(recipientPublicKey, PublicKeySize, nameof(recipientPublicKey), Name);

            X25519PrivateKeyParameters ephemeralPrivateKey = new(RandomSource.Secure);
            byte[] ephemeralPublicKey = ephemeralPrivateKey.GeneratePublicKey().GetEncoded();
            byte[] agreement = Agree(ephemeralPrivateKey, recipientPublicKey);

            return new EncapsulationResult(
                ephemeralPublicKey,
                ExpandAgreement(agreement, ephemeralPublicKey, recipientPublicKey));
        }

        public byte[] Decapsulate(ReadOnlySpan<byte> encapsulation, ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(encapsulation, EncapsulationSize, nameof(encapsulation), Name);
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);

            X25519PrivateKeyParameters privateKey = new(seed);
            byte[] agreement = Agree(privateKey, encapsulation);

            return ExpandAgreement(agreement, encapsulation, privateKey.GeneratePublicKey().GetEncoded());
        }

        /// <summary> Runs the raw X25519 agreement. </summary>
        /// <param name="privateKey"> Our private key. </param>
        /// <param name="otherPublicKey"> The other side's public key. </param>
        /// <returns> The raw agreed bytes, which still have to be expanded before use. </returns>
        static byte[] Agree(X25519PrivateKeyParameters privateKey, ReadOnlySpan<byte> otherPublicKey)
        {
            X25519Agreement agreement = new();
            agreement.Init(privateKey);

            byte[] agreed = new byte[agreement.AgreementSize];
            agreement.CalculateAgreement(new X25519PublicKeyParameters(otherPublicKey), agreed, 0);
            return agreed;
        }

        /// <summary> Turns the raw agreement into the shared secret, mixing in both public keys so the result is bound to this pair of parties. </summary>
        /// <param name="agreement"> Raw agreed bytes. </param>
        /// <param name="ephemeralPublicKey"> The sender's throwaway public key, which is also the encapsulation. </param>
        /// <param name="recipientPublicKey"> The recipient's published key. </param>
        /// <returns> Exactly <see cref="SharedSecretSize"/> bytes. </returns>
        byte[] ExpandAgreement(byte[] agreement, ReadOnlySpan<byte> ephemeralPublicKey, ReadOnlySpan<byte> recipientPublicKey)
        {
            byte[] transcript = new byte[ephemeralPublicKey.Length + recipientPublicKey.Length];
            ephemeralPublicKey.CopyTo(transcript);
            recipientPublicKey.CopyTo(transcript.AsSpan(ephemeralPublicKey.Length));

            return keyDerivation.Derive(agreement, transcript, DerivationContext, SharedSecretSize);
        }
    }

    /// <summary>
    /// ML-KEM-768 (FIPS 203, the standardized Kyber). Where X25519 falls to a quantum attacker who recorded the
    /// traffic years earlier, this one does not, which is the whole reason a message encrypted today is worth
    /// protecting with both.
    /// </summary>
    public sealed class MLKemKeyEncapsulation : IKeyEncapsulation
    {
        /// <summary> Parameter set. ML-KEM-768 is the middle of the three, matching roughly AES-192 strength. </summary>
        static readonly MLKemParameters Parameters = MLKemParameters.ml_kem_768;

        /// <summary> FIPS 203 derives an entire ML-KEM key pair from a 64-byte seed (the d and z halves). </summary>
        const int MLKemSeedSize = 64;

        /// <summary> Both output sizes, measured once at type initialization: the encapsulator only reports them after Init. </summary>
        static readonly (int Encapsulation, int SharedSecret) MeasuredSizes = MeasureSizes();

        public string Name => "ML-KEM-768";
        public int SeedSize => MLKemSeedSize;
        public int PublicKeySize { get; } = MLKemPrivateKeyParameters.FromSeed(Parameters, new byte[MLKemSeedSize]).GetPublicKeyEncoded().Length;
        public int EncapsulationSize => MeasuredSizes.Encapsulation;
        public int SharedSecretSize => MeasuredSizes.SharedSecret;

        public byte[] DerivePublicKey(ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);
            return MLKemPrivateKeyParameters.FromSeed(Parameters, seed.ToArray()).GetPublicKeyEncoded();
        }

        public EncapsulationResult Encapsulate(ReadOnlySpan<byte> recipientPublicKey)
        {
            CryptographicGuard.RequireLength(recipientPublicKey, PublicKeySize, nameof(recipientPublicKey), Name);

            MLKemEncapsulator encapsulator = new(Parameters);
            encapsulator.Init(new ParametersWithRandom(
                MLKemPublicKeyParameters.FromEncoding(Parameters, recipientPublicKey.ToArray()),
                RandomSource.Secure));

            byte[] encapsulation = new byte[encapsulator.EncapsulationLength];
            byte[] sharedSecret = new byte[encapsulator.SecretLength];
            encapsulator.Encapsulate(encapsulation, sharedSecret);

            return new EncapsulationResult(encapsulation, sharedSecret);
        }

        public byte[] Decapsulate(ReadOnlySpan<byte> encapsulation, ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(encapsulation, EncapsulationSize, nameof(encapsulation), Name);
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);

            MLKemDecapsulator decapsulator = new(Parameters);
            decapsulator.Init(MLKemPrivateKeyParameters.FromSeed(Parameters, seed.ToArray()));

            byte[] sharedSecret = new byte[decapsulator.SecretLength];
            decapsulator.Decapsulate(encapsulation, sharedSecret);
            return sharedSecret;
        }

        /// <summary> Initializes one encapsulator against a throwaway key to read this parameter set's output sizes. </summary>
        /// <returns> Encapsulation length and shared-secret length in bytes. </returns>
        static (int Encapsulation, int SharedSecret) MeasureSizes()
        {
            MLKemEncapsulator encapsulator = new(Parameters);
            encapsulator.Init(MLKemPrivateKeyParameters.FromSeed(Parameters, new byte[MLKemSeedSize]).GetPublicKey());
            return (encapsulator.EncapsulationLength, encapsulator.SecretLength);
        }
    }
}
