using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace ChaySocial.MainProject.Cryptography
{
    /// <summary>
    /// HKDF over SHA-512. Expands material that is already unguessable — a master seed, a KEM secret — into as many
    /// independent keys as are needed. It is fast on purpose, which also makes it the wrong tool for a passphrase:
    /// use <see cref="Argon2idKeyDerivation"/> there.
    /// </summary>
    public sealed class HkdfKeyDerivation : IKeyDerivation
    {
        public string Name => "HKDF-SHA512";
        public bool IsPassphraseHardened => false;

        public byte[] Derive(ReadOnlySpan<byte> inputKeyMaterial, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> context, int outputLength)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputLength);

            HkdfBytesGenerator generator = new(new Sha512Digest());
            generator.Init(new HkdfParameters(inputKeyMaterial.ToArray(), salt.ToArray(), context.ToArray()));

            byte[] output = new byte[outputLength];
            generator.GenerateBytes(output, 0, outputLength);
            return output;
        }
    }

    /// <summary>
    /// Argon2id. Deliberately slow and memory-hungry so that guessing a passphrase costs an attacker real hardware
    /// time per guess. This is what stands between a stolen encrypted identity file and the seed inside it.
    /// </summary>
    /// <param name="memoryKibibytes"> Memory each attempt must allocate. Higher costs an attacker more but must still fit on the weakest device that has to unlock an identity. </param>
    /// <param name="iterations"> Passes over that memory. </param>
    /// <param name="parallelism"> Lanes used per attempt. </param>
    public sealed class Argon2idKeyDerivation(
        int memoryKibibytes = Argon2idKeyDerivation.DefaultMemoryKibibytes,
        int iterations = Argon2idKeyDerivation.DefaultIterations,
        int parallelism = Argon2idKeyDerivation.DefaultParallelism) : IKeyDerivation
    {
        /// <summary> 64 MiB — heavy enough to hurt a guessing rig, light enough for a phone and for a browser tab. </summary>
        public const int DefaultMemoryKibibytes = 64 * 1024;

        public const int DefaultIterations = 3;
        public const int DefaultParallelism = 1;

        public string Name => "Argon2id";
        public bool IsPassphraseHardened => true;

        public byte[] Derive(ReadOnlySpan<byte> inputKeyMaterial, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> context, int outputLength)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputLength);

            Argon2Parameters parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
                .WithVersion(Argon2Parameters.Version13)
                .WithSalt(salt.ToArray())
                .WithAdditional(context.ToArray())
                .WithMemoryAsKB(memoryKibibytes)
                .WithIterations(iterations)
                .WithParallelism(parallelism)
                .Build();

            Argon2BytesGenerator generator = new();
            generator.Init(parameters);

            byte[] output = new byte[outputLength];
            generator.GenerateBytes(inputKeyMaterial.ToArray(), output);
            return output;
        }
    }
}

