using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// One direct message as it is stored. Every field here is either public routing information or ciphertext:
    /// there is deliberately no plaintext property, so a server holding the whole collection still cannot read a
    /// single word. Only the recipient's device can turn <see cref="Ciphertext"/> back into text, using
    /// <see cref="Encapsulation"/> and its own seed.
    /// </summary>
    public sealed record MessageData : IStoredDocument<MessageData>
    {
        public static string CollectionName => "messages";

        /// <summary> Id this message is stored under. </summary>
        public required string MessageId { get; init; }

        /// <summary> Conversation this message belongs to, the same value for both participants. </summary>
        public required string ConversationId { get; init; }

        /// <summary> Address of the account that sent it. </summary>
        public required string SenderAddress { get; init; }

        /// <summary> Address of the account it was sent to. </summary>
        public required string RecipientAddress { get; init; }

        /// <summary> Base64 key encapsulation the recipient feeds back into their own key to recover the shared secret. </summary>
        public required string Encapsulation { get; init; }

        /// <summary> Base64 nonce used for this one message. </summary>
        public required string Nonce { get; init; }

        /// <summary> Base64 ciphertext with its authentication tag. The message body, unreadable without the recipient's seed. </summary>
        public required string Ciphertext { get; init; }

        /// <summary> When the sender sent it. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Base64 signature over the whole envelope, proving which account sent it. </summary>
        public required string Signature { get; init; }

        /// <summary> Longest message accepted before encryption. </summary>
        public const int MaximumTextLength = 2000;

        /// <summary> Character placed between the two addresses in a conversation id. </summary>
        const char ConversationSeparator = '|';

        /// <summary> Id this message is stored under. </summary>
        public DocumentId<MessageData> Id => new(MessageId);

        /// <summary>
        /// Builds the conversation id two accounts share. The addresses are ordered before joining, so both
        /// participants compute the same id no matter which of them is sending.
        /// </summary>
        /// <param name="addressA"> One participant's address. </param>
        /// <param name="addressB"> The other participant's address. </param>
        /// <returns> The conversation id both sides will use. </returns>
        public static string ConversationIdFor(string addressA, string addressB)
            => string.CompareOrdinal(addressA, addressB) <= 0
                ? addressA + ConversationSeparator + addressB
                : addressB + ConversationSeparator + addressA;

        /// <summary> Conversation id, for reading one thread. </summary>
        public static readonly DocumentField<MessageData> ConversationField = new(nameof(ConversationId), message => message.ConversationId);

        /// <summary> Recipient address, for finding messages sent to an account. </summary>
        public static readonly DocumentField<MessageData> RecipientField = new(nameof(RecipientAddress), message => message.RecipientAddress);

        /// <summary> Sender address, for finding messages sent by an account. </summary>
        public static readonly DocumentField<MessageData> SenderField = new(nameof(SenderAddress), message => message.SenderAddress);

        /// <summary> Creation time, for ordering a thread. </summary>
        public static readonly DocumentField<MessageData> CreatedAtField = new(nameof(CreatedAtUnixMs), message => message.CreatedAtUnixMs);
    }
}
