using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// One account standing behind another, signed. It says "I know who holds these keys, and I call them this" —
    /// which is the one thing an address cannot say about itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed by the pair, so vouching twice writes the same document rather than a second one, and taking it back
    /// is a plain delete. Publicly readable, exactly as a follow is: this adds no visibility the follow graph did
    /// not already have.
    /// </para>
    /// <para>
    /// Signed, unlike a follow. A follow says nothing in anybody's name; this puts a sentence in somebody's mouth
    /// on another account's profile, and a server that could invent one could hand any account a reputation.
    /// </para>
    /// </remarks>
    public sealed record VouchData : IStoredDocument<VouchData>
    {
        public static string CollectionName => "vouches";

        /// <summary> Address of the account doing the vouching. </summary>
        public required string VoucherAddress { get; init; }

        /// <summary> Address of the account being vouched for. </summary>
        public required string SubjectAddress { get; init; }

        /// <summary> What the voucher calls them, in the voucher's own words. </summary>
        public required string KnownAsName { get; init; }

        /// <summary> When the vouch was made; inside the signature too, so it cannot be back-dated. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Base64 signature over every field above, produced by the voucher's signing key. </summary>
        public required string Signature { get; init; }

        /// <summary>
        /// Longest name a voucher may give, for the same reason a display name is capped: past this it stops being
        /// a name and starts being a paragraph on somebody else's profile.
        /// </summary>
        public const int MaximumKnownAsLength = 40;

        /// <summary> What joins the two addresses in the id. </summary>
        const string IdSeparator = ":";

        /// <summary> Id this vouch is stored under. </summary>
        public DocumentId<VouchData> Id => IdFor(VoucherAddress, SubjectAddress);

        /// <summary> Builds the id one account's vouch for another is stored under. </summary>
        /// <param name="voucherAddress"> Account doing the vouching. </param>
        /// <param name="subjectAddress"> Account being vouched for. </param>
        /// <returns> The document id. </returns>
        public static DocumentId<VouchData> IdFor(string voucherAddress, string subjectAddress)
            => new($"{voucherAddress}{IdSeparator}{subjectAddress}");

        /// <summary> Voucher address, for reading everyone one account has vouched for. </summary>
        public static readonly DocumentField<VouchData> VoucherField = new(nameof(VoucherAddress), vouch => vouch.VoucherAddress);

        /// <summary> Subject address, for reading everyone who has vouched for one account. </summary>
        public static readonly DocumentField<VouchData> SubjectField = new(nameof(SubjectAddress), vouch => vouch.SubjectAddress);

        /// <summary> When it was made, for listing vouches newest first. </summary>
        public static readonly DocumentField<VouchData> CreatedAtField = new(nameof(CreatedAtUnixMs), vouch => vouch.CreatedAtUnixMs);
    }
}
