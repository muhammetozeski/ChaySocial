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
    /// Founding pages and handing out the right to speak as one. A page is an account in its own right — same kind
    /// of keypair, same kind of address, same published profile — so every screen that already draws an account
    /// draws a page without being told about pages at all.
    /// </summary>
    /// <remarks>
    /// What makes a page shared rather than a founder relaying other people is how the right travels: the page's
    /// master seed is sealed to each editor's own encryption key and stored beside their name. An editor opens it
    /// with their own account and signs as the page directly, so a post carries the page's signature whoever wrote
    /// it. The server holds those sealed bytes and can open none of them.
    /// </remarks>
    public static class PageService
    {
        /// <summary> Separates a page's founding signature from every other signature the app produces. </summary>
        static readonly byte[] PageSignatureDomain = "ChaySocial/Page/v1"u8.ToArray();

        /// <summary> Separates the derivation of a page's keys from every other use of a seed. </summary>
        static readonly byte[] PageSeedContext = "ChaySocial/PageSeed/v1"u8.ToArray();

        /// <summary> Separates the sealing of a page seed from every other use of the cipher. </summary>
        static readonly byte[] SealedSeedDomain = "ChaySocial/PageSeedSeal/v1"u8.ToArray();

        /// <summary> Pages fetched in one page of a listing. </summary>
        public const int PageListSize = 30;

        /// <summary> Editors read back for one page. </summary>
        public const int EditorPageSize = 50;

        /// <summary> Random bytes behind a page's seed salt. </summary>
        const int SeedSaltBytes = 16;

        /// <summary>
        /// Founds a page. Its keys come from the founder's seed and a fresh salt, it publishes an ordinary profile
        /// under its own address so the rest of the app can see it, and the founder is written in as its first
        /// editor with the page's seed sealed to them.
        /// </summary>
        /// <param name="founder"> The unlocked account founding it. </param>
        /// <param name="name"> What to call it; trimmed, and refused when empty or over <see cref="ProfileData.MaximumDisplayNameLength"/>. </param>
        /// <param name="description"> A line about it, stored as the page profile's bio. </param>
        /// <param name="avatar"> Emoji standing in for its picture; blank falls back to the profile default. </param>
        /// <returns> The stored page, or null when the details were not usable. </returns>
        public static async Task<PageData?> FoundAsync(
            PrivateIdentity founder,
            string name,
            string description = "",
            string avatar = "")
        {
            string trimmedName = name.Trim();
            string trimmedDescription = description.Trim();

            if (trimmedName.Length == 0 || trimmedName.Length > ProfileData.MaximumDisplayNameLength) return null;
            if (trimmedDescription.Length > ProfileData.MaximumBioLength) return null;

            byte[] salt = RandomSource.Next(SeedSaltBytes);
            byte[] pageSeed = DerivePageSeed(founder, salt);
            PrivateIdentity page = AppCryptography.Identities.Open(pageSeed);

            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            byte[] transcript = BuildTranscript(page.Public.Address, founder.Public.Address, createdAt);

            PageData record = new()
            {
                Address = page.Public.Address,
                FounderAddress = founder.Public.Address,
                CreatedAtUnixMs = createdAt,
                Signature = Convert.ToBase64String(founder.Sign(transcript))
            };

            // The profile goes first: a page nobody can look up is a page that does not exist as far as every
            // other screen is concerned.
            await ProfileService.SaveAsync(new ProfileData
            {
                Address = page.Public.Address,
                DisplayName = trimmedName,
                Bio = trimmedDescription,
                Avatar = string.IsNullOrWhiteSpace(avatar) ? ProfileService.PickAvatar(page.Public.Address) : avatar.Trim(),
                CreatedAtUnixMs = createdAt,
                SigningPublicKey = Convert.ToBase64String(page.Public.SigningPublicKey),
                EncryptionPublicKey = Convert.ToBase64String(page.Public.EncryptionPublicKey)
            });

            await AppServices.Documents.WriteAsync(record.Id, record);
            await GrantEditorAsync(record, pageSeed, founder.Public, createdAt);

            MainEvents.Trigger(MainEvents.Names.PagesChanged, record.Address);
            return record;
        }

        /// <summary> Reads one page by its address. </summary>
        /// <param name="address"> The page's address. </param>
        /// <returns> The page, or null when nothing is stored under it. </returns>
        public static Task<PageData?> ReadAsync(string address)
            => address.Length == 0 ? Task.FromResult<PageData?>(null) : AppServices.Documents.ReadAsync(new DocumentId<PageData>(address));

        /// <summary> Reads the newest pages, for somebody looking for one to follow. </summary>
        /// <param name="limit"> Largest number of pages to return. </param>
        /// <returns> Pages, newest first. </returns>
        public static async Task<IReadOnlyList<PageData>> ReadRecentAsync(int limit = PageListSize)
        {
            DocumentQuery<PageData> query = new DocumentQuery<PageData>()
                .WithSort(PageData.CreatedAtField, descending: true)
                .WithLimit(limit);

            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary> Reads the pages one account may speak as. </summary>
        /// <param name="editorAddress"> The account. </param>
        /// <param name="limit"> Largest number of pages to return. </param>
        /// <returns> Those pages, with any that have since been deleted left out. </returns>
        public static async Task<IReadOnlyList<PageData>> ReadPagesOfAsync(string editorAddress, int limit = PageListSize)
        {
            if (editorAddress.Length == 0 || limit <= 0) return [];

            DocumentQuery<PageEditorData> query = new DocumentQuery<PageEditorData>()
                .WithMatch(PageEditorData.EditorField, editorAddress)
                .WithSort(PageEditorData.CreatedAtField, descending: true)
                .WithLimit(limit);

            IReadOnlyList<PageEditorData> rights = (await AppServices.Documents.QueryAsync(query)).Documents;
            if (rights.Count == 0) return [];

            PageData?[] pages = await Task.WhenAll(rights.Select(right => ReadAsync(right.PageAddress)));

            return [.. pages.Where(page => page is not null).Select(page => page!)];
        }

        /// <summary> Reads who may speak as one page, in the order they were given the right. </summary>
        /// <param name="pageAddress"> The page. </param>
        /// <param name="limit"> Largest number of editors to return. </param>
        /// <returns> The editors' addresses, oldest first. </returns>
        public static async Task<IReadOnlyList<string>> ReadEditorsAsync(string pageAddress, int limit = EditorPageSize)
        {
            DocumentQuery<PageEditorData> query = new DocumentQuery<PageEditorData>()
                .WithMatch(PageEditorData.PageField, pageAddress)
                .WithSort(PageEditorData.CreatedAtField)
                .WithLimit(limit);

            return [.. (await AppServices.Documents.QueryAsync(query)).Documents.Select(right => right.EditorAddress)];
        }

        /// <summary>
        /// Hands somebody the right to speak as a page. Only the founder may: the seed has to be opened before it
        /// can be sealed to anybody new, and the founder is the one who can open it.
        /// </summary>
        /// <param name="page"> The page. </param>
        /// <param name="founder"> The unlocked founding account. </param>
        /// <param name="editorProfile"> Profile of the account being let in, read for its published encryption key. </param>
        /// <returns> True once that account may speak as the page. </returns>
        public static async Task<bool> AddEditorAsync(PageData page, PrivateIdentity founder, ProfileData editorProfile)
        {
            if (page.FounderAddress != founder.Public.Address) return false;
            if (!TryReadPublishedKeys(editorProfile, out PublicIdentity? editor)) return false;

            // The founder gets at the seed the same way any editor does — by opening their own sealed copy. That
            // is why the page's salt is never stored: after founding, the sealed copies are the only way in, and
            // losing them is the same as losing the page, which is the honest shape of the thing.
            PrivateIdentity? opened = await OpenAsEditorAsync(founder, page);
            if (opened is null) return false;

            await GrantEditorAsync(page, opened.ExportMasterSeed(), editor!, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            MainEvents.Trigger(MainEvents.Names.PagesChanged, page.Address);
            return true;
        }

        /// <summary>
        /// Takes the right back. The founder's own cannot be taken: a page nobody can edit is a page nobody can
        /// look after.
        /// </summary>
        /// <param name="page"> The page. </param>
        /// <param name="founder"> The unlocked founding account. </param>
        /// <param name="editorAddress"> Account losing the right. </param>
        /// <returns> True when that account can no longer speak as the page. </returns>
        public static async Task<bool> RemoveEditorAsync(PageData page, PrivateIdentity founder, string editorAddress)
        {
            if (page.FounderAddress != founder.Public.Address) return false;
            if (editorAddress == page.FounderAddress) return false;

            await AppServices.Documents.DeleteAsync(PageEditorData.IdFor(page.Address, editorAddress));
            MainEvents.Trigger(MainEvents.Names.PagesChanged, page.Address);
            return true;
        }

        /// <summary>
        /// Opens the page as one of its editors, giving back an identity that can sign posts as the page itself.
        /// </summary>
        /// <param name="editor"> The unlocked account trying to speak as the page. </param>
        /// <param name="page"> The page to open. </param>
        /// <returns> The page's unlocked identity, or null when this account holds no right to it. </returns>
        public static async Task<PrivateIdentity?> OpenAsEditorAsync(PrivateIdentity editor, PageData page)
        {
            PageEditorData? right = await AppServices.Documents.ReadAsync(
                PageEditorData.IdFor(page.Address, editor.Public.Address));

            if (right is null) return null;

            try
            {
                byte[] encapsulation = Convert.FromBase64String(right.Encapsulation);
                if (encapsulation.Length != AppCryptography.KeyExchange.EncapsulationSize) return null;

                byte[] sharedSecret = editor.Decapsulate(encapsulation);
                byte[] nonce = Convert.FromBase64String(right.Nonce);
                byte[] sealedSeed = Convert.FromBase64String(right.SealedSeed);

                if (!AppCryptography.Cipher.TryDecrypt(sealedSeed, sharedSecret, nonce, SealedSeedDomain, out byte[] pageSeed))
                {
                    return null;
                }

                PrivateIdentity opened = AppCryptography.Identities.Open(pageSeed);

                // A seed that opens to a different address was sealed for another page, or tampered with. Either
                // way it must not be used to sign anything in this page's name.
                return opened.Public.Address == page.Address ? opened : null;
            }
            catch (Exception error) when (error is FormatException or ArgumentException)
            {
                Log($"Editing right on page '{page.Address}' is malformed.\n{error}", LogLevel.Warning);
                return null;
            }
        }

        /// <summary>
        /// Checks that a page really was founded by the account it names, and that its address belongs to the keys
        /// its profile publishes.
        /// </summary>
        /// <param name="page"> Page to check. </param>
        /// <param name="pageProfile"> The page's own published profile. </param>
        /// <param name="founderProfile"> Profile of the account it names as founder. </param>
        /// <returns> True when both hold. </returns>
        public static bool VerifyFounder(PageData page, ProfileData? pageProfile, ProfileData? founderProfile)
        {
            if (founderProfile is null || founderProfile.Address != page.FounderAddress) return false;
            if (pageProfile is null || pageProfile.Address != page.Address) return false;

            try
            {
                if (!AppCryptography.Addresses.Matches(
                        page.Address,
                        Convert.FromBase64String(pageProfile.SigningPublicKey),
                        Convert.FromBase64String(pageProfile.EncryptionPublicKey)))
                {
                    return false;
                }

                PublicIdentity founder = new(
                    founderProfile.Address,
                    Convert.FromBase64String(founderProfile.SigningPublicKey),
                    Convert.FromBase64String(founderProfile.EncryptionPublicKey));

                byte[] transcript = BuildTranscript(page.Address, page.FounderAddress, page.CreatedAtUnixMs);
                return AppCryptography.Identities.Verify(transcript, Convert.FromBase64String(page.Signature), founder);
            }
            catch (FormatException error)
            {
                Log($"Page '{page.Address}' carries malformed base64.\n{error}", LogLevel.Warning);
                return false;
            }
        }

        /// <summary> Seals the page's seed to one account and stores the right. </summary>
        /// <param name="page"> The page. </param>
        /// <param name="pageSeed"> The page's master seed, in the clear. </param>
        /// <param name="editor"> The account being let in. </param>
        /// <param name="createdAtUnixMs"> When the right is granted. </param>
        /// <returns> A task that completes once the right is stored. </returns>
        static async Task GrantEditorAsync(PageData page, byte[] pageSeed, PublicIdentity editor, long createdAtUnixMs)
        {
            EncapsulationResult secret = AppCryptography.Identities.EncapsulateTo(editor);
            byte[] nonce = RandomSource.Next(AppCryptography.Cipher.NonceSize);

            byte[] sealedSeed = AppCryptography.Cipher.Encrypt(pageSeed, secret.SharedSecret, nonce, SealedSeedDomain);

            PageEditorData right = new()
            {
                PageAddress = page.Address,
                EditorAddress = editor.Address,
                Encapsulation = Convert.ToBase64String(secret.Encapsulation),
                Nonce = Convert.ToBase64String(nonce),
                SealedSeed = Convert.ToBase64String(sealedSeed),
                CreatedAtUnixMs = createdAtUnixMs
            };

            await AppServices.Documents.WriteAsync(right.Id, right);
        }

        /// <summary> Derives a page's master seed from the founder's seed and a salt, once, at founding. </summary>
        /// <param name="founder"> The founding account. </param>
        /// <param name="salt"> The page's salt. </param>
        /// <returns> The page's master seed. </returns>
        static byte[] DerivePageSeed(PrivateIdentity founder, ReadOnlySpan<byte> salt)
            => AppCryptography.SeedExpander.Derive(
                founder.ExportMasterSeed(), salt, PageSeedContext, IdentityScheme.MasterSeedSize);

        /// <summary> Rebuilds the published half of an identity out of a profile. </summary>
        /// <param name="profile"> Profile carrying the two base64 public keys. </param>
        /// <param name="identity"> Receives the rebuilt identity, or null when the profile is malformed. </param>
        /// <returns> True when both keys decoded. </returns>
        static bool TryReadPublishedKeys(ProfileData profile, out PublicIdentity? identity)
        {
            identity = null;

            try
            {
                identity = new PublicIdentity(
                    profile.Address,
                    Convert.FromBase64String(profile.SigningPublicKey),
                    Convert.FromBase64String(profile.EncryptionPublicKey));

                return true;
            }
            catch (FormatException error)
            {
                Log($"Profile '{profile.Address}' carries malformed base64.\n{error}", LogLevel.Warning);
                return false;
            }
        }

        /// <summary> Builds the exact bytes a founder signs and a reader verifies. </summary>
        /// <param name="address"> The page's address. </param>
        /// <param name="founderAddress"> Address of the founding account. </param>
        /// <param name="createdAtUnixMs"> When it was founded. </param>
        /// <returns> The transcript to sign. </returns>
        static byte[] BuildTranscript(string address, string founderAddress, long createdAtUnixMs)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(PageSignatureDomain);
            transcript.WriteText(address);
            transcript.WriteText(founderAddress);
            transcript.WriteInt64(createdAtUnixMs);
            return transcript.ToArray();
        }
    }
}
