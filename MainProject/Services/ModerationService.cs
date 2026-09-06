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

        /// <summary>
        /// Every account one reader should see nothing of and hear nothing from: the ones they blocked, and the
        /// ones that blocked them.
        /// </summary>
        /// <param name="ownerAddress"> The reader, or empty when nobody is signed in. </param>
        /// <returns> The addresses to shut out, empty when nobody is signed in. </returns>
        /// <remarks>
        /// A block is one promise — that there is nothing left between these two — and it only holds if both
        /// directions are read. Kept here rather than in each screen that needs it, so the rule has one definition
        /// and a screen written later cannot quietly implement half of it.
        /// </remarks>
        public static async Task<HashSet<string>> ReadShutOutAddressesAsync(string ownerAddress)
        {
            if (string.IsNullOrEmpty(ownerAddress)) return [];

            Task<IReadOnlyList<string>> blocked = ReadBlockedAddressesAsync(ownerAddress);
            Task<IReadOnlyList<string>> blockedBy = ReadBlockedByAddressesAsync(ownerAddress);
            await Task.WhenAll(blocked, blockedBy);

            return [.. await blocked, .. await blockedBy];
        }

        /// <summary> True when a block stands between two accounts, whichever of them made it. </summary>
        /// <param name="ownerAddress"> One account. </param>
        /// <param name="otherAddress"> The other. </param>
        /// <returns> True when either has blocked the other. </returns>
        /// <remarks>
        /// Two single reads rather than the two listings, because this answers about one pair. Somewhere judging
        /// a page of accounts should read the set once instead.
        /// </remarks>
        public static async Task<bool> IsShutOutAsync(string ownerAddress, string otherAddress)
        {
            if (ownerAddress.Length == 0 || otherAddress.Length == 0) return false;

            Task<bool> blocked = IsBlockedAsync(ownerAddress, otherAddress);
            Task<bool> blockedBy = IsBlockedAsync(otherAddress, ownerAddress);
            await Task.WhenAll(blocked, blockedBy);

            return await blocked || await blockedBy;
        }

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
                Kind = ReportKind.Post,
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
                Kind = ReportKind.Account,
                TargetAddress = targetAddress,
                Reason = reason,
                Detail = trimmedDetail,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await AppServices.Documents.WriteAsync(report.Id, report);
            MainEvents.Trigger(MainEvents.Names.ModerationChanged, targetAddress);
            return report;
        }

        /// <summary>
        /// Files a complaint about a reply, disclosing its text the same way a post report discloses a post's.
        /// </summary>
        /// <param name="reporter"> The unlocked account filing the complaint. </param>
        /// <param name="comment"> Reply being complained about; its text is what gets disclosed. </param>
        /// <param name="reason"> Category the reporter chose. </param>
        /// <param name="detail"> What the reporter wrote in their own words. </param>
        /// <returns> The stored report, or null when the detail was too long to store. </returns>
        public static async Task<ReportData?> ReportCommentAsync(
            PrivateIdentity reporter,
            CommentData comment,
            ReportReason reason,
            string detail = "")
        {
            if (TrimDetail(detail) is not string trimmedDetail) return null;

            ReportData report = new()
            {
                ReportId = CreateReportId(),
                ReporterAddress = reporter.Public.Address,
                Kind = ReportKind.Comment,
                TargetCommentId = comment.CommentId,
                TargetPostId = comment.PostId,
                TargetAddress = comment.AuthorAddress,
                Reason = reason,
                Detail = trimmedDetail,
                DisclosedContent = comment.Text,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await AppServices.Documents.WriteAsync(report.Id, report);
            MainEvents.Trigger(MainEvents.Names.ModerationChanged, comment.AuthorAddress);
            return report;
        }

        /// <summary>
        /// Files a complaint about a private message. The reporter passes the text they read, because there is no
        /// other way it could reach anybody: the message is encrypted to its recipient and the server holds only
        /// ciphertext it cannot open.
        /// </summary>
        /// <param name="reporter"> The unlocked account filing the complaint, who must be the one who received it. </param>
        /// <param name="message"> The envelope being complained about. </param>
        /// <param name="decryptedText"> What the reporter read, which is what they are choosing to disclose. </param>
        /// <param name="reason"> Category the reporter chose. </param>
        /// <param name="detail"> What the reporter wrote in their own words. </param>
        /// <returns> The stored report, or null when the reporter was not a party to the message or the detail was too long. </returns>
        /// <remarks>
        /// Only the recipient may report: the sender complaining about their own message would be disclosing
        /// somebody else's inbox, and nobody outside the pair holds anything to disclose in the first place.
        /// </remarks>
        public static async Task<ReportData?> ReportMessageAsync(
            PrivateIdentity reporter,
            MessageData message,
            string decryptedText,
            ReportReason reason,
            string detail = "")
        {
            if (message.RecipientAddress != reporter.Public.Address) return null;
            if (TrimDetail(detail) is not string trimmedDetail) return null;

            ReportData report = new()
            {
                ReportId = CreateReportId(),
                ReporterAddress = reporter.Public.Address,
                Kind = ReportKind.Message,
                TargetMessageId = message.MessageId,
                TargetAddress = message.SenderAddress,
                Reason = reason,
                Detail = trimmedDetail,
                DisclosedContent = decryptedText,
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await AppServices.Documents.WriteAsync(report.Id, report);
            MainEvents.Trigger(MainEvents.Names.ModerationChanged, message.SenderAddress);
            return report;
        }

        /// <summary> Reads what one account has reported, newest first, so they can see where each complaint got to. </summary>
        /// <param name="reporterAddress"> The account that filed them. </param>
        /// <param name="limit"> Largest number of reports to return. </param>
        /// <returns> That account's reports, newest first. </returns>
        public static async Task<IReadOnlyList<ReportData>> ReadFiledByAsync(string reporterAddress, int limit = ReportPageSize)
        {
            if (string.IsNullOrEmpty(reporterAddress) || limit <= 0) return [];

            DocumentQuery<ReportData> query = new DocumentQuery<ReportData>()
                .WithMatch(ReportData.ReporterField, reporterAddress)
                .WithSort(ReportData.CreatedAtField, descending: true)
                .WithLimit(limit);

            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary>
        /// Takes a complaint back. Only whoever filed it may, and only while it is still open — a report already
        /// acted on is a record of something that happened, not a draft.
        /// </summary>
        /// <param name="report"> The report to withdraw. </param>
        /// <param name="reporter"> Account asking; anybody else is refused. </param>
        /// <returns> True once it is withdrawn. </returns>
        public static async Task<bool> WithdrawAsync(ReportData report, PublicIdentity reporter)
        {
            if (report.ReporterAddress != reporter.Address) return false;
            if (report.Status != ReportStatus.Open) return false;

            ReportData withdrawn = report with
            {
                Status = ReportStatus.Withdrawn,
                ResolvedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),

                // The disclosure goes with it. Somebody taking a complaint back should not leave behind the
                // content they only handed over in order to make it.
                DisclosedContent = string.Empty
            };

            await AppServices.Documents.WriteAsync(withdrawn.Id, withdrawn);
            MainEvents.Trigger(MainEvents.Names.ModerationChanged, report.TargetAddress);
            return true;
        }

        /// <summary> Reports read back in one page. </summary>
        public const int ReportPageSize = 50;

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
