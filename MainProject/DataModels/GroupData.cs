using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// A place a set of people share. Like an account, a group is its own keypair: its address is derived from that
    /// pair, so nobody can claim to be a group they do not hold the seed for, and the founder signs the group into
    /// existence with it.
    /// </summary>
    public sealed record GroupData : IStoredDocument<GroupData>
    {
        public static string CollectionName => "groups";

        /// <summary> The group's own address, derived from its keys exactly as an account's is. </summary>
        public required string Address { get; init; }

        /// <summary> Base64 signing key the group publishes, so anything signed as the group can be checked. </summary>
        public required string SigningPublicKey { get; init; }

        /// <summary> Base64 encryption key the group publishes; a closed group's posts are sealed to it. </summary>
        public required string EncryptionPublicKey { get; init; }

        /// <summary> What the group is called. </summary>
        public required string Name { get; init; }

        /// <summary> A line saying what the group is for. </summary>
        public string Description { get; init; } = string.Empty;

        /// <summary> Emoji standing in for the group's picture. </summary>
        public string Avatar { get; init; } = DefaultAvatar;

        /// <summary> Address of the account that founded it, and the only one that can change it. </summary>
        public required string FounderAddress { get; init; }

        /// <summary>
        /// Base64 salt the group's own keys were derived from, alongside the founder's seed. It is public and
        /// useless on its own: without the founder's seed it derives nothing. Keeping it here is what lets the
        /// founder rebuild the group's keys on any device they can already open their own account on, instead of
        /// the group dying with one browser's storage.
        /// </summary>
        public required string SeedSalt { get; init; }

        /// <summary> True when anybody may join without being let in. </summary>
        public bool IsOpen { get; init; } = true;

        /// <summary> When it was founded. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Base64 signature over the group's own fields, produced by the founder's signing key. </summary>
        public required string Signature { get; init; }

        /// <summary> Emoji a group carries until its founder picks another. </summary>
        public const string DefaultAvatar = "🫂";

        /// <summary> Longest name accepted. </summary>
        public const int MaximumNameLength = 60;

        /// <summary> Longest description accepted. </summary>
        public const int MaximumDescriptionLength = 300;

        /// <summary> Id this group is stored under, which is its address. </summary>
        public DocumentId<GroupData> Id => new(Address);

        /// <summary> Founder address, for reading the groups one account started. </summary>
        public static readonly DocumentField<GroupData> FounderField = new(nameof(FounderAddress), group => group.FounderAddress);

        /// <summary> Founding time, for listing groups newest first. </summary>
        public static readonly DocumentField<GroupData> CreatedAtField = new(nameof(CreatedAtUnixMs), group => group.CreatedAtUnixMs);
    }

    /// <summary>
    /// One account's membership of one group. Keyed by group and member, so joining twice is the same document and
    /// leaving is a delete.
    /// </summary>
    public sealed record GroupMemberData : IStoredDocument<GroupMemberData>
    {
        public static string CollectionName => "groupmembers";

        /// <summary> Group being joined. </summary>
        public required string GroupAddress { get; init; }

        /// <summary> Account that joined it. </summary>
        public required string MemberAddress { get; init; }

        /// <summary> When they joined. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Id this membership is stored under. </summary>
        public DocumentId<GroupMemberData> Id => IdFor(GroupAddress, MemberAddress);

        /// <summary> Builds the id one account's membership of one group is stored under. </summary>
        /// <param name="groupAddress"> The group. </param>
        /// <param name="memberAddress"> The member. </param>
        /// <returns> The document id. </returns>
        public static DocumentId<GroupMemberData> IdFor(string groupAddress, string memberAddress)
            => new($"{groupAddress}:{memberAddress}");

        /// <summary> Group address, for reading who is in a group. </summary>
        public static readonly DocumentField<GroupMemberData> GroupField = new(nameof(GroupAddress), member => member.GroupAddress);

        /// <summary> Member address, for reading which groups one account is in. </summary>
        public static readonly DocumentField<GroupMemberData> MemberField = new(nameof(MemberAddress), member => member.MemberAddress);

        /// <summary> Joining time, for listing a group's members oldest first. </summary>
        public static readonly DocumentField<GroupMemberData> CreatedAtField = new(nameof(CreatedAtUnixMs), member => member.CreatedAtUnixMs);
    }
}
