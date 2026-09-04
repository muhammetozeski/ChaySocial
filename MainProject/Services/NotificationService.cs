using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// The alerts list behind the bell. A notification is a pointer, not a copy: it names who acted and what they
    /// acted on, so opening one sends the reader back to the post, comment or conversation itself rather than to a
    /// second stored version of it that could drift out of step.
    /// </summary>
    public static class NotificationService
    {
        /// <summary> Alerts fetched in one page. </summary>
        public const int NotificationPageSize = 30;

        /// <summary>
        /// Records that someone acted on an account, unless that someone is the account itself — an owner liking
        /// their own post should not light up their own bell.
        /// </summary>
        /// <param name="recipientAddress"> Account that should see the alert. </param>
        /// <param name="actorAddress"> Account whose action caused it. </param>
        /// <param name="kind"> What the actor did. </param>
        /// <param name="targetId"> Post or comment the alert points at; left empty for a follow. </param>
        /// <param name="preview"> Excerpt to show, shortened to <see cref="NotificationData.MaximumPreviewLength"/> and dropped entirely for a message. </param>
        /// <returns> The stored notification, or null when it was for the actor themselves or an address was missing. </returns>
        public static async Task<NotificationData?> NotifyAsync(
            string recipientAddress,
            string actorAddress,
            NotificationKind kind,
            string targetId = "",
            string preview = "")
        {
            if (recipientAddress.Length == 0 || actorAddress.Length == 0) return null;
            if (recipientAddress == actorAddress) return null;

            NotificationData notification = new()
            {
                NotificationId = Base32.Encode(RandomSource.Next(NotificationIdBytes)),
                RecipientAddress = recipientAddress,
                ActorAddress = actorAddress,
                Kind = kind,
                TargetId = targetId,

                // A message body is encrypted for the recipient alone; putting an excerpt here would hand the
                // server the very text it is not supposed to be able to read.
                Preview = kind == NotificationKind.Message ? string.Empty : ShortenPreview(preview),
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await AppServices.Documents.WriteAsync(notification.Id, notification);
            MainEvents.Trigger(MainEvents.Names.NotificationsChanged, recipientAddress);
            return notification;
        }

        /// <summary>
        /// Tells everyone a line names that they were named. It is deliberately quiet about failure: being named is
        /// worth an alert, but never worth losing the post or comment that did the naming, so a write that fails is
        /// logged and the rest still go out.
        /// </summary>
        /// <param name="text"> The line as it was written; the addresses in it are what decide who hears. </param>
        /// <param name="actorAddress"> Account that wrote it. </param>
        /// <param name="targetId"> Post the alert should open. </param>
        /// <param name="preview"> Excerpt to show beside the alert. </param>
        /// <param name="alreadyTold"> Accounts that have already been alerted about this same line, so nobody is told twice. </param>
        /// <returns> A task that completes once everyone named has been told. </returns>
        public static async Task NotifyMentionedAsync(
            string text,
            string actorAddress,
            string targetId,
            string preview,
            IReadOnlySet<string>? alreadyTold = null)
        {
            foreach (string address in WrittenText.AccountsIn(text))
            {
                if (alreadyTold is not null && alreadyTold.Contains(address)) continue;

                try
                {
                    await NotifyAsync(address, actorAddress, NotificationKind.Mention, targetId, preview);
                }
                catch (Exception error)
                {
                    Log($"'{address}' could not be told they were named.\n{error}", LogLevel.Warning);
                }
            }
        }

        /// <summary> Reads one account's alerts, newest first. </summary>
        /// <param name="recipientAddress"> Account whose alerts to read. </param>
        /// <param name="limit"> Largest number of alerts to return. </param>
        /// <returns> That account's alerts, newest first; empty when the address is missing. </returns>
        public static async Task<IReadOnlyList<NotificationData>> ReadForAsync(string recipientAddress, int limit = NotificationPageSize)
        {
            if (recipientAddress.Length == 0) return [];

            DocumentQuery<NotificationData> query = BuildRecipientQuery(recipientAddress).WithLimit(limit);
            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary>
        /// Counts the alerts an account has not opened yet — the number the bell wears.
        /// </summary>
        /// <param name="recipientAddress"> Account to count for. </param>
        /// <returns> How many of that account's alerts are still unread. </returns>
        public static async Task<int> CountUnreadAsync(string recipientAddress)
            => (await ReadEveryForAsync(recipientAddress)).Count(notification => !notification.IsRead);

        /// <summary> Marks one alert as opened. An alert that was already read is left untouched. </summary>
        /// <param name="notification"> Alert the recipient opened. </param>
        /// <returns> The read alert: the stored replacement, or the original when it was already read. </returns>
        public static async Task<NotificationData> MarkReadAsync(NotificationData notification)
        {
            if (notification.IsRead) return notification;

            NotificationData opened = notification with { IsRead = true };

            await AppServices.Documents.WriteAsync(opened.Id, opened);
            MainEvents.Trigger(MainEvents.Names.NotificationsChanged, opened.RecipientAddress);
            return opened;
        }

        /// <summary> Marks every one of an account's alerts as opened, for the "clear all" the bell offers. </summary>
        /// <param name="recipientAddress"> Account whose alerts to clear. </param>
        /// <returns> How many alerts were actually changed; zero when there was nothing unread. </returns>
        public static async Task<int> MarkAllReadAsync(string recipientAddress)
        {
            List<NotificationData> unread = [.. (await ReadEveryForAsync(recipientAddress)).Where(notification => !notification.IsRead)];
            if (unread.Count == 0) return 0;

            foreach (NotificationData notification in unread)
            {
                NotificationData opened = notification with { IsRead = true };
                await AppServices.Documents.WriteAsync(opened.Id, opened);
            }

            MainEvents.Trigger(MainEvents.Names.NotificationsChanged, recipientAddress);
            return unread.Count;
        }

        /// <summary> Removes one alert from its recipient's list. </summary>
        /// <param name="notification"> Alert to remove. </param>
        public static async Task DeleteAsync(NotificationData notification)
        {
            await AppServices.Documents.DeleteAsync(notification.Id);
            MainEvents.Trigger(MainEvents.Names.NotificationsChanged, notification.RecipientAddress);
        }

        /// <summary> Random bytes behind a notification id — enough that two alerts never collide. </summary>
        const int NotificationIdBytes = 12;

        /// <summary>
        /// How many pages counting and clearing may walk through. Both of those have to see every alert an account
        /// holds, and this stops a store that keeps handing back a cursor from spinning the loop forever.
        /// </summary>
        const int MaximumPagesWalked = 20;

        /// <summary> Marks an excerpt that had to be cut short. </summary>
        const string PreviewEllipsis = "…";

        /// <summary> Builds the read that every alerts query starts from: one account's alerts, newest first. </summary>
        /// <param name="recipientAddress"> Account whose alerts to read. </param>
        /// <returns> The query, still without a limit or cursor. </returns>
        static DocumentQuery<NotificationData> BuildRecipientQuery(string recipientAddress)
            => new DocumentQuery<NotificationData>()
                .WithMatch(NotificationData.RecipientField, recipientAddress)
                .WithSort(NotificationData.CreatedAtField, descending: true);

        /// <summary>
        /// Walks every page of one account's alerts. Counting the unread ones and clearing them both need the whole
        /// list rather than the first page, so the walk follows the cursor until the store runs out of pages.
        /// </summary>
        /// <param name="recipientAddress"> Account whose alerts to walk. </param>
        /// <returns> Every alert that account holds, newest first; empty when the address is missing. </returns>
        static async Task<List<NotificationData>> ReadEveryForAsync(string recipientAddress)
        {
            if (recipientAddress.Length == 0) return [];

            List<NotificationData> collected = [];
            string? cursor = null;
            int pagesWalked = 0;

            do
            {
                DocumentQuery<NotificationData> query = BuildRecipientQuery(recipientAddress)
                    .WithLimit(NotificationPageSize)
                    .WithCursor(cursor);

                DocumentPage<NotificationData> page = await AppServices.Documents.QueryAsync(query);

                collected.AddRange(page.Documents);
                cursor = page.NextCursor;
                pagesWalked++;
            }
            while (cursor is not null && pagesWalked < MaximumPagesWalked);

            return collected;
        }

        /// <summary> Cuts an excerpt down to what the alerts list can show. </summary>
        /// <param name="preview"> Text the caller offered. </param>
        /// <returns> The trimmed text, shortened with an ellipsis when it was too long. </returns>
        static string ShortenPreview(string preview)
        {
            string trimmed = preview.Trim();
            if (trimmed.Length <= NotificationData.MaximumPreviewLength) return trimmed;

            int cut = NotificationData.MaximumPreviewLength - PreviewEllipsis.Length;

            // A cut counts UTF-16 units, so it can land between the two halves of an emoji. Dropping the orphaned
            // half keeps the excerpt from ending in a replacement box.
            if (char.IsHighSurrogate(trimmed[cut - 1])) cut--;

            return string.Concat(trimmed.AsSpan(0, cut), PreviewEllipsis);
        }
    }
}
