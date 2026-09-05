namespace ChaySocial.MainProject.Events
{
    /// <summary>
    /// The single channel every part of the app publishes changes on. Handlers live in a <see cref="HashSet{T}"/>,
    /// so subscribing the same method twice registers it once and it fires once.
    /// </summary>
    public static class MainEvents
    {
        static readonly HashSet<Action<string, object?>> Handlers = [];

        /// <summary> Every event name that may be passed to <see cref="Trigger"/>, so no caller types the name by hand. </summary>
        public static class Names
        {
            public const string ThemeChanged = nameof(ThemeChanged);

            /// <summary> An identity was created, unlocked or signed out. Payload is the active <see cref="Identity.PublicIdentity"/>, or null after signing out. </summary>
            public const string SessionChanged = nameof(SessionChanged);

            /// <summary> A post was published or removed. Payload is null; subscribers re-read the wall. </summary>
            public const string WallChanged = nameof(WallChanged);

            /// <summary> A profile was edited. Payload is the address whose profile changed. </summary>
            public const string ProfileChanged = nameof(ProfileChanged);

            /// <summary> One account started or stopped following another. Payload is the followee's address. </summary>
            public const string FollowChanged = nameof(FollowChanged);

            /// <summary> A comment was published or removed. Payload is the id of the post it belongs to. </summary>
            public const string CommentsChanged = nameof(CommentsChanged);

            /// <summary> A notification was created or marked read. Payload is the recipient's address. </summary>
            public const string NotificationsChanged = nameof(NotificationsChanged);

            /// <summary> A direct message was sent. Payload is the conversation id. </summary>
            public const string MessagesChanged = nameof(MessagesChanged);

            /// <summary> A block or a report was recorded. Payload is the address that was blocked or reported. </summary>
            public const string ModerationChanged = nameof(ModerationChanged);
            public const string GroupsChanged = nameof(GroupsChanged);
            public const string PagesChanged = nameof(PagesChanged);
            public const string MatchChanged = nameof(MatchChanged);

            /// <summary> Raised when the reader changes how far a line has to go before this device covers it. </summary>
            public const string ContentGuardChanged = nameof(ContentGuardChanged);

            /// <summary> Raised when the reader starts or stops following a subject. </summary>
            public const string SubjectFollowChanged = nameof(SubjectFollowChanged);
        }

        /// <summary> Registers a handler that receives every triggered event. </summary>
        /// <param name="handler"> Receives the event name and its payload. </param>
        /// <returns> True when it was added; false when the very same handler was already registered. </returns>
        public static bool Subscribe(Action<string, object?> handler) => Handlers.Add(handler);

        /// <summary> Stops a handler from receiving further events. </summary>
        /// <param name="handler"> The handler that was passed to <see cref="Subscribe"/>. </param>
        /// <returns> True when it was found and removed. </returns>
        public static bool Unsubscribe(Action<string, object?> handler) => Handlers.Remove(handler);

        /// <summary>
        /// Notifies every handler. Runs against a copy of the set so a handler may unsubscribe while the trigger is
        /// still running, and one handler that throws does not stop the rest from being notified.
        /// </summary>
        /// <param name="eventName"> One of <see cref="Names"/>. </param>
        /// <param name="data"> Payload described by that name, or null. </param>
        public static void Trigger(string eventName, object? data = null)
        {
            foreach (Action<string, object?> handler in Handlers.ToArray())
            {
                try
                {
                    handler(eventName, data);
                }
                catch (Exception error)
                {
                    Log($"Handler '{handler.Method.DeclaringType?.Name}.{handler.Method.Name}' threw on '{eventName}'.\n{error}", LogLevel.Error);
                }
            }
        }
    }
}
