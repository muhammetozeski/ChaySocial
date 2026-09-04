using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Blocking accounts and filing reports.
    /// <para>
    /// Blocks are the reader's own decision and are stored under a deterministic id, so blocking an account twice
    /// overwrites the same row and unblocking is a delete.
    /// </para>
    /// <para>
    /// Reports are where the app's content policy shows: this app does not hand post content to the server as a
    /// matter of course, so <see cref="ReportPostAsync"/> copying the post's text into
    /// <see cref="ReportData.DisclosedContent"/> is the single path by which content reaches the server for review.
    /// Nothing else in this service sends content anywhere — <see cref="ReportAccountAsync"/> discloses no content
    /// at all, and a block never leaves a copy of what was blocked.
    /// </para>
    /// </summary>
    public static class ModerationService
    {
        /// <summary> Random bytes behind a report id — the same amount a post id is built from, so reports never collide. </summary>
        const int ReportIdRandomBytes = 12;

        /// <summary> Largest number of block rows read back for one account in either direction. </summary>
        const int MaximumBlocksPerAccount = 500;

        /// <summary>
        /// Records that one account no longer wants to see another. An account cannot block itself, and an empty
        /// address is not an account, so both are refused instead of stored.
        /// </summary>
        /// <param name="blocker"> The unlocked account placing the block. </param>
        /// <param name="blockedAddress"> Address of the account to stop seeing. </param>
        /// <returns> True when the block was stored; false when the address was the blocker's own or was blank. </returns>
        public static async Task<bool> BlockAsync(PrivateIdentity blocker, string blockedAddress)
        {
            string blockerAddress = blocker.Public.Address;
            if (string.IsNullOrWhiteSpace(blockedAddress)) return false;
            if (string.Equals(blockerAddress, blockedAddress, StringComparison.Ordinal)) return false;

            BlockData block = new()
            {
                BlockerAddress = blockerAddress,
                BlockedAddress = blockedAddress,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await AppServices.Documents.WriteAsync(block.Id, block);
            MainEvents.Trigger(MainEvents.Names.ModerationChanged, blockedAddress);
            return true;
        }

        /// <summary> Lifts a block. Lifting one that was never placed is not an error. </summary>
        /// <param name="blocker"> The unlocked account that had placed the block. </param>
        /// <param name="blockedAddress"> Address that was blocked. </param>
        public static async Task UnblockAsync(PrivateIdentity blocker, string blockedAddress)
        {
            await AppServices.Documents.DeleteAsync(BlockData.IdFor(blocker.Public.Address, blockedAddress));
            MainEvents.Trigger(MainEvents.Names.ModerationChanged, blockedAddress);
        }

        /// <summary> Checks whether one account has blocked another. </summary>
        /// <param name="blockerAddress"> Account that may have placed a block. </param>
        /// <param name="blockedAddress"> Account that may have been blocked. </param>
        /// <returns> True when a block from the first account on the second is stored. </returns>
        public static async Task<bool> IsBlockedAsync(string blockerAddress, string blockedAddress)
            => await AppServices.Documents.ReadAsync(BlockData.IdFor(blockerAddress, blockedAddress)) is not null;

        /// <summary> Reads every account one account has blocked, for hiding them from its own feeds. </summary>
        /// <param name="blockerAddress"> Account whose blocks are wanted. </param>
        /// <returns> Addresses that account has blocked, at most <see cref="MaximumBlocksPerAccount"/> of them. </returns>
        public static async Task<IReadOnlyList<string>> ReadBlockedAddressesAsync(string blockerAddress)
        {
            DocumentQuery<BlockData> query = new DocumentQuery<BlockData>()
                .WithMatch(BlockData.BlockerField, blockerAddress)
                .WithLimit(MaximumBlocksPerAccount);

            return [.. (await AppServices.Documents.QueryAsync(query)).Documents.Select(block => block.BlockedAddress)];
        }

        /// <summary>
        /// Reads every account that has blocked one account. A block cuts both ways, so this is what keeps an
        /// account out of the feeds and threads of the people who blocked it.
        /// </summary>
        /// <param name="blockedAddress"> Account that may have been blocked by others. </param>
        /// <returns> Addresses that blocked it, at most <see cref="MaximumBlocksPerAccount"/> of them. </returns>
        public static async Task<IReadOnlyList<string>> ReadBlockedByAddressesAsync(string blockedAddress)
        {
            DocumentQuery<BlockData> query = new DocumentQuery<BlockData>()
                .WithMatch(BlockData.BlockedField, blockedAddress)
                .WithLimit(MaximumBlocksPerAccount);

            return [.. (await AppServices.Documents.QueryAsync(query)).Documents.Select(block => block.BlockerAddress)];
        }

        /// <summary>
        /// Files a complaint about a post, and copies the post's text into the report as
        /// <see cref="ReportData.DisclosedContent"/>.
        /// <para>
        /// That copy is the point of the method and the one thing to understand about it: the app does not hand post
        /// content to the server as a matter of course, so this is the single path by which content reaches the
        /// server for review. A reporter is choosing, at this moment, to disclose what they read.
        /// </para>
        /// </summary>
        /// <param name="reporter"> The unlocked account filing the complaint. </param>
        /// <param name="post"> Post being complained about; its text is what gets disclosed. </param>
        /// <param name="reason"> Category the reporter chose. </param>
        /// <param name="detail"> What the reporter wrote in their own words; trimmed, and refused when longer than <see cref="ReportData.MaximumDetailLength"/>. </param>
        /// <returns> The stored report, or null when the detail was too long to store. </returns>
        public static async Task<ReportData?> ReportPostAsync(PrivateIdentity reporter, PostData post, ReportReason reason, string detail = "")
        {
            if (TrimDetail(detail) is not string trimmedDetail) return null;

            ReportData report = new()
            {
                ReportId = CreateReportId(),
                ReporterAddress = reporter.Public.Address,
                TargetPostId = post.PostId,
                Reason = reason,
                Detail = trimmedDetail,
                DisclosedContent = post.Text,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await AppServices.Documents.WriteAsync(report.Id, report);
            MainEvents.Trigger(MainEvents.Names.ModerationChanged, post.AuthorAddress);
            return report;
        }

        /// <summary>
        /// Files a complaint about an account rather than about one thing it wrote. No content is disclosed: the
        /// report carries the reporter's own words and nothing the reported account authored.
        /// </summary>
        /// <param name="reporter"> The unlocked account filing the complaint. </param>
        /// <param name="targetAddress"> Address being complained about. </param>
        /// <param name="reason"> Category the reporter chose. </param>
        /// <param name="detail"> What the reporter wrote in their own words; trimmed, and refused when longer than <see cref="ReportData.MaximumDetailLength"/>. </param>
        /// <returns> The stored report, or null when the address was blank or the detail was too long to store. </returns>
        public static async Task<ReportData?> ReportAccountAsync(PrivateIdentity reporter, string targetAddress, ReportReason reason, string detail = "")
        {
            if (string.IsNullOrWhiteSpace(targetAddress)) return null;
            if (TrimDetail(detail) is not string trimmedDetail) return null;

            ReportData report = new()
            {
                ReportId = CreateReportId(),
                ReporterAddress = reporter.Public.Address,
                TargetAddress = targetAddress,
                Reason = reason,
                Detail = trimmedDetail,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await AppServices.Documents.WriteAsync(report.Id, report);
            MainEvents.Trigger(MainEvents.Names.ModerationChanged, targetAddress);
            return report;
        }

        /// <summary> Draws the id a new report is stored under, the same way a post id is drawn. </summary>
        /// <returns> Base32 text over <see cref="ReportIdRandomBytes"/> random bytes. </returns>
        static string CreateReportId() => Base32.Encode(RandomSource.Next(ReportIdRandomBytes));

        /// <summary> Trims a reporter's free-text detail and rejects one too long to store. </summary>
        /// <param name="detail"> Text the reporter typed, or null when they typed nothing. </param>
        /// <returns> The trimmed text — empty is allowed — or null when it is longer than <see cref="ReportData.MaximumDetailLength"/>. </returns>
        static string? TrimDetail(string? detail)
        {
            string trimmed = detail?.Trim() ?? string.Empty;
            return trimmed.Length > ReportData.MaximumDetailLength ? null : trimmed;
        }
    }
}
