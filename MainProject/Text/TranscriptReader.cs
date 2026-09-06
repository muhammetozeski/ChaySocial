using System.Buffers.Binary;
using System.Text;

namespace ChaySocial.MainProject.Text
{
    /// <summary>
    /// Reads back what a <see cref="TranscriptWriter"/> wrote. The two are a pair: fields come out in the order
    /// they went in, each still carrying the length that keeps one field from bleeding into the next.
    /// </summary>
    /// <remarks>
    /// A transcript is usually only ever hashed or signed, and nothing needs to read one back. It does when the
    /// same shape is used to seal something a reader has to open again — then the length prefixes are what make
    /// the pieces come apart exactly where they were joined, whatever bytes were inside them.
    /// </remarks>
    /// <param name="content"> The bytes a writer produced. </param>
    public sealed class TranscriptReader(ReadOnlyMemory<byte> content)
    {
        /// <summary> How far through the bytes the reading has got. </summary>
        int _at;

        /// <summary> True once every field has been read and nothing is left over. </summary>
        public bool IsFinished => _at == content.Length;

        /// <summary> Reads the next field's bytes. </summary>
        /// <param name="value"> Receives the field, or nothing when the transcript is malformed or exhausted. </param>
        /// <returns> True when a whole field was there to read. </returns>
        public bool TryReadBytes(out ReadOnlyMemory<byte> value)
        {
            value = default;

            if (content.Length - _at < sizeof(int)) return false;

            int length = BinaryPrimitives.ReadInt32BigEndian(content.Span.Slice(_at, sizeof(int)));
            if (length < 0 || content.Length - _at - sizeof(int) < length) return false;

            value = content.Slice(_at + sizeof(int), length);
            _at += sizeof(int) + length;
            return true;
        }

        /// <summary> Reads the next field as UTF-8 text. </summary>
        /// <param name="value"> Receives the text, or an empty string when there was no whole field to read. </param>
        /// <returns> True when a whole field was there to read. </returns>
        public bool TryReadText(out string value)
        {
            if (!TryReadBytes(out ReadOnlyMemory<byte> bytes))
            {
                value = string.Empty;
                return false;
            }

            value = Encoding.UTF8.GetString(bytes.Span);
            return true;
        }
    }
}
