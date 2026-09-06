using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.DataModels
{
    /// <summary> What happened to make a notification appear. </summary>
    public enum NotificationKind
    {
        /// <summary> Someone liked one of the recipient's posts. </summary>
        Like,

        /// <summary> Someone commented on one of the recipient's posts. </summary>
        Comment,

        /// <summary> Someone started following the recipient. </summary>
        Follow,

        /// <summary> Someone sent the recipient a direct message. </summary>
        Message,

        /// <summary> Someone named the recipient in something they wrote. </summary>
        Mention
    }

    /// <summary>
    /// One line in an account's alerts list. Carries who did it and what it points at, never the content itself —
    /// a message notification in particular has an empty preview, because a preview would hand the server the
    /// message body it is not supposed to be able to read.
    /// </summary>
    public sealed record NotificationData : IStoredDocument<NotificationData>
    {
        public static string CollectionName => "notifications";

        /// <summary> Id this notification is stored under. </summary>
        public required string NotificationId { get; init; }

        /// <summary> Account this notification is for. </summary>
        public required string RecipientAddress { get; init; }

        /// <summary> Account whose action caused it. </summary>
        public required string ActorAddress { get; init; }

        /// <summary> What the actor did. </summary>
        public required NotificationKind Kind { get; init; }

        /// <summary> Post or comment the notification points at; empty for a follow. </summary>
        public string TargetId { get; init; } = string.Empty;

        /// <summary> Short excerpt shown in the alerts list. Always empty for <see cref="NotificationKind.Message"/>. </summary>
        public string Preview { get; init; } = string.Empty;

        /// <summary>
        /// Base64 value the recipient decapsulates to recover the secret <see cref="SealedDetail"/> was sealed
        /// with; empty on an alert that carries nothing sealed.
        /// </summary>
        public string Encapsulation { get; init; } = string.Empty;

        /// <summary> Base64 nonce <see cref="SealedDetail"/> was sealed under; empty when nothing is sealed. </summary>
        public string Nonce { get; init; } = string.Empty;

        /// <summary>
        /// Who acted and what they acted on, sealed to the recipient alone.
        /// </summary>
        /// <remarks>
        /// A private message's alert used to name its sender and its conversation in the clear, in a collection
        /// that exists only to ring a bell — a second, plain-text copy of the private-message graph the encryption
        /// beside it was there to prevent. Sealed, the alert still rings and still opens the right conversation,
        /// and the collection says nothing about who wrote to whom.
        /// </remarks>
        public string SealedDetail { get; init; } = string.Empty;

        /// <summary> True when the actor and target of this alert are sealed rather than written in the clear. </summary>
        public bool IsSealed => SealedDetail.Length > 0;

        /// <summary> When it happened. </summary>
        public required long CreatedAtUnixMs { get; init; }

        /// <summary> False until the recipient opens it. </summary>
        public bool IsRead { get; init; }

        /// <summary> Longest excerpt kept, so one long post cannot bloat an alerts page. </summary>
        public const int MaximumPreviewLength = 80;

        /// <summary> Id this notification is stored under. </summary>
        public DocumentId<NotificationData> Id => new(NotificationId);

        /// <summary> Recipient address, for reading one account's alerts. </summary>
        public static readonly DocumentField<NotificationData> RecipientField = new(nameof(RecipientAddress), notification => notification.RecipientAddress);

        /// <summary> Creation time, for showing newest alerts first. </summary>
        public static readonly DocumentField<NotificationData> CreatedAtField = new(nameof(CreatedAtUnixMs), notification => notification.CreatedAtUnixMs);

        /// <summary> Read flag, for counting what is still unread. </summary>
        public static readonly DocumentField<NotificationData> IsReadField = new(nameof(IsRead), notification => notification.IsRead);
    }
}
