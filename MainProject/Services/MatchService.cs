using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Pairing two strangers into a temporary room. The store has no transactions, so nothing here relies on one:
    /// a room's id is derived from its two participants, which means both sides compute the same id independently
    /// and racing each other produces the same room rather than two.
    /// </summary>
    /// <remarks>
    /// What is said in a room is an ordinary end-to-end encrypted message filed under the room's own conversation
    /// rather than the one those two accounts would share as correspondents. That is what keeps a chat with a
    /// stranger out of both postboxes, and what lets the whole thing be thrown away when somebody walks out.
    /// </remarks>
    public static class MatchService
    {
        /// <summary> Tickets read while looking for somebody to pair with. </summary>
        const int WaitingTicketsRead = 50;

        /// <summary> Rooms read while looking for the one this account is in. </summary>
        const int RoomsReadPerSide = 10;

        /// <summary> Messages cleared out when a room ends. </summary>
        const int RoomMessagesCleared = 500;

        /// <summary>
        /// Joins the queue, or hands back the room this account is already in. Called once when somebody asks to
        /// meet a stranger, and again on every poll while they wait.
        /// </summary>
        /// <param name="account"> The account waiting. </param>
        /// <returns> The room once there is one, or null while still waiting. </returns>
        public static async Task<MatchRoomData?> JoinOrPollAsync(PublicIdentity account)
        {
            // Somebody may have paired with us since the last poll, in which case the room already exists and
            // there is nothing to queue for.
            if (await ReadOpenRoomAsync(account.Address) is MatchRoomData existing) return existing;

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await AppServices.Documents.WriteAsync(
                new DocumentId<MatchTicketData>(account.Address),
                new MatchTicketData { Address = account.Address, CreatedAtUnixMs = now });

            // Whoever has been waiting longest gets paired first, so the queue is a queue rather than a lottery.
            DocumentQuery<MatchTicketData> query = new DocumentQuery<MatchTicketData>()
                .WithSort(MatchTicketData.CreatedAtField)
                .WithLimit(WaitingTicketsRead);

            IReadOnlyList<MatchTicketData> waiting = (await AppServices.Documents.QueryAsync(query)).Documents;

            MatchTicketData? partner = waiting.FirstOrDefault(ticket => ticket.Address != account.Address);
            if (partner is null) return null;

            return await OpenRoomAsync(account.Address, partner.Address, now);
        }

        /// <summary> Steps out of the queue without pairing. </summary>
        /// <param name="account"> The account giving up. </param>
        /// <returns> A task that completes once the ticket is gone. </returns>
        public static async Task LeaveQueueAsync(PublicIdentity account)
        {
            await AppServices.Documents.DeleteAsync(new DocumentId<MatchTicketData>(account.Address));
            MainEvents.Trigger(MainEvents.Names.MatchChanged, account.Address);
        }

        /// <summary> Reads the room one account is in, if any is still open. </summary>
        /// <param name="address"> The account. </param>
        /// <returns> Their open room, or null when they are in none. </returns>
        public static async Task<MatchRoomData?> ReadOpenRoomAsync(string address)
        {
            if (address.Length == 0) return null;

            // A room names its participants in a fixed order, so being in one means matching either side.
            Task<DocumentPage<MatchRoomData>> asFirst = AppServices.Documents.QueryAsync(
                new DocumentQuery<MatchRoomData>()
                    .WithMatch(MatchRoomData.FirstField, address)
                    .WithSort(MatchRoomData.CreatedAtField, descending: true)
                    .WithLimit(RoomsReadPerSide));

            Task<DocumentPage<MatchRoomData>> asSecond = AppServices.Documents.QueryAsync(
                new DocumentQuery<MatchRoomData>()
                    .WithMatch(MatchRoomData.SecondField, address)
                    .WithSort(MatchRoomData.CreatedAtField, descending: true)
                    .WithLimit(RoomsReadPerSide));

            await Task.WhenAll(asFirst, asSecond);

            return (await asFirst).Documents
                .Concat((await asSecond).Documents)
                .Where(room => !room.IsClosed)
                .OrderByDescending(room => room.CreatedAtUnixMs)
                .FirstOrDefault();
        }

        /// <summary> Reads one room by id, for a page checking whether the other side has walked out. </summary>
        /// <param name="roomId"> The room. </param>
        /// <returns> The room, or null when it has been cleared away. </returns>
        public static Task<MatchRoomData?> ReadRoomAsync(string roomId)
            => roomId.Length == 0
                ? Task.FromResult<MatchRoomData?>(null)
                : AppServices.Documents.ReadAsync(new DocumentId<MatchRoomData>(roomId));

        /// <summary> The other person in a room. </summary>
        /// <param name="room"> The room. </param>
        /// <param name="address"> The account asking. </param>
        /// <returns> The other participant's address, or empty when this account is not in the room. </returns>
        public static string PartnerOf(MatchRoomData room, string address)
        {
            if (room.FirstAddress == address) return room.SecondAddress;
            if (room.SecondAddress == address) return room.FirstAddress;

            return string.Empty;
        }

        /// <summary> Reads what has been said in a room, oldest first. </summary>
        /// <param name="room"> The room. </param>
        /// <returns> Its messages, still encrypted. </returns>
        public static Task<IReadOnlyList<MessageData>> ReadMessagesAsync(MatchRoomData room)
            => MessageService.ReadConversationAsync(MatchRoomData.ConversationIdFor(room.RoomId));

        /// <summary> Says something in a room. </summary>
        /// <param name="sender"> The unlocked account speaking. </param>
        /// <param name="room"> The room. </param>
        /// <param name="partnerProfile"> Profile of the other participant, read for their published encryption key. </param>
        /// <param name="text"> What to say. </param>
        /// <returns> The stored message, or null when it was not sendable. </returns>
        public static Task<MessageData?> SendAsync(
            PrivateIdentity sender,
            MatchRoomData room,
            ProfileData partnerProfile,
            string text)
            => MessageService.SendAsync(
                sender,
                partnerProfile,
                text,
                conversationIdOverride: MatchRoomData.ConversationIdFor(room.RoomId));

        /// <summary>
        /// Walks out. The room is marked closed so the other side sees it end rather than being left talking to
        /// nobody, and everything said in it is deleted — which is the whole promise of a room with a stranger.
        /// </summary>
        /// <param name="room"> The room being left. </param>
        /// <param name="account"> The account leaving. </param>
        /// <returns> A task that completes once the room and its messages are gone. </returns>
        public static async Task LeaveRoomAsync(MatchRoomData room, PublicIdentity account)
        {
            if (PartnerOf(room, account.Address).Length == 0) return;

            // Marked closed before the words are cleared, so the other side never polls a room that still looks
            // open but has already been emptied under them.
            await AppServices.Documents.WriteAsync(room.Id, room with { ClosedByAddress = account.Address });

            IReadOnlyList<MessageData> said = await MessageService.ReadConversationAsync(
                MatchRoomData.ConversationIdFor(room.RoomId), RoomMessagesCleared);

            foreach (MessageData message in said)
            {
                await AppServices.Documents.DeleteAsync(message.Id);
            }

            MainEvents.Trigger(MainEvents.Names.MatchChanged, room.RoomId);
        }

        /// <summary>
        /// Clears a closed room away once both sides have seen it end. Called by whichever side is still standing
        /// there when it notices the room closed.
        /// </summary>
        /// <param name="room"> The closed room. </param>
        /// <returns> A task that completes once the record is gone. </returns>
        public static async Task ForgetRoomAsync(MatchRoomData room)
        {
            await AppServices.Documents.DeleteAsync(room.Id);
            MainEvents.Trigger(MainEvents.Names.MatchChanged, room.RoomId);
        }

        /// <summary>
        /// Writes the room both sides will find, and takes both tickets out of the queue. Writing the same room
        /// twice is harmless: its id and its contents are derived from the pair, so two racing clients write
        /// identical records rather than two different rooms.
        /// </summary>
        /// <param name="addressA"> One participant. </param>
        /// <param name="addressB"> The other. </param>
        /// <param name="createdAtUnixMs"> When the pairing happened. </param>
        /// <returns> The room. </returns>
        static async Task<MatchRoomData> OpenRoomAsync(string addressA, string addressB, long createdAtUnixMs)
        {
            bool aFirst = string.CompareOrdinal(addressA, addressB) <= 0;

            MatchRoomData room = new()
            {
                RoomId = MatchRoomData.RoomIdFor(addressA, addressB),
                FirstAddress = aFirst ? addressA : addressB,
                SecondAddress = aFirst ? addressB : addressA,
                CreatedAtUnixMs = createdAtUnixMs
            };

            await AppServices.Documents.WriteAsync(room.Id, room);

            await AppServices.Documents.DeleteAsync(new DocumentId<MatchTicketData>(addressA));
            await AppServices.Documents.DeleteAsync(new DocumentId<MatchTicketData>(addressB));

            MainEvents.Trigger(MainEvents.Names.MatchChanged, room.RoomId);
            return room;
        }
    }
}
