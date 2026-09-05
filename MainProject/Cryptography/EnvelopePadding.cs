using System.Buffers.Binary;
using System.Text;

namespace ChaySocial.MainProject.Cryptography
{
    /// <summary>
    /// Makes every sealed letter one of a handful of sizes, so that holding the whole collection tells you nothing
    /// about what any letter says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sealed body is exactly as long as what went into it plus the authentication tag, which means the one thing
    /// a stored letter says perfectly is its length. "ok" and a confession look nothing alike from the outside, two
    /// accounts writing the same shapes at the same moments match on shape alone with no key needed, and a long
    /// letter is visibly a long letter. Rounding every body up to the next rung of a ladder takes that away.
    /// </para>
    /// <para>
    /// The padding sits inside the sealed envelope, so the authentication tag already covers it and nothing that
    /// signs or verifies a message has to change. An ordinary chat line costs half a kilobyte instead of a few
    /// dozen bytes; the longest letter the app allows — two thousand characters, at worst four bytes each — lands
    /// on the eight kilobyte rung.
    /// </para>
    /// </remarks>
    public static class EnvelopePadding
    {
        /// <summary> Room the real length is written into, ahead of the body. </summary>
        const int LengthHeaderBytes = 4;

        /// <summary>
        /// Smallest a padded body ever gets. Half a kilobyte rather than eight, so an ordinary line of conversation
        /// does not carry the cost of the longest letter somebody could have written.
        /// </summary>
        const int SmallestPaddedBodyBytes = 512;

        /// <summary> How far apart the rungs are. Fewer, wider rungs hide more and cost more; this is the middle. </summary>
        const int PaddedBodyStepBytes = 512;

        /// <summary>
        /// Wraps a body so that what comes out is one of the ladder's sizes and says nothing about what went in.
        /// </summary>
        /// <param name="body"> The bytes to hide the length of. </param>
        /// <returns> The real length, the body, and zeroes up to the next rung. </returns>
        public static byte[] Pad(ReadOnlySpan<byte> body)
        {
            byte[] padded = new byte[RungFor(LengthHeaderBytes + body.Length)];

            BinaryPrimitives.WriteInt32BigEndian(padded, body.Length);
            body.CopyTo(padded.AsSpan(LengthHeaderBytes));

            return padded;
        }

        /// <summary>
        /// Reads a body back out, if what was handed over is padded at all.
        /// </summary>
        /// <param name="padded"> The bytes that came out of the envelope. </param>
        /// <param name="text"> The text that was hidden in them; empty when this is not a padded body. </param>
        /// <returns> True when the bytes were padded by <see cref="Pad"/>. </returns>
        /// <remarks>
        /// All three conditions have to hold: the whole thing is exactly a rung, the declared length fits inside
        /// what is left, and every byte after the body is zero. Checking only the size would call an old unpadded
        /// letter padded whenever its length happened to land on a rung, and it would then be read as nonsense.
        /// </remarks>
        public static bool TryUnpad(ReadOnlySpan<byte> padded, out string text)
        {
            text = string.Empty;

            if (!IsRung(padded.Length)) return false;

            int declaredLength = BinaryPrimitives.ReadInt32BigEndian(padded);
            if (declaredLength < 0 || declaredLength > padded.Length - LengthHeaderBytes) return false;

            if (padded[(LengthHeaderBytes + declaredLength)..].IndexOfAnyExcept((byte)0) >= 0) return false;

            text = Encoding.UTF8.GetString(padded.Slice(LengthHeaderBytes, declaredLength));
            return true;
        }

        /// <summary> The rung a given number of bytes rounds up to. </summary>
        /// <param name="needed"> How many bytes actually have to fit. </param>
        /// <returns> The size to allocate. </returns>
        static int RungFor(int needed)
        {
            int atLeast = Math.Max(needed, SmallestPaddedBodyBytes);
            int steps = (atLeast + PaddedBodyStepBytes - 1) / PaddedBodyStepBytes;

            return steps * PaddedBodyStepBytes;
        }

        /// <summary> True when a length is one of the ladder's sizes. </summary>
        /// <param name="length"> The length to judge. </param>
        /// <returns> True when a padded body could be exactly this long. </returns>
        static bool IsRung(int length) => length >= SmallestPaddedBodyBytes && length % PaddedBodyStepBytes == 0;
    }
}
