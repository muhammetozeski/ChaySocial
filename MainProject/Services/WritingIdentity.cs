using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Identity;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// A name somebody can publish under: their own account, or a page they hold the keys to. It carries the
    /// unlocked identity that will actually sign, so a composer picks a name and the post is signed by whatever
    /// that name really is.
    /// </summary>
    /// <param name="Address"> The address posts will carry. </param>
    /// <param name="Name"> What to show in the picker. </param>
    /// <param name="Avatar"> The emoji to show beside it. </param>
    /// <param name="Signer"> The unlocked identity that signs; the reader's own account, or the page opened as an editor. </param>
    /// <param name="IsPage"> True when this is a page rather than the reader themselves. </param>
    public readonly record struct WritingIdentity(
        string Address,
        string Name,
        string Avatar,
        PrivateIdentity Signer,
        bool IsPage);

    /// <summary> Builds the list of names one account may publish under. </summary>
    public static class WritingIdentities
    {
        /// <summary>
        /// Reads everything this account can post as: itself, then every page whose keys it holds. A page whose
        /// sealed keys will not open is left out rather than offered and then refused at the moment of posting.
        /// </summary>
        /// <param name="account"> The unlocked signed-in account. </param>
        /// <returns> The reader's own identity first, then their pages in the order the store returns them. </returns>
        public static async Task<IReadOnlyList<WritingIdentity>> ReadForAsync(PrivateIdentity account)
        {
            ProfileData? own = SessionService.CurrentProfile ?? await ProfileService.ReadAsync(account.Public.Address);

            List<WritingIdentity> identities =
            [
                new WritingIdentity(
                    account.Public.Address,
                    string.IsNullOrWhiteSpace(own?.DisplayName)
                        ? ProfileService.FallbackDisplayName(account.Public.Address)
                        : own.DisplayName,
                    string.IsNullOrWhiteSpace(own?.Avatar) ? ProfileData.DefaultAvatar : own.Avatar,
                    account,
                    IsPage: false)
            ];

            IReadOnlyList<PageData> pages = await PageService.ReadPagesOfAsync(account.Public.Address);

            foreach (PageData page in pages)
            {
                PrivateIdentity? signer = await PageService.OpenAsEditorAsync(account, page);
                if (signer is null) continue;

                ProfileData? profile = await ProfileService.ReadAsync(page.Address);

                identities.Add(new WritingIdentity(
                    page.Address,
                    string.IsNullOrWhiteSpace(profile?.DisplayName)
                        ? ProfileService.FallbackDisplayName(page.Address)
                        : profile.DisplayName,
                    string.IsNullOrWhiteSpace(profile?.Avatar) ? ProfileService.PickAvatar(page.Address) : profile.Avatar,
                    signer,
                    IsPage: true));
            }

            return identities;
        }
    }
}
