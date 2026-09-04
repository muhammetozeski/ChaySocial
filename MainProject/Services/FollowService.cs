using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Who follows whom. Each follow is its own document keyed by the follower and followee pair, so pressing follow
    /// twice lands on the same id instead of adding a second row, unfollowing is a plain delete, and the two halves of
    /// a mutual follow stay independent — either side can drop its own without touching the other's.
    /// </summary>
    public static class FollowService
    {
        /// <summary> Accounts returned in one page of a following or followers list. </summary>
        public const int FollowPageSize = 50;

        /// <summary>
        /// Largest number of follows one count call looks at. A count stops here rather than walking an unbounded
        /// collection, so a very popular account reports this figure instead of stalling the page it renders on.
        /// </summary>
        public const int MaximumCountedFollows = 500;

        /// <summary>
        /// Records that one account follows another and tells the followee about it. Following an account that is
        /// already followed changes nothing and raises no second notification, because the pair's id is the same
        /// document either way.
        /// </summary>
        /// <param name="follower"> The unlocked account doing the following. </param>
        /// <param name="followeeAddress"> Address of the account being followed. </param>
        /// <returns> True when the follow now stands; false when it was refused, which happens for a blank address and for following yourself. </returns>
        public static async Task<bool> FollowAsync(PrivateIdentity follower, string followeeAddress)
        {
            string followerAddress = follower.Public.Address;
            if (!IsFollowable(followerAddress, followeeAddress)) return false;

            DocumentId<FollowData> id = FollowData.IdFor(followerAddress, followeeAddress);
            if (await AppServices.Documents.ReadAsync(id) is not null) return true;

            await AppServices.Documents.WriteAsync(id, new FollowData
            {
                FollowerAddress = followerAddress,
                FolloweeAddress = followeeAddress,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            await NotificationService.NotifyAsync(followeeAddress, follower.Public.Address, NotificationKind.Follow);
            MainEvents.Trigger(MainEvents.Names.FollowChanged, followeeAddress);
            return true;
        }

        /// <summary>
        /// Drops one account's follow of another. Unfollowing someone who was never followed removes nothing and is
        /// not an error, so a stale button cannot fail.
        /// </summary>
        /// <param name="follower"> The unlocked account letting go. </param>
        /// <param name="followeeAddress"> Address of the account being dropped. </param>
        public static async Task UnfollowAsync(PrivateIdentity follower, string followeeAddress)
        {
            await AppServices.Documents.DeleteAsync(FollowData.IdFor(follower.Public.Address, followeeAddress));
            MainEvents.Trigger(MainEvents.Names.FollowChanged, followeeAddress);
        }

        /// <summary> Checks whether one account currently follows another. </summary>
        /// <param name="followerAddress"> Address of the account that would be doing the following. </param>
        /// <param name="followeeAddress"> Address of the account that would be followed. </param>
        /// <returns> True while the follow document exists. </returns>
        public static async Task<bool> IsFollowingAsync(string followerAddress, string followeeAddress)
            => await AppServices.Documents.ReadAsync(FollowData.IdFor(followerAddress, followeeAddress)) is not null;

        /// <summary> Reads the accounts one address follows, most recently followed first. </summary>
        /// <param name="address"> Address whose following list is wanted. </param>
        /// <param name="limit"> Largest number of addresses to return. </param>
        /// <returns> Addresses this account follows. </returns>
        public static async Task<IReadOnlyList<string>> ReadFollowingAsync(string address, int limit = FollowPageSize)
        {
            DocumentQuery<FollowData> query = new DocumentQuery<FollowData>()
                .WithMatch(FollowData.FollowerField, address)
                .WithSort(FollowData.CreatedAtField, descending: true)
                .WithLimit(limit);

            return [.. (await AppServices.Documents.QueryAsync(query)).Documents.Select(follow => follow.FolloweeAddress)];
        }

        /// <summary> Reads the accounts following one address, newest follower first. </summary>
        /// <param name="address"> Address whose followers are wanted. </param>
        /// <param name="limit"> Largest number of addresses to return. </param>
        /// <returns> Addresses following this account. </returns>
        public static async Task<IReadOnlyList<string>> ReadFollowersAsync(string address, int limit = FollowPageSize)
        {
            DocumentQuery<FollowData> query = new DocumentQuery<FollowData>()
                .WithMatch(FollowData.FolloweeField, address)
                .WithSort(FollowData.CreatedAtField, descending: true)
                .WithLimit(limit);

            return [.. (await AppServices.Documents.QueryAsync(query)).Documents.Select(follow => follow.FollowerAddress)];
        }

        /// <summary> Counts how many accounts one address follows. </summary>
        /// <param name="address"> Address whose following total is wanted. </param>
        /// <returns> The total, stopping at <see cref="MaximumCountedFollows"/>. </returns>
        public static async Task<int> CountFollowingAsync(string address)
            => await CountAsync(FollowData.FollowerField, address);

        /// <summary> Counts how many accounts follow one address. </summary>
        /// <param name="address"> Address whose follower total is wanted. </param>
        /// <returns> The total, stopping at <see cref="MaximumCountedFollows"/>. </returns>
        public static async Task<int> CountFollowersAsync(string address)
            => await CountAsync(FollowData.FolloweeField, address);

        /// <summary>
        /// Decides whether a follow may be recorded at all. An account cannot follow itself: the pair would collapse
        /// to one address on both sides of the id, and the owner would be told about their own action.
        /// </summary>
        /// <param name="followerAddress"> Address doing the following. </param>
        /// <param name="followeeAddress"> Address being followed. </param>
        /// <returns> True when the two addresses are different accounts and neither side is blank. </returns>
        static bool IsFollowable(string followerAddress, string followeeAddress)
            => !string.IsNullOrWhiteSpace(followerAddress)
               && !string.IsNullOrWhiteSpace(followeeAddress)
               && !followerAddress.Equals(followeeAddress, StringComparison.Ordinal);

        /// <summary> Counts the follow documents whose given side equals an address. </summary>
        /// <param name="side"> Which end of the follow to match — <see cref="FollowData.FollowerField"/> or <see cref="FollowData.FolloweeField"/>. </param>
        /// <param name="address"> Address that side has to equal. </param>
        /// <returns> How many follows matched, never more than <see cref="MaximumCountedFollows"/>. </returns>
        static async Task<int> CountAsync(DocumentField<FollowData> side, string address)
        {
            DocumentQuery<FollowData> query = new DocumentQuery<FollowData>()
                .WithMatch(side, address)
                .WithLimit(MaximumCountedFollows);

            return (await AppServices.Documents.QueryAsync(query)).Documents.Count;
        }
    }
}
