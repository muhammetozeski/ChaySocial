using System.Buffers.Binary;
using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary> How far one reader got into one conversation. </summary>
    /// <param name="PostId"> The conversation. </param>
    /// <param name="LastSeenReplyAtUnixMs"> The moment of the newest reply they had already seen. </param>
    public readonly record struct ReadingMark(string PostId, long LastSeenReplyAtUnixMs);

    /// <summary>
    /// Somebody's own book of reading marks, sealed so that only they can read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Coming back to a thirty-reply conversation the next day means scrolling it from the top and trying to
    /// remember where the reading stopped. This ends that, and it does so without the server learning anything
    /// past "this address reads": which conversation and how far into it are both inside the seal.
    /// </para>
    /// <para>
    /// Sealed to the reader's own key, the way a shelf is. There is no second party, so the encapsulation is made
    /// to the reader and opened by them.
    /// </para>
    /// </remarks>
    public static class ReadingMarkService
    {
        /// <summary>
        /// How coarse the stored time is. It says which minute somebody last put a book down, not which second —
        /// and nothing at all about which conversation they put down.
        /// </summary>
        const long MarkTimeBucketMs = 60_000;

        /// <summary> What the sealed body is, so a body from another kind of envelope cannot be read as one of these. </summary>
        static readonly byte[] ReadingMarkEnvelopeDomain = "ChaySocial/ReadingMark/v1"u8.ToArray();

        /// <summary> Reads and opens one account's book of marks. </summary>
        /// <param name="reader"> The unlocked account whose book it is. </param>
        /// <returns> Every mark they carry, or an empty list when there is no book or it will not open. </returns>
        /// <remarks>
        /// A book that will not open is not an error anybody should see. It means a document under this address
        /// that this key cannot read, and a reader who loses their marks simply starts a conversation from the top.
        /// </remarks>
        public static async Task<IReadOnlyList<ReadingMark>> ReadMarksAsync(PrivateIdentity reader)
        {
            ReadingMarkData? stored = await AppServices.Documents.ReadAsync(
                new DocumentId<ReadingMarkData>(reader.Public.Address));

            return stored is null || !TryOpen(reader, stored, out IReadOnlyList<ReadingMark> marks) ? [] : marks;
        }

        /// <summary>
        /// Moves one conversation's mark, leaving every other mark where it is.
        /// </summary>
        /// <param name="reader"> The unlocked account whose book it is. </param>
        /// <param name="postId"> The conversation being marked. </param>
        /// <param name="lastSeenReplyAtUnixMs"> The moment of the newest reply this reader has now seen. </param>
        /// <returns> A task that completes once the whole book has been sealed again. </returns>
        /// <remarks>
        /// The book is one document, so moving one mark rewrites all of them. That is the price of a store that
        /// cannot be asked for "this reader's mark on this post" without being told which post it is.
        /// </remarks>
        public static async Task WriteMarkAsync(PrivateIdentity reader, string postId, long lastSeenReplyAtUnixMs)
        {
            if (postId.Length == 0) return;

            List<ReadingMark> marks = [.. await ReadMarksAsync(reader)];

            int existing = marks.FindIndex(mark => mark.PostId == postId);
            if (existing >= 0)
            {
                // Never moved backwards. A reader who opens a thread on a second device, sees less of it and
                // leaves would otherwise lose the ground they covered on the first.
                if (marks[existing].LastSeenReplyAtUnixMs >= lastSeenReplyAtUnixMs) return;

                marks[existing] = new ReadingMark(postId, lastSeenReplyAtUnixMs);
            }
            else
            {
                marks.Add(new ReadingMark(postId, lastSeenReplyAtUnixMs));
            }

            // The mark that has stood still longest goes first: the book is for conversations somebody is still
            // in, and the one they last read a year ago is the one they are least likely to come back to.
            while (marks.Count > ReadingMarkData.MarksKeptPerReader)
            {
                int oldest = 0;
                for (int index = 1; index < marks.Count; index++)
                {
                    if (marks[index].LastSeenReplyAtUnixMs < marks[oldest].LastSeenReplyAtUnixMs) oldest = index;
                }

                marks.RemoveAt(oldest);
            }

            await SealAsync(reader, marks);
        }

        /// <summary> Seals a whole book and writes it over whatever was there. </summary>
        /// <param name="reader"> The unlocked account whose book it is. </param>
        /// <param name="marks"> Every mark to keep. </param>
        /// <returns> A task that completes once the document is stored. </returns>
        static async Task SealAsync(PrivateIdentity reader, IReadOnlyList<ReadingMark> marks)
        {
            long exactUpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long updatedAt = exactUpdatedAt - (exactUpdatedAt % MarkTimeBucketMs);

            EncapsulationResult secret = AppCryptography.Identities.EncapsulateTo(reader.Public);
            byte[] nonce = RandomSource.Next(AppCryptography.Cipher.NonceSize);
            byte[] associatedData = BuildAssociatedData(reader.Public.Address, updatedAt);

            // Padded before sealing: an unpadded body is exactly as long as its contents, and the number of
            // conversations somebody is following is the one thing a sealed book would otherwise tell perfectly.
            byte[] paddedBody = EnvelopePadding.Pad(WriteBody(marks));

            ReadingMarkData book = new()
            {
                ReaderAddress = reader.Public.Address,
                Encapsulation = Convert.ToBase64String(secret.Encapsulation),
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(
                    AppCryptography.Cipher.Encrypt(paddedBody, secret.SharedSecret, nonce, associatedData)),
                UpdatedAtUnixMs = updatedAt
            };

            await AppServices.Documents.WriteAsync(book.Id, book);
        }

        /// <summary> Opens one book with the reader's own key. </summary>
        /// <param name="reader"> The unlocked account. </param>
        /// <param name="stored"> The stored book. </param>
        /// <param name="marks"> Receives what was inside, or an empty list when it could not be opened. </param>
        /// <returns> True when it opened. </returns>
        static bool TryOpen(PrivateIdentity reader, ReadingMarkData stored, out IReadOnlyList<ReadingMark> marks)
        {
            marks = [];

            if (stored.ReaderAddress != reader.Public.Address) return false;

            if (!TryFromBase64(stored.Encapsulation, out byte[] encapsulation)
                || !TryFromBase64(stored.Nonce, out byte[] nonce)
                || !TryFromBase64(stored.Ciphertext, out byte[] ciphertext))
            {
                return false;
            }

            // Decapsulating throws on a wrong-length value, and a stored envelope is exactly the kind of hostile
            // input that carries one, so the length is checked here instead.
            if (encapsulation.Length != AppCryptography.KeyExchange.EncapsulationSize) return false;

            byte[] sharedSecret = reader.Decapsulate(encapsulation);
            byte[] associatedData = BuildAssociatedData(stored.ReaderAddress, stored.UpdatedAtUnixMs);

            if (!AppCryptography.Cipher.TryDecrypt(ciphertext, sharedSecret, nonce, associatedData, out byte[] plaintext))
            {
                return false;
            }

            byte[] body = EnvelopePadding.TryUnpad(plaintext, out byte[] unpadded) ? unpadded : plaintext;

            return ReadBody(body, out marks);
        }

        /// <summary> Writes the sealed body: how many marks there are, then each of them. </summary>
        /// <param name="marks"> The marks to seal. </param>
        /// <returns> The bytes to seal. </returns>
        /// <remarks>
        /// Written as plain fields rather than named ones. A named field writes nothing at all for an empty value,
        /// which is right for a signature that must stay stable and wrong for a body read back by position.
        /// </remarks>
        static byte[] WriteBody(IReadOnlyList<ReadingMark> marks)
        {
            TranscriptWriter body = new();
            body.WriteInt64(marks.Count);

            foreach (ReadingMark mark in marks)
            {
                body.WriteText(mark.PostId);
                body.WriteInt64(mark.LastSeenReplyAtUnixMs);
            }

            return body.ToArray();
        }

        /// <summary> Reads a sealed body back. </summary>
        /// <param name="body"> The opened bytes. </param>
        /// <param name="marks"> Receives the marks, or an empty list when the body is not one of these. </param>
        /// <returns> True when every mark the count promised was there. </returns>
        static bool ReadBody(ReadOnlyMemory<byte> body, out IReadOnlyList<ReadingMark> marks)
        {
            marks = [];

            TranscriptReader reader = new(body);

            if (!TryReadInt64(reader, out long count)) return false;
            if (count < 0 || count > ReadingMarkData.MarksKeptPerReader) return false;

            List<ReadingMark> read = new((int)count);
            for (long index = 0; index < count; index++)
            {
                if (!reader.TryReadText(out string postId)) return false;
                if (!TryReadInt64(reader, out long lastSeen)) return false;

                read.Add(new ReadingMark(postId, lastSeen));
            }

            marks = read;
            return true;
        }

        /// <summary>
        /// Reads one number back out of a body.
        /// </summary>
        /// <param name="reader"> The reader positioned on the number. </param>
        /// <param name="value"> Receives the number, or zero. </param>
        /// <returns> True when eight bytes were there. </returns>
        /// <remarks>
        /// <see cref="TranscriptReader"/> has no number reader of its own, so a number comes back as the eight
        /// bytes the writer laid down.
        /// </remarks>
        static bool TryReadInt64(TranscriptReader reader, out long value)
        {
            value = 0;

            if (!reader.TryReadBytes(out ReadOnlyMemory<byte> bytes) || bytes.Length != sizeof(long)) return false;

            value = BinaryPrimitives.ReadInt64BigEndian(bytes.Span);
            return true;
        }

        /// <summary>
        /// Builds what the cipher authenticates alongside the body: everything about this book that is stored in
        /// the clear, so none of it can be swapped under a seal that still opens.
        /// </summary>
        /// <param name="readerAddress"> Address of the reader. </param>
        /// <param name="updatedAtUnixMs"> The stored, rounded time. </param>
        /// <returns> The associated data. </returns>
        static byte[] BuildAssociatedData(string readerAddress, long updatedAtUnixMs)
        {
            TranscriptWriter associatedData = new();
            associatedData.WriteBytes(ReadingMarkEnvelopeDomain);
            associatedData.WriteText(readerAddress);
            associatedData.WriteInt64(updatedAtUnixMs);

            return associatedData.ToArray();
        }

        /// <summary> Reads base64 that came out of a store, where malformed text is an ordinary outcome. </summary>
        /// <param name="text"> The stored text. </param>
        /// <param name="bytes"> Receives the bytes, or an empty array. </param>
        /// <returns> True when it was base64. </returns>
        static bool TryFromBase64(string text, out byte[] bytes)
        {
            try
            {
                bytes = Convert.FromBase64String(text);
                return true;
            }
            catch (FormatException)
            {
                bytes = [];
                return false;
            }
        }
    }
}
