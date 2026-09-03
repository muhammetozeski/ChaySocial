using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Groundwork.Cryptography
{
    /// <summary>
    /// Ed25519 signing. Fast and small, and the scheme whose signatures a classical attacker cannot forge — but a
    /// large quantum computer could. Pair it with <see cref="MLDsaSignatureScheme"/> through
    /// <see cref="HybridSignatureScheme"/> rather than relying on it alone.
    /// </summary>
    public sealed class Ed25519SignatureScheme : ISignatureScheme
    {
        public string Name => "Ed25519";
        public int SeedSize => Ed25519PrivateKeyParameters.KeySize;
        public int PublicKeySize => Ed25519PublicKeyParameters.KeySize;
        public int SignatureSize => Ed25519PrivateKeyParameters.SignatureSize;

        public byte[] DerivePublicKey(ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);
            return new Ed25519PrivateKeyParameters(seed).GeneratePublicKey().GetEncoded();
        }

        public byte[] Sign(ReadOnlySpan<byte> message, ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);

            Ed25519Signer signer = new();
            signer.Init(true, new Ed25519PrivateKeyParameters(seed));
            signer.BlockUpdate(message);
            return signer.GenerateSignature();
        }

        public bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
        {
            if (publicKey.Length != PublicKeySize || signature.Length != SignatureSize) return false;

            Ed25519Signer verifier = new();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey));
            verifier.BlockUpdate(message);
            return verifier.VerifySignature(signature.ToArray());
        }
    }

    /// <summary>
    /// ML-DSA-65 signing (FIPS 204, the standardized Dilithium). Its security rests on lattice problems rather than
    /// elliptic curves, so it survives the attack that breaks <see cref="Ed25519SignatureScheme"/>. Keys and
    /// signatures are far larger — which is exactly the cost this project accepts to be safe against a future
    /// quantum attacker who records today's traffic.
    /// </summary>
    public sealed class MLDsaSignatureScheme : ISignatureScheme
    {
        /// <summary> Parameter set. ML-DSA-65 is the middle of the three, matching roughly AES-192 strength. </summary>
        static readonly MLDsaParameters Parameters = MLDsaParameters.ml_dsa_65;

        /// <summary> FIPS 204 derives an entire ML-DSA key pair from a 32-byte seed. </summary>
        const int MLDsaSeedSize = 32;

        public string Name => "ML-DSA-65";
        public int SeedSize => MLDsaSeedSize;
        public int PublicKeySize { get; } = MLDsaPrivateKeyParameters.FromSeed(Parameters, new byte[MLDsaSeedSize]).GetPublicKeyEncoded().Length;

        // Measured from a real signature rather than read off the signer: the signer reports its size only after
        // Init, and a hard-coded 3309 would silently rot if the parameter set ever changed.
        public int SignatureSize { get; } = MeasureSignatureSize();

        public byte[] DerivePublicKey(ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);
            return MLDsaPrivateKeyParameters.FromSeed(Parameters, seed.ToArray()).GetPublicKeyEncoded();
        }

        public byte[] Sign(ReadOnlySpan<byte> message, ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);

            // Deterministic signing: the same message and seed always produce the same signature, so a signature can
            // never leak the private key through a weak random source on the signing device.
            MLDsaSigner signer = new(Parameters, deterministic: true);
            signer.Init(true, MLDsaPrivateKeyParameters.FromSeed(Parameters, seed.ToArray()));
            signer.BlockUpdate(message);
            return signer.GenerateSignature();
        }

        public bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
        {
            if (publicKey.Length != PublicKeySize) return false;

            MLDsaSigner verifier = new(Parameters, deterministic: true);
            verifier.Init(false, MLDsaPublicKeyParameters.FromEncoding(Parameters, publicKey.ToArray()));
            verifier.BlockUpdate(message);
            return verifier.VerifySignature(signature.ToArray());
        }

        /// <summary> Signs an empty message once at type initialization to learn how long this parameter set's signatures are. </summary>
        /// <returns> Signature length in bytes. </returns>
        static int MeasureSignatureSize()
        {
            MLDsaSigner signer = new(Parameters, deterministic: true);
            signer.Init(true, MLDsaPrivateKeyParameters.FromSeed(Parameters, new byte[MLDsaSeedSize]));
            return signer.GenerateSignature().Length;
        }
    }
}
