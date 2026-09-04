using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Founding groups, joining them, and reading who is in one. A group is its own keypair rather than a row with
    /// a name: its address falls out of its keys exactly as an account's does, so a group cannot be impersonated by
    /// anybody who does not hold what it was derived from.
    /// </summary>
    /// <remarks>
    /// Those keys are derived from the founder's own seed and a public salt kept on the group. The founder can
    /// therefore rebuild them wherever they can open their own account, and nobody else can rebuild them at all —
    /// which is what a closed group's posts will be sealed to when they arrive.
    /// </remarks>
    public static class GroupService
    {
        /// <summary> Separates a group's founding signature from every other signature the app produces. </summary>
        static readonly byte[] GroupSignatureDomain = "ChaySocial/Group/v1"u8.ToArray();

        /// <summary> Separates the derivation of a group's keys from every other use of a seed. </summary>
        static readonly byte[] GroupSeedContext = "ChaySocial/GroupSeed/v1"u8.ToArray();

        /// <summary> Groups fetched in one page of a listing. </summary>
        public const int GroupPageSize = 30;

        /// <summary> Members read back for one group. </summary>
        public const int MemberPageSize = 200;

        /// <summary> Random bytes behind a group's seed salt. </summary>
        const int SeedSaltBytes = 16;

        /// <summary>
        /// Founds a group. The founder's seed and a fresh public salt derive the group's own keys, its address
        /// falls out of those keys, and the founder signs the whole thing before it is stored.
        /// </summary>
        /// <param name="founder"> The unlocked account founding it. </param>
        /// <param name="name"> What to call it; trimmed, and refused when empty or over <see cref="GroupData.MaximumNameLength"/>. </param>
        /// <param name="description"> A line saying what it is for; trimmed, and refused when over <see cref="GroupData.MaximumDescriptionLength"/>. </param>
        /// <param name="avatar"> Emoji standing in for its picture; blank falls back to the default. </param>
        /// <param name="isOpen"> True when anybody may join without being let in. </param>
        /// <returns> The stored group, or null when the details were not usable. </returns>
        public static async Task<GroupData?> FoundAsync(
            PrivateIdentity founder,
            string name,
            string description = "",
            string avatar = "",
            bool isOpen = true)
        {
            string trimmedName = name.Trim();
            string trimmedDescription = description.Trim();

            if (trimmedName.Length == 0 || trimmedName.Length > GroupData.MaximumNameLength) return null;
            if (trimmedDescription.Length > GroupData.MaximumDescriptionLength) return null;

            byte[] salt = RandomSource.Next(SeedSaltBytes);
            PublicIdentity groupIdentity = DeriveGroupIdentity(founder, salt);

            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string chosenAvatar = string.IsNullOrWhiteSpace(avatar) ? GroupData.DefaultAvatar : avatar.Trim();

            byte[] transcript = BuildTranscript(
                groupIdentity.Address, trimmedName, trimmedDescription, chosenAvatar,
                founder.Public.Address, isOpen, createdAt);

            GroupData group = new()
            {
                Address = groupIdentity.Address,
                SigningPublicKey = Convert.ToBase64String(groupIdentity.SigningPublicKey),
                EncryptionPublicKey = Convert.ToBase64String(groupIdentity.EncryptionPublicKey),
                Name = trimmedName,
                Description = trimmedDescription,
                Avatar = chosenAvatar,
                FounderAddress = founder.Public.Address,
                SeedSalt = Convert.ToBase64String(salt),
                IsOpen = isOpen,
                CreatedAtUnixMs = createdAt,
                Signature = Convert.ToBase64String(founder.Sign(transcript))
            };

            await AppServices.Documents.WriteAsync(group.Id, group);

            // A founder who is not in their own group would have to join it, which reads as a mistake rather than
            // as a choice, so the first membership is written with the group itself.
            await AppServices.Documents.WriteAsync(
                GroupMemberData.IdFor(group.Address, founder.Public.Address),
                new GroupMemberData
                {
                    GroupAddress = group.Address,
                    MemberAddress = founder.Public.Address,
                    CreatedAtUnixMs = createdAt
                });

            MainEvents.Trigger(MainEvents.Names.GroupsChanged, group.Address);
            return group;
        }

        /// <summary> Reads one group by its address. </summary>
        /// <param name="address"> The group's address. </param>
        /// <returns> The group, or null when nothing is stored under it. </returns>
        public static Task<GroupData?> ReadAsync(string address)
            => address.Length == 0 ? Task.FromResult<GroupData?>(null) : AppServices.Documents.ReadAsync(new DocumentId<GroupData>(address));

        /// <summary> Reads the newest groups, for somebody looking for one to join. </summary>
        /// <param name="limit"> Largest number of groups to return. </param>
        /// <returns> Groups, newest first. </returns>
        public static async Task<IReadOnlyList<GroupData>> ReadRecentAsync(int limit = GroupPageSize)
        {
            DocumentQuery<GroupData> query = new DocumentQuery<GroupData>()
                .WithSort(GroupData.CreatedAtField, descending: true)
                .WithLimit(limit);

            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary> Joins a group, or does nothing when the account is already in it or the group is not open. </summary>
        /// <param name="group"> Group to join. </param>
        /// <param name="member"> Account joining it. </param>
        /// <returns> True when the account ended up a member. </returns>
        public static async Task<bool> JoinAsync(GroupData group, PublicIdentity member)
        {
            DocumentId<GroupMemberData> id = GroupMemberData.IdFor(group.Address, member.Address);

            if (await AppServices.Documents.ReadAsync(id) is not null) return true;

            // A closed group is joined by being let in, which is the founder's decision rather than the joiner's.
            if (!group.IsOpen && group.FounderAddress != member.Address) return false;

            await AppServices.Documents.WriteAsync(id, new GroupMemberData
            {
                GroupAddress = group.Address,
                MemberAddress = member.Address,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            MainEvents.Trigger(MainEvents.Names.GroupsChanged, group.Address);
            return true;
        }

        /// <summary>
        /// Leaves a group. The founder cannot: a group with nobody who can change it is a group nobody can look
        /// after, and leaving would be a quieter way of abandoning it than deleting it.
        /// </summary>
        /// <param name="group"> Group to leave. </param>
        /// <param name="member"> Account leaving it. </param>
        /// <returns> True when the account is no longer a member. </returns>
        public static async Task<bool> LeaveAsync(GroupData group, PublicIdentity member)
        {
            if (group.FounderAddress == member.Address) return false;

            await AppServices.Documents.DeleteAsync(GroupMemberData.IdFor(group.Address, member.Address));
            MainEvents.Trigger(MainEvents.Names.GroupsChanged, group.Address);
            return true;
        }

        /// <summary> True when one account is in one group. </summary>
        /// <param name="groupAddress"> The group. </param>
        /// <param name="memberAddress"> The account. </param>
        /// <returns> True when a membership is stored for the pair. </returns>
        public static async Task<bool> IsMemberAsync(string groupAddress, string memberAddress)
        {
            if (groupAddress.Length == 0 || memberAddress.Length == 0) return false;

            return await AppServices.Documents.ReadAsync(GroupMemberData.IdFor(groupAddress, memberAddress)) is not null;
        }

        /// <summary> Reads who is in a group, in the order they joined. </summary>
        /// <param name="groupAddress"> The group. </param>
        /// <param name="limit"> Largest number of members to return. </param>
        /// <returns> The members' addresses, oldest first. </returns>
        public static async Task<IReadOnlyList<string>> ReadMembersAsync(string groupAddress, int limit = MemberPageSize)
        {
            DocumentQuery<GroupMemberData> query = new DocumentQuery<GroupMemberData>()
                .WithMatch(GroupMemberData.GroupField, groupAddress)
                .WithSort(GroupMemberData.CreatedAtField)
                .WithLimit(limit);

            return [.. (await AppServices.Documents.QueryAsync(query)).Documents.Select(member => member.MemberAddress)];
        }

        /// <summary> Reads the groups one account belongs to, newest membership first. </summary>
        /// <param name="memberAddress"> The account. </param>
        /// <param name="limit"> Largest number of groups to return. </param>
        /// <returns> The groups themselves, with any that have since been deleted left out. </returns>
        public static async Task<IReadOnlyList<GroupData>> ReadGroupsOfAsync(string memberAddress, int limit = GroupPageSize)
        {
            if (memberAddress.Length == 0 || limit <= 0) return [];

            DocumentQuery<GroupMemberData> query = new DocumentQuery<GroupMemberData>()
                .WithMatch(GroupMemberData.MemberField, memberAddress)
                .WithSort(GroupMemberData.CreatedAtField, descending: true)
                .WithLimit(limit);

            IReadOnlyList<GroupMemberData> memberships = (await AppServices.Documents.QueryAsync(query)).Documents;
            if (memberships.Count == 0) return [];

            GroupData?[] groups = await Task.WhenAll(memberships.Select(membership => ReadAsync(membership.GroupAddress)));

            return [.. groups.Where(group => group is not null).Select(group => group!)];
        }

        /// <summary>
        /// Checks that a group really was founded by the account it names, and that its address really belongs to
        /// the keys it publishes. Either failing means the group was altered or invented after it left its founder.
        /// </summary>
        /// <param name="group"> Group to check. </param>
        /// <param name="founderProfile"> Profile of the account it names, or null when it could not be read. </param>
        /// <returns> True when both hold. </returns>
        public static bool VerifyFounder(GroupData group, ProfileData? founderProfile)
        {
            if (founderProfile is null || founderProfile.Address != group.FounderAddress) return false;

            try
            {
                byte[] signingKey = Convert.FromBase64String(group.SigningPublicKey);
                byte[] encryptionKey = Convert.FromBase64String(group.EncryptionPublicKey);

                // An address that does not belong to the published keys would let a group borrow another's name.
                if (!AppCryptography.Addresses.Matches(group.Address, signingKey, encryptionKey)) return false;

                PublicIdentity founder = new(
                    founderProfile.Address,
                    Convert.FromBase64String(founderProfile.SigningPublicKey),
                    Convert.FromBase64String(founderProfile.EncryptionPublicKey));

                byte[] transcript = BuildTranscript(
                    group.Address, group.Name, group.Description, group.Avatar,
                    group.FounderAddress, group.IsOpen, group.CreatedAtUnixMs);

                return AppCryptography.Identities.Verify(transcript, Convert.FromBase64String(group.Signature), founder);
            }
            catch (FormatException error)
            {
                Log($"Group '{group.Address}' carries malformed base64.\n{error}", LogLevel.Warning);
                return false;
            }
        }

        /// <summary>
        /// Rebuilds a group's own keys from the founder's seed and the group's public salt. Only the founder can do
        /// this, which is what makes the group's keys theirs to hold rather than the server's to hand out.
        /// </summary>
        /// <param name="founder"> The unlocked founding account. </param>
        /// <param name="group"> The group whose keys to rebuild. </param>
        /// <returns> The group's unlocked identity, or null when this account did not found it or the salt is malformed. </returns>
        public static PrivateIdentity? OpenAsFounder(PrivateIdentity founder, GroupData group)
        {
            if (group.FounderAddress != founder.Public.Address) return null;

            try
            {
                return OpenGroup(founder, Convert.FromBase64String(group.SeedSalt));
            }
            catch (FormatException error)
            {
                Log($"Group '{group.Address}' carries a malformed seed salt.\n{error}", LogLevel.Warning);
                return null;
            }
        }

        /// <summary> Derives the publishable half of a group's identity. </summary>
        /// <param name="founder"> The founding account. </param>
        /// <param name="salt"> The group's public salt. </param>
        /// <returns> The group's address and public keys. </returns>
        static PublicIdentity DeriveGroupIdentity(PrivateIdentity founder, ReadOnlySpan<byte> salt)
            => OpenGroup(founder, salt).Public;

        /// <summary> Derives a group's whole identity from the founder's seed and the group's salt. </summary>
        /// <param name="founder"> The founding account. </param>
        /// <param name="salt"> The group's public salt. </param>
        /// <returns> The group's unlocked identity. </returns>
        static PrivateIdentity OpenGroup(PrivateIdentity founder, ReadOnlySpan<byte> salt)
        {
            byte[] founderSeed = founder.ExportMasterSeed();

            byte[] groupSeed = AppCryptography.SeedExpander.Derive(
                founderSeed, salt, GroupSeedContext, IdentityScheme.MasterSeedSize);

            return AppCryptography.Identities.Open(groupSeed);
        }

        /// <summary> Builds the exact bytes a founder signs and a reader verifies. </summary>
        /// <param name="address"> The group's address. </param>
        /// <param name="name"> What it is called. </param>
        /// <param name="description"> What it is for. </param>
        /// <param name="avatar"> Its emoji. </param>
        /// <param name="founderAddress"> Address of the founding account. </param>
        /// <param name="isOpen"> Whether anybody may join. </param>
        /// <param name="createdAtUnixMs"> When it was founded. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTranscript(
            string address,
            string name,
            string description,
            string avatar,
            string founderAddress,
            bool isOpen,
            long createdAtUnixMs)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(GroupSignatureDomain);
            transcript.WriteText(address);
            transcript.WriteText(name);
            transcript.WriteText(description);
            transcript.WriteText(avatar);
            transcript.WriteText(founderAddress);
            transcript.WriteInt64(isOpen ? 1 : 0);
            transcript.WriteInt64(createdAtUnixMs);
            return transcript.ToArray();
        }
    }
}
