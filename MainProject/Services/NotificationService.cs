using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary> What an alert kept sealed: who acted, what they acted on, and the thing it announced. </summary>
    /// <param name="ActorAddress"> Account whose action caused the alert. </param>
    /// <param name="TargetId"> What the alert points at — a conversation, for a letter. </param>
    /// <param name="DetailId"> The thing announced, so an alert can be found again by it; a message's own id. </param>
    public readonly record struct SealedAlertDetail(string ActorAddress, string TargetId, string DetailId);

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
        /// <param name="sealTo"> Account to seal the actor and target to, so the stored alert names neither; null leaves them in the clear. </param>
        /// <param name="sealedDetailId"> A second id kept inside the seal, so an alert can be found again by what it announced. </param>
        /// <returns> The stored notification, or null when it was for the actor themselves or an address was missing. </returns>
        public static async Task<NotificationData?> NotifyAsync(
            string recipientAddress,
            string actorAddress,
            NotificationKind kind,
            string targetId = "",
            string preview = "",
            PublicIdentity? sealTo = null,
            string sealedDetailId = "")
        {
            if (recipientAddress.Length == 0 || actorAddress.Length == 0) return null;
            if (recipientAddress == actorAddress) return null;

            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SealedFields sealed_ = sealTo is null
                ? new SealedFields(string.Empty, string.Empty, string.Empty)
                : Seal(sealTo, recipientAddress, createdAt, actorAddress, targetId, sealedDetailId);

            NotificationData notification = new()
            {
                NotificationId = Base32.Encode(RandomSource.Next(NotificationIdBytes)),
                RecipientAddress = recipientAddress,

                // Sealed, these two are left empty rather than written twice: the collection exists to ring a bell,
                // and a bell does not need to know who rang it.
                ActorAddress = sealTo is null ? actorAddress : string.Empty,
                Kind = kind,
                TargetId = sealTo is null ? targetId : string.Empty,

                Encapsulation = sealed_.Encapsulation,
                Nonce = sealed_.Nonce,
                SealedDetail = sealed_.Detail,

                // A message body is encrypted for the recipient alone; putting an excerpt here would hand the
                // server the very text it is not supposed to be able to read.
                Preview = kind == NotificationKind.Message ? string.Empty : ShortenPreview(preview),
                CreatedAtUnixMs = createdAt
            };

            await AppServices.Documents.WriteAsync(notification.Id, notification);
            MainEvents.Trigger(MainEvents.Names.NotificationsChanged, recipientAddress);
            return notification;
        }

        /// <summary>
        /// Opens what an alert was sealed with. Only the account the alert belongs to can do it, because the secret
        /// was encapsulated to that account's key.
        /// </summary>
        /// <param name="reader"> The unlocked account whose alerts these are. </param>
        /// <param name="notification"> The alert to open. </param>
        /// <param name="detail"> Receives who acted and what they acted on, or empty values when it could not be opened. </param>
        /// <returns> True when the alert carried a seal this account could open. </returns>
        /// <remarks>
        /// A seal that will not open is an ordinary outcome — an alert written for somebody else, or one whose
        /// stored fields were altered — so it is reported as false rather than thrown. The alerts screen draws the
        /// padlocked line it already draws for a letter it cannot read.
        /// </remarks>
        public static bool TryOpenSealed(PrivateIdentity reader, NotificationData notification, out SealedAlertDetail detail)
        {
            detail = new SealedAlertDetail(string.Empty, string.Empty, string.Empty);

            if (!notification.IsSealed || notification.RecipientAddress != reader.Public.Address) return false;

            try
            {
                byte[] sharedSecret = reader.Decapsulate(Convert.FromBase64String(notification.Encapsulation));
                byte[] associatedData = BuildSealAssociatedData(notification.RecipientAddress, notification.CreatedAtUnixMs);

                if (!AppCryptography.Cipher.TryDecrypt(
                    Convert.FromBase64String(notification.SealedDetail),
                    sharedSecret,
                    Convert.FromBase64String(notification.Nonce),
                    associatedData,
                    out byte[] plaintext))
                {
                    return false;
                }

                TranscriptReader fields = new(plaintext);
                if (!fields.TryReadText(out string actorAddress)
                    || !fields.TryReadText(out string targetId)
                    || !fields.TryReadText(out string detailId))
                {
                    return false;
                }

                detail = new SealedAlertDetail(actorAddress, targetId, detailId);
                return true;
            }
            catch (FormatException error)
            {
                Log($"Alert '{notification.NotificationId}' carries malformed base64.\n{error}", LogLevel.Warning);
                return false;
            }
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

        /// <summary> Separates an alert's seal from every other thing this app encrypts. </summary>
        static readonly byte[] AlertSealDomain = "ChaySocial/Alert/v1"u8.ToArray();

        /// <summary> The three stored values a seal produces, kept together so the caller cannot pair them wrongly. </summary>
        /// <param name="Encapsulation"> Base64 value the recipient decapsulates. </param>
        /// <param name="Nonce"> Base64 nonce the detail was sealed under. </param>
        /// <param name="Detail"> Base64 sealed bytes. </param>
        readonly record struct SealedFields(string Encapsulation, string Nonce, string Detail);

        /// <summary> Seals who acted and what they acted on to the account the alert belongs to. </summary>
        /// <param name="sealTo"> The recipient's published identity. </param>
        /// <param name="recipientAddress"> Address of that account, bound into the seal. </param>
        /// <param name="createdAtUnixMs"> When the alert was written, bound into the seal too. </param>
        /// <param name="actorAddress"> Account whose action caused the alert. </param>
        /// <param name="targetId"> What the alert points at. </param>
        /// <param name="detailId"> The second id kept inside the seal. </param>
        /// <returns> What to store. </returns>
        /// <remarks>
        /// The recipient's address and the alert's time are the associated data rather than part of the body: they
        /// are already stored in the clear, and binding them means a sealed detail cannot be lifted onto a
        /// different alert without the tag failing.
        /// </remarks>
        static SealedFields Seal(
            PublicIdentity sealTo,
            string recipientAddress,
            long createdAtUnixMs,
            string actorAddress,
            string targetId,
            string detailId)
        {
            TranscriptWriter body = new();
            body.WriteText(actorAddress);
            body.WriteText(targetId);
            body.WriteText(detailId);

            EncapsulationResult secret = AppCryptography.Identities.EncapsulateTo(sealTo);
            byte[] nonce = RandomSource.Next(AppCryptography.Cipher.NonceSize);

            byte[] sealedDetail = AppCryptography.Cipher.Encrypt(
                body.ToArray(),
                secret.SharedSecret,
                nonce,
                BuildSealAssociatedData(recipientAddress, createdAtUnixMs));

            return new SealedFields(
                Convert.ToBase64String(secret.Encapsulation),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(sealedDetail));
        }

        /// <summary> Builds the bytes an alert's seal is bound to. </summary>
        /// <param name="recipientAddress"> Account the alert belongs to. </param>
        /// <param name="createdAtUnixMs"> When it was written. </param>
        /// <returns> The associated data both sealing and opening use. </returns>
        static byte[] BuildSealAssociatedData(string recipientAddress, long createdAtUnixMs)
        {
            TranscriptWriter associatedData = new();
            associatedData.WriteBytes(AlertSealDomain);
            associatedData.WriteText(recipientAddress);
            associatedData.WriteInt64(createdAtUnixMs);
            return associatedData.ToArray();
        }

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
