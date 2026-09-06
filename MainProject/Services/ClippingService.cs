using System.Buffers.Binary;
using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary> One thing on somebody's shelf, opened. </summary>
    /// <param name="ClippingId"> Id it is stored under, so it can be taken off again. </param>
    /// <param name="PostId"> The post that was kept. </param>
    /// <param name="Note"> What the keeper wrote about it, empty when they wrote nothing. </param>
    /// <param name="KeptAtUnixMs"> The moment it was kept, from inside the seal rather than off the document. </param>
    public readonly record struct KeptClipping(string ClippingId, string PostId, string Note, long KeptAtUnixMs);

    /// <summary>
    /// Somebody's own shelf of things worth coming back to, sealed so that only they can read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The way back to a post today is scrolling a feed backwards. This is the alternative, and it is sealed for
    /// the same reason a letter is: a list of what a person kept, with their own words about why, describes them
    /// better than anything they ever published. The server sees that an address keeps things and nothing else.
    /// </para>
    /// <para>
    /// Sealed to the keeper's own key, exactly the way a sender's own copy of a letter is. There is no second
    /// party here, so the encapsulation is made to the keeper and opened by them.
    /// </para>
    /// </remarks>
    public static class ClippingService
    {
        /// <summary> Clippings read in one page of a shelf. </summary>
        public const int ShelfPageSize = 100;

        /// <summary>
        /// How coarse the stored time is. The exact moment lives inside the seal; what is left outside says only
        /// which minute somebody was reading, not which second.
        /// </summary>
        const long KeptTimeBucketMs = 60_000;

        /// <summary> What the sealed body is, so a body from another kind of envelope cannot be read as one of these. </summary>
        static readonly byte[] ClippingEnvelopeDomain = "ChaySocial/Clipping/v1"u8.ToArray();

        /// <summary>
        /// Keeps one post on somebody's shelf, or hands back what is already there.
        /// </summary>
        /// <param name="keeper"> The unlocked account doing the keeping. </param>
        /// <param name="postId"> Post to keep. </param>
        /// <param name="note"> What they want to remember about it; trimmed, and refused when too long. </param>
        /// <returns> The clipping, or null when it could not be kept. </returns>
        /// <remarks>
        /// The shelf is read and opened before anything is written, because the id is random and cannot refuse a
        /// duplicate the way a keyed id would. That is the price of an id that tells the server nothing.
        /// </remarks>
        public static async Task<KeptClipping?> KeepAsync(PrivateIdentity keeper, string postId, string note)
        {
            if (postId.Length == 0) return null;

            string trimmed = note.Trim();
            if (trimmed.Length > ClippingData.MaximumNoteLength) return null;

            IReadOnlyList<KeptClipping> shelf = await ReadShelfAsync(keeper);
            foreach (KeptClipping kept in shelf)
            {
                if (kept.PostId == postId) return kept;
            }

            string clippingId = Base32.Encode(RandomSource.Next(ClippingData.ClippingIdBytes));
            long exactKeptAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long createdAt = exactKeptAt - (exactKeptAt % KeptTimeBucketMs);

            EncapsulationResult secret = AppCryptography.Identities.EncapsulateTo(keeper.Public);
            byte[] nonce = RandomSource.Next(AppCryptography.Cipher.NonceSize);
            byte[] associatedData = BuildAssociatedData(clippingId, keeper.Public.Address, createdAt);

            // Padded before sealing, the same way a letter is: an unpadded body is exactly as long as its contents,
            // and the length of a note is the one thing a sealed shelf would otherwise tell perfectly.
            byte[] paddedBody = EnvelopePadding.Pad(WriteBody(exactKeptAt, postId, trimmed));

            ClippingData clipping = new()
            {
                ClippingId = clippingId,
                KeeperAddress = keeper.Public.Address,
                Encapsulation = Convert.ToBase64String(secret.Encapsulation),
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(
                    AppCryptography.Cipher.Encrypt(paddedBody, secret.SharedSecret, nonce, associatedData)),
                CreatedAtUnixMs = createdAt
            };

            await AppServices.Documents.WriteAsync(clipping.Id, clipping);

            return new KeptClipping(clippingId, postId, trimmed, exactKeptAt);
        }

        /// <summary> Reads and opens one account's shelf, newest first. </summary>
        /// <param name="keeper"> The unlocked account whose shelf it is. </param>
        /// <param name="limit"> Largest number of clippings to return. </param>
        /// <returns> What they kept, in the order they kept it, newest first. </returns>
        /// <remarks>
        /// Sorted by the stored bucket rather than by the exact moment inside each seal, because the store cannot
        /// sort on something it cannot read. Within one minute the order is whatever the store hands back.
        /// </remarks>
        public static async Task<IReadOnlyList<KeptClipping>> ReadShelfAsync(PrivateIdentity keeper, int limit = ShelfPageSize)
        {
            DocumentQuery<ClippingData> query = new DocumentQuery<ClippingData>()
                .WithMatch(ClippingData.KeeperField, keeper.Public.Address)
                .WithSort(ClippingData.CreatedAtField, descending: true)
                .WithLimit(limit);

            IReadOnlyList<ClippingData> stored = (await AppServices.Documents.QueryAsync(query)).Documents;

            List<KeptClipping> opened = new(stored.Count);
            foreach (ClippingData clipping in stored)
            {
                // A clipping that will not open is somebody else's document under this address, or one this key
                // cannot read. It is left out rather than thrown over: one bad row must not cost a whole shelf.
                if (TryOpen(keeper, clipping, out KeptClipping kept)) opened.Add(kept);
            }

            return opened;
        }

        /// <summary> Takes one clipping off a shelf. </summary>
        /// <param name="keeper"> The unlocked account whose shelf it is. </param>
        /// <param name="clippingId"> The clipping to forget. </param>
        /// <returns> A task that completes once it is gone, or at once when it was not theirs. </returns>
        public static async Task ForgetAsync(PrivateIdentity keeper, string clippingId)
        {
            if (clippingId.Length == 0) return;

            DocumentId<ClippingData> id = new(clippingId);
            ClippingData? clipping = await AppServices.Documents.ReadAsync(id);

            if (clipping is null || clipping.KeeperAddress != keeper.Public.Address) return;

            await AppServices.Documents.DeleteAsync(id);
        }

        /// <summary> Opens one clipping with the keeper's own key. </summary>
        /// <param name="keeper"> The unlocked account. </param>
        /// <param name="clipping"> The stored clipping. </param>
        /// <param name="kept"> Receives what was inside, or nothing when it could not be opened. </param>
        /// <returns> True when it opened. </returns>
        static bool TryOpen(PrivateIdentity keeper, ClippingData clipping, out KeptClipping kept)
        {
            kept = default;

            if (clipping.KeeperAddress != keeper.Public.Address) return false;

            if (!TryFromBase64(clipping.Encapsulation, out byte[] encapsulation)
                || !TryFromBase64(clipping.Nonce, out byte[] nonce)
                || !TryFromBase64(clipping.Ciphertext, out byte[] ciphertext))
            {
                return false;
            }

            // Decapsulating throws on a wrong-length value, and a stored envelope is exactly the kind of hostile
            // input that carries one, so the length is checked here instead.
            if (encapsulation.Length != AppCryptography.KeyExchange.EncapsulationSize) return false;

            byte[] sharedSecret = keeper.Decapsulate(encapsulation);
            byte[] associatedData = BuildAssociatedData(clipping.ClippingId, clipping.KeeperAddress, clipping.CreatedAtUnixMs);

            if (!AppCryptography.Cipher.TryDecrypt(ciphertext, sharedSecret, nonce, associatedData, out byte[] plaintext))
            {
                return false;
            }

            byte[] body = EnvelopePadding.TryUnpad(plaintext, out byte[] unpadded) ? unpadded : plaintext;

            if (!ReadBody(body, out long keptAt, out string postId, out string note)) return false;

            kept = new KeptClipping(clipping.ClippingId, postId, note, keptAt);
            return true;
        }

        /// <summary>
        /// Writes the sealed body: the exact moment, the post, and the note.
        /// </summary>
        /// <param name="exactKeptAtUnixMs"> The moment it was kept, which is not outside the seal. </param>
        /// <param name="postId"> The post kept. </param>
        /// <param name="note"> The keeper's note. </param>
        /// <returns> The bytes to seal. </returns>
        /// <remarks>
        /// Written as three plain fields rather than named ones. A named field writes nothing at all for an empty
        /// value, and an empty note would then shift what the reader finds where — which is right for a signature
        /// that must stay stable and wrong for a body that is read back by position.
        /// </remarks>
        static byte[] WriteBody(long exactKeptAtUnixMs, string postId, string note)
        {
            TranscriptWriter body = new();
            body.WriteInt64(exactKeptAtUnixMs);
            body.WriteText(postId);
            body.WriteText(note);

            return body.ToArray();
        }

        /// <summary> Reads a sealed body back. </summary>
        /// <param name="body"> The opened bytes. </param>
        /// <param name="keptAtUnixMs"> Receives the exact moment. </param>
        /// <param name="postId"> Receives the post kept. </param>
        /// <param name="note"> Receives the note. </param>
        /// <returns> True when all three fields were there. </returns>
        static bool ReadBody(ReadOnlyMemory<byte> body, out long keptAtUnixMs, out string postId, out string note)
        {
            keptAtUnixMs = 0;
            postId = string.Empty;
            note = string.Empty;

            TranscriptReader reader = new(body);

            if (!reader.TryReadBytes(out ReadOnlyMemory<byte> moment) || moment.Length != sizeof(long)) return false;
            if (!reader.TryReadText(out postId)) return false;
            if (!reader.TryReadText(out note)) return false;

            keptAtUnixMs = BinaryPrimitives.ReadInt64BigEndian(moment.Span);
            return true;
        }

        /// <summary>
        /// Builds what the cipher authenticates alongside the body: everything about this clipping that is stored
        /// in the clear, so none of it can be swapped under a seal that still opens.
        /// </summary>
        /// <param name="clippingId"> The clipping's id. </param>
        /// <param name="keeperAddress"> Address of the keeper. </param>
        /// <param name="createdAtUnixMs"> The stored, rounded time. </param>
        /// <returns> The associated data. </returns>
        static byte[] BuildAssociatedData(string clippingId, string keeperAddress, long createdAtUnixMs)
        {
            TranscriptWriter associatedData = new();
            associatedData.WriteBytes(ClippingEnvelopeDomain);
            associatedData.WriteText(clippingId);
            associatedData.WriteText(keeperAddress);
            associatedData.WriteInt64(createdAtUnixMs);

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
