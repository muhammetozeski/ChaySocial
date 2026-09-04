using System.Buffers.Binary;
using System.Text;

namespace ChaySocial.MainProject.Text
{
    /// <summary>
    /// Builds the exact byte string that a signature covers or a cipher authenticates. Every field is written with
    /// its length in front, so two different sets of fields can never produce the same bytes — without that, an
    /// attacker could move characters across a field boundary and reuse a signature for something it never said.
    /// </summary>
    public sealed class TranscriptWriter
    {
        readonly List<byte> _buffer = [];

        /// <summary> Appends raw bytes, preceded by their length. </summary>
        /// <param name="value"> Bytes to append. </param>
        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(length, value.Length);

            _buffer.AddRange(length);
            _buffer.AddRange(value);
        }

        /// <summary> Appends text as UTF-8, preceded by its byte length. </summary>
        /// <param name="value"> Text to append. </param>
        public void WriteText(string value) => WriteBytes(Encoding.UTF8.GetBytes(value));

        /// <summary> Appends a 64-bit number in big-endian order, so the same value writes identically on every machine. </summary>
        /// <param name="value"> Number to append. </param>
        public void WriteInt64(long value)
        {
            Span<byte> encoded = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64BigEndian(encoded, value);
            WriteBytes(encoded);
        }

        /// <summary> Finishes the transcript. </summary>
        /// <returns> Everything written so far, in order. </returns>
        public byte[] ToArray() => [.. _buffer];
    }
}

