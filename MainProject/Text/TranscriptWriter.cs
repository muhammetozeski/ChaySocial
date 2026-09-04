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

        /// <summary>
        /// Appends a named field, and only when it carries something. This is what lets a record grow a new field
        /// without invalidating every signature written before that field existed: a record that leaves the new
        /// field empty writes exactly the bytes it always did.
        /// </summary>
        /// <param name="name"> The field's name, written alongside its value so no two fields can be confused for one another. </param>
        /// <param name="value"> The field's value; an empty one writes nothing at all. </param>
        /// <remarks>
        /// Dropping an empty field is safe because the name travels with the value: removing a field from a signed
        /// record changes the transcript and the signature stops verifying. What it cannot tell apart is a field
        /// that is empty from one that was never there — which is the same thing everywhere it is used, since an
        /// empty id means "belongs to nothing" rather than a different value.
        /// </remarks>
        public void WriteNamedText(string name, string value)
        {
            if (value.Length == 0) return;

            WriteText(name);
            WriteText(value);
        }

        /// <summary> Finishes the transcript. </summary>
        /// <returns> Everything written so far, in order. </returns>
        public byte[] ToArray() => [.. _buffer];
    }
}

