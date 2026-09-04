using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// The public face of an account: what other people see next to its posts. Stored under the account's address,
    /// and carrying the public keys that address commits to, so anyone can verify a post this account signed without
    /// asking its owner for anything.
    /// </summary>
    public sealed record ProfileData : IStoredDocument<ProfileData>
    {
        public static string CollectionName => "profiles";

        /// <summary> The account's address; also the id this profile is stored under. </summary>
        public required string Address { get; init; }

        /// <summary> Name the owner chose. Not unique and not verified — the address is the identity, this is only a label. </summary>
        public required string DisplayName { get; init; }

        /// <summary> One emoji standing in for a picture. </summary>
        public string Avatar { get; init; } = DefaultAvatar;

        /// <summary> Short self-description. </summary>
        public string Bio { get; init; } = string.Empty;

        /// <summary> When the account first published its profile. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Base64 signing key, republished here so readers can verify posts without a second lookup. </summary>
        public required string SigningPublicKey { get; init; }

        /// <summary> Base64 encryption key, so anyone can encrypt to this account. </summary>
        public required string EncryptionPublicKey { get; init; }

        /// <summary> Emoji a profile starts with before its owner picks one. </summary>
        public const string DefaultAvatar = "🫖";

        /// <summary> Longest display name accepted, so one account cannot flood a wall with a name. </summary>
        public const int MaximumDisplayNameLength = 40;

        /// <summary> Longest bio accepted. </summary>
        public const int MaximumBioLength = 200;

        /// <summary> Id this profile is stored under. </summary>
        public DocumentId<ProfileData> Id => new(Address);
    }
}
