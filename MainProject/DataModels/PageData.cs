using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary>
    /// A publishing account several people can speak through: a brand, a project, a community voice. Like a group
    /// it is its own keypair, so its address falls out of its keys and its posts are signed as the page rather than
    /// as whichever person happened to be typing.
    /// </summary>
    /// <remarks>
    /// A page also publishes an ordinary <see cref="ProfileData"/> under the same address, which is what lets every
    /// screen in the app already handle it: following, searching, feeds, chays and comments all see an account and
    /// need to know nothing about pages at all.
    /// </remarks>
    public sealed record PageData : IStoredDocument<PageData>
    {
        public static string CollectionName => "pages";

        /// <summary> The page's own address, derived from its keys exactly as an account's is. </summary>
        public required string Address { get; init; }

        /// <summary> Address of the account that founded it, and the only one that can hand out editing rights. </summary>
        public required string FounderAddress { get; init; }

        /// <summary> When it was founded. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Base64 signature over the page's own fields, produced by the founder's signing key. </summary>
        public required string Signature { get; init; }

        /// <summary> Id this page is stored under, which is its address. </summary>
        public DocumentId<PageData> Id => new(Address);

        /// <summary> Founder address, for reading the pages one account started. </summary>
        public static readonly DocumentField<PageData> FounderField = new(nameof(FounderAddress), page => page.FounderAddress);

        /// <summary> Founding time, for listing pages newest first. </summary>
        public static readonly DocumentField<PageData> CreatedAtField = new(nameof(CreatedAtUnixMs), page => page.CreatedAtUnixMs);
    }

    /// <summary>
    /// One person's right to speak as one page, and the means to do it: the page's own seed, sealed to that
    /// person's encryption key. Nobody else can open it — not another editor, and not the server, which stores the
    /// sealed bytes without ever being able to use them.
    /// </summary>
    /// <remarks>
    /// This is what makes a page genuinely shared rather than a founder relaying other people's words. An editor
    /// derives the page's keys from the seed they were handed and signs as the page directly, so a post carries the
    /// page's signature no matter which of them wrote it.
    /// </remarks>
    public sealed record PageEditorData : IStoredDocument<PageEditorData>
    {
        public static string CollectionName => "pageeditors";

        /// <summary> Page this right is for. </summary>
        public required string PageAddress { get; init; }

        /// <summary> Account holding it. </summary>
        public required string EditorAddress { get; init; }

        /// <summary> Base64 key encapsulation the editor feeds back into their own key to recover the sealing secret. </summary>
        public required string Encapsulation { get; init; }

        /// <summary> Base64 nonce the seed was sealed under. </summary>
        public required string Nonce { get; init; }

        /// <summary> Base64 ciphertext of the page's master seed, readable only by this editor. </summary>
        public required string SealedSeed { get; init; }

        /// <summary> When the right was granted. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> Id this right is stored under. </summary>
        public DocumentId<PageEditorData> Id => IdFor(PageAddress, EditorAddress);

        /// <summary> Builds the id one account's editing right on one page is stored under. </summary>
        /// <param name="pageAddress"> The page. </param>
        /// <param name="editorAddress"> The account. </param>
        /// <returns> The document id. </returns>
        public static DocumentId<PageEditorData> IdFor(string pageAddress, string editorAddress)
            => new($"{pageAddress}:{editorAddress}");

        /// <summary> Page address, for reading who may speak as one page. </summary>
        public static readonly DocumentField<PageEditorData> PageField = new(nameof(PageAddress), editor => editor.PageAddress);

        /// <summary> Editor address, for reading which pages one account may speak as. </summary>
        public static readonly DocumentField<PageEditorData> EditorField = new(nameof(EditorAddress), editor => editor.EditorAddress);

        /// <summary> Time the right was granted, for listing editors oldest first. </summary>
        public static readonly DocumentField<PageEditorData> CreatedAtField = new(nameof(CreatedAtUnixMs), editor => editor.CreatedAtUnixMs);
    }
}
