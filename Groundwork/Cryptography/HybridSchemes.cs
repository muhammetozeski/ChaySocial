namespace Groundwork.Cryptography
{
    /// <summary>
    /// Runs a classical and a post-quantum signature scheme side by side over the same message. A signature is
    /// accepted only when both halves verify, so the identity stays unforgeable as long as <em>either</em> scheme
    /// still holds — the classical one against today's attackers, the post-quantum one against a future attacker
    /// with a quantum computer.
    /// </summary>
    /// <param name="classical"> The fast, small, well-worn scheme. </param>
    /// <param name="postQuantum"> The lattice scheme that survives a quantum attacker. </param>
    /// <param name="seedExpander"> Expands the single hybrid seed into one seed per inner scheme. </param>
    public sealed class HybridSignatureScheme(ISignatureScheme classical, ISignatureScheme postQuantum, IKeyDerivation seedExpander)
        : ISignatureScheme
    {
        static readonly byte[] ClassicalSeedContext = "Groundwork/HybridSignature/classical/v1"u8.ToArray();
        static readonly byte[] PostQuantumSeedContext = "Groundwork/HybridSignature/post-quantum/v1"u8.ToArray();

        /// <summary> Bytes of the one seed a caller supplies; both inner seeds are expanded from it. </summary>
        public const int HybridSeedSize = 32;

        public string Name => $"{classical.Name}+{postQuantum.Name}";
        public int SeedSize => HybridSeedSize;
        public int PublicKeySize => classical.PublicKeySize + postQuantum.PublicKeySize;
        public int SignatureSize => classical.SignatureSize + postQuantum.SignatureSize;

        public byte[] DerivePublicKey(ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);

            return Concatenate(
                classical.DerivePublicKey(ExpandSeed(seed, ClassicalSeedContext, classical.SeedSize)),
                postQuantum.DerivePublicKey(ExpandSeed(seed, PostQuantumSeedContext, postQuantum.SeedSize)));
        }

        public byte[] Sign(ReadOnlySpan<byte> message, ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);

            return Concatenate(
                classical.Sign(message, ExpandSeed(seed, ClassicalSeedContext, classical.SeedSize)),
                postQuantum.Sign(message, ExpandSeed(seed, PostQuantumSeedContext, postQuantum.SeedSize)));
        }

        public bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey)
        {
            if (publicKey.Length != PublicKeySize || signature.Length != SignatureSize) return false;

            return classical.Verify(message, signature[..classical.SignatureSize], publicKey[..classical.PublicKeySize])
                && postQuantum.Verify(message, signature[classical.SignatureSize..], publicKey[classical.PublicKeySize..]);
        }

        /// <summary> Derives one inner scheme's seed from the hybrid seed. </summary>
        /// <param name="seed"> The hybrid seed. </param>
        /// <param name="context"> Label naming which inner scheme the output is for. </param>
        /// <param name="length"> Seed length that inner scheme requires. </param>
        /// <returns> The inner seed. </returns>
        byte[] ExpandSeed(ReadOnlySpan<byte> seed, byte[] context, int length)
            => seedExpander.Derive(seed, [], context, length);

        /// <summary> Joins the classical half and the post-quantum half into the single blob callers store or transmit. </summary>
        /// <param name="classicalPart"> Bytes produced by the classical scheme. </param>
        /// <param name="postQuantumPart"> Bytes produced by the post-quantum scheme. </param>
        /// <returns> The two halves back to back, classical first. </returns>
        internal static byte[] Concatenate(byte[] classicalPart, byte[] postQuantumPart)
        {
            byte[] joined = new byte[classicalPart.Length + postQuantumPart.Length];
            classicalPart.CopyTo(joined, 0);
            postQuantumPart.CopyTo(joined, classicalPart.Length);
            return joined;
        }
    }

    /// <summary>
    /// Establishes a secret with both a classical and a post-quantum KEM and mixes the two results into one key. An
    /// attacker has to break both to learn it, so traffic recorded today stays private even if elliptic curves fall
    /// later — the "harvest now, decrypt later" attack this project is built against.
    /// </summary>
    /// <param name="classical"> The fast, small, well-worn scheme. </param>
    /// <param name="postQuantum"> The lattice scheme that survives a quantum attacker. </param>
    /// <param name="secretCombiner"> Mixes the two shared secrets into the final key; also expands the hybrid seed into one seed per inner scheme. </param>
    public sealed class HybridKeyEncapsulation(IKeyEncapsulation classical, IKeyEncapsulation postQuantum, IKeyDerivation secretCombiner)
        : IKeyEncapsulation
    {
        static readonly byte[] ClassicalSeedContext = "Groundwork/HybridKem/classical/v1"u8.ToArray();
        static readonly byte[] PostQuantumSeedContext = "Groundwork/HybridKem/post-quantum/v1"u8.ToArray();
        static readonly byte[] CombinerContext = "Groundwork/HybridKem/combine/v1"u8.ToArray();

        /// <summary> Bytes of the one seed a caller supplies; both inner seeds are expanded from it. </summary>
        public const int HybridSeedSize = 32;

        const int CombinedSecretBytes = 32;

        public string Name => $"{classical.Name}+{postQuantum.Name}";
        public int SeedSize => HybridSeedSize;
        public int PublicKeySize => classical.PublicKeySize + postQuantum.PublicKeySize;
        public int EncapsulationSize => classical.EncapsulationSize + postQuantum.EncapsulationSize;
        public int SharedSecretSize => CombinedSecretBytes;

        public byte[] DerivePublicKey(ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);

            return HybridSignatureScheme.Concatenate(
                classical.DerivePublicKey(ExpandSeed(seed, ClassicalSeedContext, classical.SeedSize)),
                postQuantum.DerivePublicKey(ExpandSeed(seed, PostQuantumSeedContext, postQuantum.SeedSize)));
        }

        public EncapsulationResult Encapsulate(ReadOnlySpan<byte> recipientPublicKey)
        {
            CryptographicGuard.RequireLength(recipientPublicKey, PublicKeySize, nameof(recipientPublicKey), Name);

            EncapsulationResult classicalResult = classical.Encapsulate(recipientPublicKey[..classical.PublicKeySize]);
            EncapsulationResult postQuantumResult = postQuantum.Encapsulate(recipientPublicKey[classical.PublicKeySize..]);

            byte[] encapsulation = HybridSignatureScheme.Concatenate(classicalResult.Encapsulation, postQuantumResult.Encapsulation);

            return new EncapsulationResult(
                encapsulation,
                Combine(classicalResult.SharedSecret, postQuantumResult.SharedSecret, encapsulation));
        }

        public byte[] Decapsulate(ReadOnlySpan<byte> encapsulation, ReadOnlySpan<byte> seed)
        {
            CryptographicGuard.RequireLength(encapsulation, EncapsulationSize, nameof(encapsulation), Name);
            CryptographicGuard.RequireLength(seed, SeedSize, nameof(seed), Name);

            byte[] classicalSecret = classical.Decapsulate(
                encapsulation[..classical.EncapsulationSize],
                ExpandSeed(seed, ClassicalSeedContext, classical.SeedSize));

            byte[] postQuantumSecret = postQuantum.Decapsulate(
                encapsulation[classical.EncapsulationSize..],
                ExpandSeed(seed, PostQuantumSeedContext, postQuantum.SeedSize));

            return Combine(classicalSecret, postQuantumSecret, encapsulation.ToArray());
        }

        /// <inheritdoc cref="HybridSignatureScheme.ExpandSeed"/>
        byte[] ExpandSeed(ReadOnlySpan<byte> seed, byte[] context, int length)
            => secretCombiner.Derive(seed, [], context, length);

        /// <summary>
        /// Mixes both shared secrets into the final key. The encapsulations go in as the salt so the key is tied to
        /// the exact ciphertexts that carried it, which blocks an attacker from swapping one half for their own.
        /// </summary>
        /// <param name="classicalSecret"> Secret from the classical KEM. </param>
        /// <param name="postQuantumSecret"> Secret from the post-quantum KEM. </param>
        /// <param name="encapsulation"> Both encapsulations back to back. </param>
        /// <returns> Exactly <see cref="SharedSecretSize"/> bytes. </returns>
        byte[] Combine(byte[] classicalSecret, byte[] postQuantumSecret, byte[] encapsulation)
            => secretCombiner.Derive(
                HybridSignatureScheme.Concatenate(classicalSecret, postQuantumSecret),
                encapsulation,
                CombinerContext,
                SharedSecretSize);
    }
}
