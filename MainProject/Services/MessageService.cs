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
        public static async Task<MessageData?> SendAsync(PrivateIdentity sender, ProfileData recipientProfile, string text)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 0 || trimmed.Length > MessageData.MaximumTextLength) return null;

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

            byte[] transcript = BuildTranscript(
                messageId,
                conversationId,
                senderAddress,
                recipientAddress,
                secret.Encapsulation,
                nonce,
                ciphertext,
                createdAt);

            MessageData message = new()
            {
                MessageId = messageId,
                ConversationId = conversationId,
                SenderAddress = senderAddress,
                RecipientAddress = recipientAddress,
                Encapsulation = Convert.ToBase64String(secret.Encapsulation),
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(ciphertext),
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
                || !TryDecodeBase64(message.Ciphertext, nameof(message.Ciphertext), message.MessageId, out byte[] ciphertext)
                || !TryDecodeBase64(message.Signature, nameof(message.Signature), message.MessageId, out byte[] signature))
            {
                return false;
            }

            byte[] transcript = BuildTranscript(
                message.MessageId,
                message.ConversationId,
                message.SenderAddress,
                message.RecipientAddress,
                encapsulation,
                nonce,
                ciphertext,
                message.CreatedAtUnixMs);

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
        /// <param name="ciphertext"> The encrypted body with its authentication tag. </param>
        /// <param name="createdAtUnixMs"> When the message was sent. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTranscript(
            string messageId,
            string conversationId,
            string senderAddress,
            string recipientAddress,
            byte[] encapsulation,
            byte[] nonce,
            byte[] ciphertext,
            long createdAtUnixMs)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(MessageSignatureDomain);
            transcript.WriteText(messageId);
            transcript.WriteText(conversationId);
            transcript.WriteText(senderAddress);
            transcript.WriteText(recipientAddress);
            transcript.WriteBytes(encapsulation);
            transcript.WriteBytes(nonce);
            transcript.WriteBytes(ciphertext);
            transcript.WriteInt64(createdAtUnixMs);
            return transcript.ToArray();
        }

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
