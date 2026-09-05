using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// Somebody standing in the queue waiting to be paired with a stranger. It says who is waiting and since when,
    /// and nothing else: a ticket is not a conversation and never carries a word of one.
    /// </summary>
    public sealed record MatchTicketData : IStoredDocument<MatchTicketData>
    {
        public static string CollectionName => "matchtickets";

        /// <summary> Address of the account waiting. One account waits once, so the ticket is stored under it. </summary>
        public required string Address { get; init; }

        /// <summary> When they joined the queue, which is what decides who has been waiting longest. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Id this ticket is stored under, which is the waiting account's address. </summary>
        public DocumentId<MatchTicketData> Id => new(Address);

        /// <summary> Time joined, for finding whoever has been waiting longest. </summary>
        public static readonly DocumentField<MatchTicketData> CreatedAtField = new(nameof(CreatedAtUnixMs), ticket => ticket.CreatedAtUnixMs);
    }

    /// <summary>
    /// A room two strangers were paired into. Its id is derived from the two addresses rather than drawn at random,
    /// so both sides compute the same one without having to agree on it first — which is what lets a store with no
    /// transactions pair people without either side racing the other.
    /// </summary>
    public sealed record MatchRoomData : IStoredDocument<MatchRoomData>
    {
        public static string CollectionName => "matchrooms";

        /// <summary> The room's id, derived from its two participants. </summary>
        public required string RoomId { get; init; }

        /// <summary> The participant whose address sorts first; naming them this way keeps the record itself order-free. </summary>
        public required string FirstAddress { get; init; }

        /// <summary> The other participant. </summary>
        public required string SecondAddress { get; init; }

        /// <summary> When the pairing happened. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Address of whoever walked out, or empty while both are still in the room. </summary>
        public string ClosedByAddress { get; init; } = string.Empty;

        /// <summary> True once somebody has left, which is what ends the room for both sides. </summary>
        public bool IsClosed => ClosedByAddress.Length > 0;

        /// <summary> Id this room is stored under. </summary>
        public DocumentId<MatchRoomData> Id => new(RoomId);

        /// <summary>
        /// Builds the room id two accounts share. The addresses are ordered before joining, so both sides compute
        /// the same id no matter which of them got there first.
        /// </summary>
        /// <param name="addressA"> One participant's address. </param>
        /// <param name="addressB"> The other participant's address. </param>
        /// <returns> The room id both sides will use. </returns>
        public static string RoomIdFor(string addressA, string addressB)
            => string.CompareOrdinal(addressA, addressB) <= 0
                ? $"{addressA}{RoomSeparator}{addressB}"
                : $"{addressB}{RoomSeparator}{addressA}";

        /// <summary>
        /// The conversation a room's messages live under. It is deliberately not the conversation two accounts
        /// would share as ordinary correspondents: a chat with a stranger must not turn up in either postbox, and
        /// it is thrown away whole when the room ends.
        /// </summary>
        /// <param name="roomId"> The room. </param>
        /// <returns> The conversation id its messages carry. </returns>
        public static string ConversationIdFor(string roomId) => ConversationPrefix + roomId;

        /// <summary> True when a conversation belongs to a stranger-chat room rather than to somebody's postbox. </summary>
        /// <param name="conversationId"> The conversation to judge. </param>
        /// <returns> True when it is a room's. </returns>
        public static bool IsRoomConversation(string conversationId)
            => conversationId.StartsWith(ConversationPrefix, StringComparison.Ordinal);

        /// <summary> Marks a conversation as belonging to a room, so a postbox can leave it out. </summary>
        const string ConversationPrefix = "match:";

        /// <summary> Character between the two addresses in a room id. </summary>
        const char RoomSeparator = '~';

        /// <summary> First participant, for finding the room one account is in. </summary>
        public static readonly DocumentField<MatchRoomData> FirstField = new(nameof(FirstAddress), room => room.FirstAddress);

        /// <summary> Second participant, for the same reason from the other side. </summary>
        public static readonly DocumentField<MatchRoomData> SecondField = new(nameof(SecondAddress), room => room.SecondAddress);

        /// <summary> Pairing time, for reading the newest room first. </summary>
        public static readonly DocumentField<MatchRoomData> CreatedAtField = new(nameof(CreatedAtUnixMs), room => room.CreatedAtUnixMs);
    }
}
