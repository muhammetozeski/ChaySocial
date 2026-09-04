using System.Diagnostics.CodeAnalysis;
using System.Text;
using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary> A piece of media recovered from a vanishing message, together with the attachment describing it. </summary>
    /// <param name="Attachment"> The attachment, carrying the media type the bytes should be drawn as. </param>
    /// <param name="Content"> The decrypted bytes, which exist nowhere else once this is handed over. </param>
    public readonly record struct RevealedMedia(MediaAttachment Attachment, byte[] Content);

    /// <summary>
    /// Everything a vanishing message held, handed over once. Both the words and the media come back together
    /// because both are destroyed together — there is no second chance to fetch either.
    /// </summary>
    /// <param name="Text"> The message body. </param>
    /// <param name="Media"> The media that came with it, already decrypted. </param>
    public readonly record struct RevealedMessage(string Text, IReadOnlyList<RevealedMedia> Media);

    /// <summary>
    /// One row of the inbox: the newest message of a conversation, and who that conversation is with. What it carries
    /// is the envelope, not the words — the body is still ciphertext and only turns back into text on the recipient's
    /// device, through <see cref="MessageService.TryDecrypt"/>.
    /// </summary>
    /// <param name="ConversationId"> Conversation this row stands for. </param>
    /// <param name="OtherAddress"> The participant who is not the account reading the inbox. </param>
    /// <param name="NewestMessage"> Most recent message of that conversation, still encrypted. </param>
    /// <param name="NewestAtUnixMs"> When that message was sent, so rows can be ordered without opening them. </param>
    public sealed record ConversationSummary(
        string ConversationId,
        string OtherAddress,
        MessageData NewestMessage,
        long NewestAtUnixMs);

    /// <summary>
    /// Direct messages, encrypted end to end. A message is sealed on the sender's device to a secret encapsulated to
    /// the recipient's published key, so what reaches storage is an envelope: who wrote to whom and when, and a block
    /// of ciphertext nobody else holds a key for. The server can list, count and deliver messages, and can never read
    /// one — not even the excerpt in the alert it raises, which is deliberately left empty.
    /// </summary>
    public static class MessageService
    {
        /// <summary>
        /// Separates a message signature from a post's or a comment's, so a signature lifted from one can never be
        /// presented as the other.
        /// </summary>
        static readonly byte[] MessageSignatureDomain = "ChaySocial/Message/v1"u8.ToArray();

        /// <summary>
        /// Labels the data the cipher authenticates alongside the body. It binds the ciphertext to one conversation,
        /// one pair of participants and one moment, so an envelope moved into another conversation fails its tag
        /// check instead of decrypting into a message it was never part of.
        /// </summary>
        static readonly byte[] MessageEnvelopeDomain = "ChaySocial/MessageEnvelope/v1"u8.ToArray();

        /// <summary> Messages fetched in one page of a conversation. </summary>
        public const int ConversationPageSize = 50;

        /// <summary> Conversations shown in one page of the inbox. </summary>
        public const int InboxPageSize = 30;

        /// <summary>
        /// Encrypts a message to one account, signs the envelope, stores it, and raises a contentless alert. The
        /// plaintext exists only on this device: it is never written to <see cref="MessageData"/> and never handed to
        /// the notification.
        /// </summary>
        /// <param name="sender"> The unlocked account writing the message. </param>
        /// <param name="recipientProfile"> Profile of the account being written to, carrying the key to encrypt against. </param>
        /// <param name="text"> What to send; trimmed, and refused when empty or over <see cref="MessageData.MaximumTextLength"/>. </param>
        /// <returns>
        /// The stored envelope, or null when the text was not sendable or the recipient's profile could not be
        /// trusted to carry that account's real encryption key.
        /// </returns>
        /// <param name="isVanishing">
        /// True sends a message the recipient may read exactly once. Its encrypted body is stored as a blob rather
        /// than inside the document, because a blob read can destroy the bytes in the same step — so once it has
        /// been opened the server has nothing left to serve, to anyone.
        /// </param>
        /// <param name="attachments"> Media already uploaded for this message, or null for a message that is only text. </param>
        /// <param name="quotedMessageId"> Message this one replies to, or empty when it replies to nothing. </param>
        public static async Task<MessageData?> SendAsync(
            PrivateIdentity sender,
            ProfileData recipientProfile,
            string text,
            bool isVanishing = false,
            IReadOnlyList<MediaAttachment>? attachments = null,
            string quotedMessageId = "")
        {
            string trimmed = text.Trim();
            IReadOnlyList<MediaAttachment> media = attachments ?? [];

            // A message has to carry something: words or media.
            if (trimmed.Length > MessageData.MaximumTextLength) return null;
            if (trimmed.Length == 0 && media.Count == 0) return null;
            if (isVanishing && AppServices.Blobs is null) return null;

            if (!TryReadPublishedKeys(recipientProfile, out PublicIdentity? recipient)) return null;

            if (!CanEncryptTo(recipient))
            {
                Log($"Profile '{recipientProfile.Address}' does not commit to the encryption key beside it; refusing to send.", LogLevel.Warning);
                return null;
            }

            string senderAddress = sender.Public.Address;
            string recipientAddress = recipient.Address;
            string conversationId = MessageData.ConversationIdFor(senderAddress, recipientAddress);
            string messageId = Base32.Encode(RandomSource.Next(MessageIdBytes));
            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            EncapsulationResult secret = AppCryptography.Identities.EncapsulateTo(recipient);
            byte[] nonce = RandomSource.Next(AppCryptography.Cipher.NonceSize);
            byte[] associatedData = BuildAssociatedData(conversationId, senderAddress, recipientAddress, createdAt);

            byte[] ciphertext = AppCryptography.Cipher.Encrypt(
                Encoding.UTF8.GetBytes(trimmed),
                secret.SharedSecret,
                nonce,
                associatedData);

            string ciphertextDigest = DigestOf(ciphertext);

            byte[] transcript = BuildTranscript(
                messageId,
                conversationId,
                senderAddress,
                recipientAddress,
                secret.Encapsulation,
                nonce,
                ciphertextDigest,
                createdAt,
                media,
                quotedMessageId);

            // A vanishing body goes to the blob store and leaves the document empty; an ordinary one rides inside
            // the document as usual. Either way the signature above already covers the same ciphertext.
            string vanishingBlobId = string.Empty;
            if (isVanishing)
            {
                string? uploaded = await AppServices.Blobs!.UploadAsync(ciphertext);
                if (uploaded is null) return null;

                vanishingBlobId = uploaded;
            }

            MessageData message = new()
            {
                MessageId = messageId,
                ConversationId = conversationId,
                SenderAddress = senderAddress,
                RecipientAddress = recipientAddress,
                Encapsulation = Convert.ToBase64String(secret.Encapsulation),
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = isVanishing ? string.Empty : Convert.ToBase64String(ciphertext),
                VanishingBlobId = vanishingBlobId,
                CiphertextDigest = ciphertextDigest,
                Attachments = media,
                QuotedMessageId = quotedMessageId,
                CreatedAtUnixMs = createdAt,
                Signature = Convert.ToBase64String(sender.Sign(transcript))
            };

            await AppServices.Documents.WriteAsync(message.Id, message);

            // The alert points at the conversation and says nothing about it. An excerpt here would hand the server
            // the one thing the encryption exists to keep from it.
            await NotificationService.NotifyAsync(
                recipientAddress,
                senderAddress,
                NotificationKind.Message,
                conversationId);

            MainEvents.Trigger(MainEvents.Names.MessagesChanged, conversationId);
            return message;
        }

        /// <summary>
        /// Turns one stored envelope back into text. Only the account the message was addressed to can do this: the
        /// secret was encapsulated to that account's key, so the sender's own copy of what they wrote is unreadable
        /// to them once it has left. A message that fails to open is a normal outcome — a forged envelope, one moved
        /// out of its conversation, or one meant for somebody else — and is reported as false, not as an error.
        /// </summary>
        /// <param name="reader"> The unlocked account trying to read it. </param>
        /// <param name="message"> Envelope to open. </param>
        /// <param name="text"> Receives the message body, or an empty string when it could not be opened. </param>
        /// <returns> True when the envelope was addressed to <paramref name="reader"/> and its tag verified. </returns>
        public static bool TryDecrypt(PrivateIdentity reader, MessageData message, out string text)
        {
            text = string.Empty;

            if (message.RecipientAddress != reader.Public.Address) return false;

            if (!TryDecodeBase64(message.Encapsulation, nameof(message.Encapsulation), message.MessageId, out byte[] encapsulation)
                || !TryDecodeBase64(message.Nonce, nameof(message.Nonce), message.MessageId, out byte[] nonce)
                || !TryDecodeBase64(message.Ciphertext, nameof(message.Ciphertext), message.MessageId, out byte[] ciphertext))
            {
                return false;
            }

            // Decapsulating rejects a wrong-length value by throwing, and a stored envelope is exactly the kind of
            // hostile input that carries one, so its length is checked here instead.
            if (encapsulation.Length != AppCryptography.KeyExchange.EncapsulationSize) return false;

            byte[] sharedSecret = reader.Decapsulate(encapsulation);

            byte[] associatedData = BuildAssociatedData(
                message.ConversationId,
                message.SenderAddress,
                message.RecipientAddress,
                message.CreatedAtUnixMs);

            if (!AppCryptography.Cipher.TryDecrypt(ciphertext, sharedSecret, nonce, associatedData, out byte[] plaintext)) return false;

            text = Encoding.UTF8.GetString(plaintext);
            return true;
        }

        /// <summary>
        /// Opens a vanishing message, destroying it as it is read. The server hands the bytes over and deletes
        /// them in the same step, so calling this twice returns nothing the second time — the message is gone
        /// whether or not the reader ever managed to look at it, which is what "read once" has to mean if it is
        /// to mean anything.
        /// </summary>
        /// <param name="reader"> The unlocked account the message was addressed to. </param>
        /// <param name="message"> The vanishing envelope to open. </param>
        /// <param name="cancellationToken"> Cancels the fetch. </param>
        /// <returns> The message body, or null when it was already opened, addressed elsewhere, or could not be decrypted. </returns>
        public static async Task<RevealedMessage?> ConsumeVanishingAsync(PrivateIdentity reader, MessageData message, CancellationToken cancellationToken = default)
        {
            if (!message.IsVanishing || AppServices.Blobs is null) return null;
            if (message.RecipientAddress != reader.Public.Address) return null;

            byte[]? ciphertext = await AppServices.Blobs.ConsumeAsync(message.VanishingBlobId, cancellationToken);
            if (ciphertext is null) return null;

            // The media goes the same way as the body: fetched and destroyed together, so a picture sent to be
            // seen once cannot be fetched again by anybody who later gets hold of the message document.
            List<RevealedMedia> revealedMedia = [];
            foreach (MediaAttachment attachment in message.Attachments)
            {
                byte[]? content = await MediaService.ConsumeAsync(attachment, cancellationToken);
                if (content is not null) revealedMedia.Add(new RevealedMedia(attachment, content));
            }

            // The body arrived separately from the document that vouches for it, so it is checked against the
            // digest the sender signed before any of it is trusted.
            if (DigestOf(ciphertext) != message.CiphertextDigest)
            {
                Log($"Vanishing message '{message.MessageId}' arrived with a body its signature does not cover.", LogLevel.Warning);
                return null;
            }

            // The document is dropped too, so the conversation stops listing an envelope whose body no longer exists.
            // No event is raised: the only screen that cares is the one that just did this, and telling it to reload
            // would pull the message out from under the reader before they had read it.
            await AppServices.Documents.DeleteAsync(message.Id, cancellationToken);

            if (!TryDecodeBase64(message.Encapsulation, nameof(message.Encapsulation), message.MessageId, out byte[] encapsulation)
                || !TryDecodeBase64(message.Nonce, nameof(message.Nonce), message.MessageId, out byte[] nonce)
                || encapsulation.Length != AppCryptography.KeyExchange.EncapsulationSize)
            {
                return null;
            }

            byte[] associatedData = BuildAssociatedData(
                message.ConversationId,
                message.SenderAddress,
                message.RecipientAddress,
                message.CreatedAtUnixMs);

            return AppCryptography.Cipher.TryDecrypt(ciphertext, reader.Decapsulate(encapsulation), nonce, associatedData, out byte[] plaintext)
                ? new RevealedMessage(Encoding.UTF8.GetString(plaintext), revealedMedia)
                : null;
        }

        /// <summary>
        /// Checks that a message really was sent by the account it names, using the signing key published in that
        /// account's profile. A message that fails this was altered or forged after it left its sender, whether or
        /// not it still decrypts.
        /// </summary>
        /// <param name="message"> Message to check. </param>
        /// <param name="senderProfile"> Profile of the account the message names, or null when it could not be read. </param>
        /// <returns> True when the signature verifies against the sender's published key. </returns>
        public static bool VerifySender(MessageData message, ProfileData? senderProfile)
        {
            if (senderProfile is null || senderProfile.Address != message.SenderAddress) return false;
            if (!TryReadPublishedKeys(senderProfile, out PublicIdentity? sender)) return false;

            if (!TryDecodeBase64(message.Encapsulation, nameof(message.Encapsulation), message.MessageId, out byte[] encapsulation)
                || !TryDecodeBase64(message.Nonce, nameof(message.Nonce), message.MessageId, out byte[] nonce)
                || !TryDecodeBase64(message.Signature, nameof(message.Signature), message.MessageId, out byte[] signature))
            {
                return false;
            }

            // An ordinary message still carries its body, so the digest it claims is checked against the real
            // bytes; a vanishing one has none left, and the digest is all there is to verify against.
            if (message.Ciphertext.Length > 0)
            {
                if (!TryDecodeBase64(message.Ciphertext, nameof(message.Ciphertext), message.MessageId, out byte[] ciphertext)) return false;
                if (DigestOf(ciphertext) != message.CiphertextDigest) return false;
            }

            byte[] transcript = BuildTranscript(
                message.MessageId,
                message.ConversationId,
                message.SenderAddress,
                message.RecipientAddress,
                encapsulation,
                nonce,
                message.CiphertextDigest,
                message.CreatedAtUnixMs,
                message.Attachments,
                message.QuotedMessageId);

            return AppCryptography.Identities.Verify(transcript, signature, sender);
        }

        /// <summary> Reads one conversation from its beginning, the way a thread is read. </summary>
        /// <param name="conversationId"> Conversation to read, from <see cref="MessageData.ConversationIdFor"/>. </param>
        /// <param name="limit"> Largest number of messages to return. </param>
        /// <returns> That conversation's messages, oldest first, each still encrypted. </returns>
        public static async Task<IReadOnlyList<MessageData>> ReadConversationAsync(string conversationId, int limit = ConversationPageSize)
        {
            if (conversationId.Length == 0) return [];

            DocumentQuery<MessageData> query = new DocumentQuery<MessageData>()
                .WithMatch(MessageData.ConversationField, conversationId)
                .WithSort(MessageData.CreatedAtField)
                .WithLimit(limit);

            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary>
        /// Builds the inbox: the newest message of every conversation this account takes part in, newest
        /// conversation first. Sent and received messages are read separately, because a stored query matches one
        /// field at a time and a conversation is only visible from whichever side of it this account is on.
        /// </summary>
        /// <param name="address"> Account whose inbox to build. </param>
        /// <param name="limit"> Largest number of conversations to return. </param>
        /// <returns>
        /// One row per conversation, newest first; empty when the address is missing. Conversations older than the
        /// account's <see cref="InboxScanSize"/> most recent messages fall outside the window and are left out.
        /// </returns>
        public static async Task<IReadOnlyList<ConversationSummary>> ReadInboxAsync(string address, int limit = InboxPageSize)
        {
            if (address.Length == 0) return [];

            IReadOnlyList<MessageData> received = await ReadNewestAsync(MessageData.RecipientField, address);
            IReadOnlyList<MessageData> sent = await ReadNewestAsync(MessageData.SenderField, address);

            Dictionary<string, MessageData> newestPerConversation = [];

            foreach (MessageData message in received.Concat(sent))
            {
                if (newestPerConversation.TryGetValue(message.ConversationId, out MessageData? held)
                    && held.CreatedAtUnixMs >= message.CreatedAtUnixMs)
                {
                    continue;
                }

                newestPerConversation[message.ConversationId] = message;
            }

            return
            [
                .. newestPerConversation.Values
                    .OrderByDescending(message => message.CreatedAtUnixMs)
                    .Take(limit)
                    .Select(message => new ConversationSummary(
                        message.ConversationId,
                        message.SenderAddress == address ? message.RecipientAddress : message.SenderAddress,
                        message,
                        message.CreatedAtUnixMs))
            ];
        }

        /// <summary> Random bytes behind a message id — enough that two messages never collide. </summary>
        const int MessageIdBytes = 12;

        /// <summary>
        /// How many of an account's newest messages each side of the inbox reads before they are grouped into
        /// conversations. It bounds the work of building an inbox, at the cost of leaving out conversations whose
        /// last message is older than this many.
        /// </summary>
        const int InboxScanSize = 200;

        /// <summary> Reads the newest messages an account appears in on one side of the envelope. </summary>
        /// <param name="side"> Either <see cref="MessageData.SenderField"/> or <see cref="MessageData.RecipientField"/>. </param>
        /// <param name="address"> Account to match on that side. </param>
        /// <returns> At most <see cref="InboxScanSize"/> messages, newest first. </returns>
        static async Task<IReadOnlyList<MessageData>> ReadNewestAsync(DocumentField<MessageData> side, string address)
        {
            DocumentQuery<MessageData> query = new DocumentQuery<MessageData>()
                .WithMatch(side, address)
                .WithSort(MessageData.CreatedAtField, descending: true)
                .WithLimit(InboxScanSize);

            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary>
        /// Builds the data the cipher authenticates but leaves readable. Because the tag covers these fields, an
        /// envelope cannot be re-labelled: change the conversation, either participant or the timestamp and the
        /// ciphertext stops opening at all.
        /// </summary>
        /// <param name="conversationId"> Conversation the message belongs to. </param>
        /// <param name="senderAddress"> Address of the sender. </param>
        /// <param name="recipientAddress"> Address of the recipient. </param>
        /// <param name="createdAtUnixMs"> When the message was sent. </param>
        /// <returns> The associated data both sides authenticate. </returns>
        static byte[] BuildAssociatedData(string conversationId, string senderAddress, string recipientAddress, long createdAtUnixMs)
        {
            TranscriptWriter associatedData = new();
            associatedData.WriteBytes(MessageEnvelopeDomain);
            associatedData.WriteText(conversationId);
            associatedData.WriteText(senderAddress);
            associatedData.WriteText(recipientAddress);
            associatedData.WriteInt64(createdAtUnixMs);
            return associatedData.ToArray();
        }

        /// <summary> Builds the exact bytes a sender signs and a reader verifies: the whole envelope, ciphertext included. </summary>
        /// <param name="messageId"> The message's id. </param>
        /// <param name="conversationId"> Conversation the message belongs to. </param>
        /// <param name="senderAddress"> Address of the sender. </param>
        /// <param name="recipientAddress"> Address of the recipient. </param>
        /// <param name="encapsulation"> Value the recipient decapsulates to recover the secret. </param>
        /// <param name="nonce"> Nonce this one message was encrypted under. </param>
        /// <param name="ciphertextDigest"> Base64 SHA-256 of the encrypted body. </param>
        /// <param name="createdAtUnixMs"> When the message was sent. </param>
        /// <param name="attachments"> Media sent with the message, covered by the signature so nobody can swap a picture under it. </param>
        /// <param name="quotedMessageId"> Message this one replies to; covered too, so a reply cannot be re-pointed. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTranscript(
            string messageId,
            string conversationId,
            string senderAddress,
            string recipientAddress,
            byte[] encapsulation,
            byte[] nonce,
            string ciphertextDigest,
            long createdAtUnixMs,
            IReadOnlyList<MediaAttachment> attachments,
            string quotedMessageId)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(MessageSignatureDomain);
            transcript.WriteText(messageId);
            transcript.WriteText(conversationId);
            transcript.WriteText(senderAddress);
            transcript.WriteText(recipientAddress);
            transcript.WriteBytes(encapsulation);
            transcript.WriteBytes(nonce);
            transcript.WriteText(ciphertextDigest);
            transcript.WriteInt64(createdAtUnixMs);

            transcript.WriteInt64(attachments.Count);
            foreach (MediaAttachment attachment in attachments)
            {
                transcript.WriteText(attachment.BlobId);
                transcript.WriteText(attachment.ContentType);
                transcript.WriteText(attachment.Key);
                transcript.WriteText(attachment.Nonce);
                transcript.WriteInt64(attachment.ByteCount);
            }

            transcript.WriteText(quotedMessageId);
            return transcript.ToArray();
        }

        /// <summary>
        /// Summarises a ciphertext into the value the signature covers. The signature names the body by its digest
        /// rather than carrying it, which is what lets a vanishing message stay verifiable after its body is gone.
        /// </summary>
        /// <param name="ciphertext"> The encrypted body with its authentication tag. </param>
        /// <returns> Base64 SHA-256 of those bytes. </returns>
        static string DigestOf(byte[] ciphertext)
            => Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(ciphertext));

        /// <summary>
        /// Rebuilds the published half of an identity out of a profile, the way a reader verifies a post's author.
        /// </summary>
        /// <param name="profile"> Profile carrying the two base64 public keys. </param>
        /// <param name="identity"> Receives the rebuilt identity, or null when a key was not valid base64. </param>
        /// <returns> True when both keys decoded. </returns>
        static bool TryReadPublishedKeys(ProfileData profile, [NotNullWhen(true)] out PublicIdentity? identity)
        {
            identity = null;

            if (!TryDecodeBase64(profile.SigningPublicKey, nameof(profile.SigningPublicKey), profile.Address, out byte[] signingPublicKey)
                || !TryDecodeBase64(profile.EncryptionPublicKey, nameof(profile.EncryptionPublicKey), profile.Address, out byte[] encryptionPublicKey))
            {
                return false;
            }

            identity = new PublicIdentity(profile.Address, signingPublicKey, encryptionPublicKey);
            return true;
        }

        /// <summary>
        /// Checks that an account's address really commits to the encryption key published beside it. Without this
        /// the sender would encrypt to whatever key the profile happened to carry, and a server that swapped in its
        /// own key would be handed the message in the clear.
        /// </summary>
        /// <param name="recipient"> The account being written to. </param>
        /// <returns> True when the key is the right size and the address hashes to this pair of keys. </returns>
        static bool CanEncryptTo(PublicIdentity recipient)
            => recipient.EncryptionPublicKey.Length == AppCryptography.KeyExchange.PublicKeySize
               && AppCryptography.Addresses.Matches(recipient.Address, recipient.SigningPublicKey, recipient.EncryptionPublicKey);

        /// <summary> Decodes one stored base64 field, treating malformed text as a refusal rather than a crash. </summary>
        /// <param name="encoded"> The stored text. </param>
        /// <param name="fieldName"> Name of the field, quoted in the log line. </param>
        /// <param name="ownerId"> Message id or address the field belongs to, quoted in the log line. </param>
        /// <param name="decoded"> Receives the decoded bytes, or an empty array when the text was malformed. </param>
        /// <returns> True when the text decoded. </returns>
        static bool TryDecodeBase64(string encoded, string fieldName, string ownerId, out byte[] decoded)
        {
            try
            {
                decoded = Convert.FromBase64String(encoded);
                return true;
            }
            catch (FormatException error)
            {
                Log($"'{ownerId}' carries malformed base64 in {fieldName}.\n{error}", LogLevel.Warning);
                decoded = [];
                return false;
            }
        }
    }
}
