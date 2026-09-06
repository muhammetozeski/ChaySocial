using System.Buffers.Binary;
using System.Text;

namespace ChaySocial.MainProject.Cryptography
{
    /// <summary>
    /// What actually goes inside a sealed letter: the moment it was written, and the words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stored letter's timestamp used to be the exact millisecond it was sent, in the clear, on a document
    /// anybody holding the collection can read. That is a clock nobody encrypted: the hour somebody is awake, the
    /// rhythm of their typing, and above all that this address wrote 1.4 seconds after that one — which ties two
    /// accounts together without opening a single letter. Moving the exact moment inside the seal and leaving a
    /// coarse one outside takes that away while leaving the conversation in the right order for the two people
    /// who can read it.
    /// </para>
    /// <para>
    /// The first byte is a UTF-8 continuation byte, which can never start valid text. A letter written before this
    /// existed is plain UTF-8 and so can never be mistaken for one carrying a time, and a body that does carry one
    /// can never be mistaken for words.
    /// </para>
    /// </remarks>
    public static class SealedBody
    {
        /// <summary>
        /// Marks a body as carrying a time ahead of its words. A continuation byte: no valid UTF-8 text can begin
        /// with one, so an old letter and a new one are told apart by the bytes themselves rather than by a guess.
        /// </summary>
        const byte SealedBodyMarkerByte = 0x9C;

        /// <summary> Room the exact moment is written into, big-endian, as this app writes every number. </summary>
        const int ExactSendTimeByteCount = sizeof(long);

        /// <summary> Puts the moment and the words together, ready to be padded and sealed. </summary>
        /// <param name="exactSendTimeUnixMs"> The real moment, to the millisecond. </param>
        /// <param name="text"> What was written. </param>
        /// <returns> The bytes to seal. </returns>
        public static byte[] Write(long exactSendTimeUnixMs, string text)
        {
            byte[] words = Encoding.UTF8.GetBytes(text);
            byte[] body = new byte[1 + ExactSendTimeByteCount + words.Length];

            body[0] = SealedBodyMarkerByte;
            BinaryPrimitives.WriteInt64BigEndian(body.AsSpan(1, ExactSendTimeByteCount), exactSendTimeUnixMs);
            words.CopyTo(body.AsSpan(1 + ExactSendTimeByteCount));

            return body;
        }

        /// <summary>
        /// Reads a body back, whether or not it carries a time.
        /// </summary>
        /// <param name="body"> The bytes that came out of the padding. </param>
        /// <param name="text"> Receives what was written. </param>
        /// <param name="exactSendTimeUnixMs"> Receives the real moment, or zero when the body carries none. </param>
        /// <returns> True when the body carried a time as well as words. </returns>
        /// <remarks>
        /// A body with no marker is a letter written before letters carried their own clock, and it is read as
        /// plain text exactly as it always was.
        /// </remarks>
        public static bool Read(ReadOnlySpan<byte> body, out string text, out long exactSendTimeUnixMs)
        {
            exactSendTimeUnixMs = 0;

            if (body.Length < 1 + ExactSendTimeByteCount || body[0] != SealedBodyMarkerByte)
            {
                text = Encoding.UTF8.GetString(body);
                return false;
            }

            exactSendTimeUnixMs = BinaryPrimitives.ReadInt64BigEndian(body.Slice(1, ExactSendTimeByteCount));
            text = Encoding.UTF8.GetString(body[(1 + ExactSendTimeByteCount)..]);
            return true;
        }
    }
}
