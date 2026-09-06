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

        /// <summary>
        /// A face its owner drew on the board, or null when they wear an emoji. Kept inside the profile rather than
        /// uploaded as a blob: a profile is already read once per author on a screen, and a fetch per face would be
        /// paid a second time on every one of them. That is what the two caps below are for — a face has to stay
        /// small enough that carrying it inline is cheaper than fetching it.
        /// </summary>
        public DrawingSheet? AvatarSketch { get; init; }

        /// <summary> Short self-description. </summary>
        public string Bio { get; init; } = string.Empty;

        /// <summary> When the account first published its profile. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Base64 signing key, republished here so readers can verify posts without a second lookup. </summary>
        public required string SigningPublicKey { get; init; }

        /// <summary> Base64 encryption key, so anyone can encrypt to this account. </summary>
        public required string EncryptionPublicKey { get; init; }

        /// <summary> Which chain <see cref="TipAddress"/> belongs to, empty when the owner takes no tips. </summary>
        public string TipCurrency { get; init; } = string.Empty;

        /// <summary> Where somebody can send this account a little money, empty when the owner takes none. </summary>
        public string TipAddress { get; init; } = string.Empty;

        /// <summary>
        /// Base64 signature over the two fields above, produced by this account's own signing key.
        /// </summary>
        /// <remarks>
        /// The rest of a profile is unsigned and can afford to be: a swapped display name is a cosmetic lie. A
        /// swapped payment address is theft, and the server is the one place it could be swapped. The account's
        /// address commits to its signing key and that key signs this, so the chain from address to payment address
        /// cannot be broken by whoever is holding the documents.
        /// </remarks>
        public string TipSignature { get; init; } = string.Empty;

        /// <summary> True when the owner has published somewhere to send money. </summary>
        public bool AcceptsTips => TipAddress.Length > 0 && TipCurrency.Length > 0;

        /// <summary> Longest payment address accepted; a Monero address is 95 characters and this leaves room to spare. </summary>
        public const int MaximumTipAddressLength = 160;

        /// <summary> Emoji a profile starts with before its owner picks one. </summary>
        public const string DefaultAvatar = "🫖";

        /// <summary>
        /// Strokes a drawn face may hold, well under what the board itself allows a drawing.
        /// </summary>
        /// <remarks>
        /// A face is not a picture: five strokes make one, and thirty is already a laboured one. The cap exists
        /// because this drawing travels inside a document that is read once per author on a screen.
        /// </remarks>
        public const int MaximumAvatarSketchStrokes = 30;

        /// <summary>
        /// Points a drawn face may hold across all of its strokes together, which is the number that actually
        /// decides what the profile weighs.
        /// </summary>
        /// <remarks>
        /// A stroke cap alone would not bind: thirty strokes of five hundred points each is a fat document however
        /// few strokes it has. Measured against the board's own three-pixel sampling, an outline with two eyes, a
        /// nose and a mouth comes to about 230 points, so this leaves room for a face with some detail in it and
        /// refuses one that has become a picture. See docs/Measurements.md for what each cap weighs.
        /// </remarks>
        public const int MaximumAvatarSketchPointsAltogether = 400;

        /// <summary> Longest display name accepted, so one account cannot flood a wall with a name. </summary>
        public const int MaximumDisplayNameLength = 40;

        /// <summary> Longest bio accepted. </summary>
        public const int MaximumBioLength = 200;

        /// <summary> True when this profile carries a drawn face that may be worn as it stands. </summary>
        public bool HasWearableFace => IsWearableFace(AvatarSketch);

        /// <summary>
        /// True when a drawn face is small enough and shaped right to be worn.
        /// </summary>
        /// <param name="sketch"> The face to judge, or null for a profile that carries none. </param>
        /// <returns> True when it may be drawn as it stands. </returns>
        /// <remarks>
        /// Asked of every face that arrives from the store as well as of one just drawn, because that document was
        /// written by somebody else's device: the caps only bind the editor on this one. A face that fails is not
        /// an error to report — the emoji underneath it is what gets drawn instead.
        /// </remarks>
        public static bool IsWearableFace(DrawingSheet? sketch) =>
            sketch is not null
            && sketch.IsDrawable
            && sketch.Strokes.Count is > 0 and <= MaximumAvatarSketchStrokes
            && sketch.Strokes.Sum(stroke => stroke.Points.Count) <= MaximumAvatarSketchPointsAltogether;

        /// <summary> Id this profile is stored under. </summary>
        public DocumentId<ProfileData> Id => new(Address);
    }
}
