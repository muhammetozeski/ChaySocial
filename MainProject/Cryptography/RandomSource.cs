using Org.BouncyCastle.Security;

namespace ChaySocial.MainProject.Cryptography
{
    /// <summary> The one place random bytes are produced, so no call site ever reaches for a weaker generator. </summary>
    public static class RandomSource
    {
        /// <summary> Shared cryptographic generator handed to the BouncyCastle primitives that need one. </summary>
        public static readonly SecureRandom Secure = new();

        /// <summary> Produces unpredictable bytes. </summary>
        /// <param name="length"> Number of bytes to produce. </param>
        /// <returns> A fresh array of <paramref name="length"/> random bytes. </returns>
        public static byte[] Next(int length)
        {
            byte[] bytes = new byte[length];
            Secure.NextBytes(bytes);
            return bytes;
        }
    }

    /// <summary> Argument checks the cryptography primitives repeat, kept in one place so their failure messages stay identical. </summary>
    static class CryptographicGuard
    {
        /// <summary> Rejects key material of the wrong length before it reaches an algorithm, where the failure would be far harder to read. </summary>
        /// <param name="material"> The bytes handed in by the caller. </param>
        /// <param name="expectedLength"> Length the algorithm requires. </param>
        /// <param name="parameterName"> Name of the caller's parameter, quoted in the exception. </param>
        /// <param name="algorithmName"> Algorithm doing the checking, quoted in the exception. </param>
        internal static void RequireLength(ReadOnlySpan<byte> material, int expectedLength, string parameterName, string algorithmName)
        {
            if (material.Length == expectedLength) return;

            throw new ArgumentException(
                $"{algorithmName} expects {parameterName} to be {expectedLength} bytes but received {material.Length}.",
                parameterName);
        }
    }
}

