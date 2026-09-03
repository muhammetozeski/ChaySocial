namespace Groundwork.Observability
{
    /// <summary>
    /// Observable signal that carries no data — "the theme changed", "the session was locked". Replaces a public
    /// <c>event Action</c> field: the same handler can never be registered twice, callers cannot raise the event
    /// from outside, and a handler that throws does not stop the others.
    /// </summary>
    /// <param name="reportSubscriberFailures"> See <see cref="EventChannelBase{TSubscriber}(bool)"/>. </param>
    public sealed class EventChannel(bool reportSubscriberFailures = true)
        : EventChannelBase<Action>(reportSubscriberFailures)
    {
        /// <summary> Notifies every subscriber. </summary>
        public void Publish() => Dispatch(static subscriber => subscriber());
    }

    /// <summary>
    /// Observable signal that carries one payload — the posted item, the new balance, the unlocked identity.
    /// Same guarantees as the parameterless <see cref="EventChannel"/>.
    /// </summary>
    /// <typeparam name="TPayload"> What each publish hands to its subscribers. </typeparam>
    /// <param name="reportSubscriberFailures"> See <see cref="EventChannelBase{TSubscriber}(bool)"/>. </param>
    public sealed class EventChannel<TPayload>(bool reportSubscriberFailures = true)
        : EventChannelBase<Action<TPayload>>(reportSubscriberFailures)
    {
        /// <summary> Notifies every subscriber with <paramref name="payload"/>. </summary>
        /// <param name="payload"> Value handed to each subscriber. </param>
        public void Publish(TPayload payload) => Dispatch(subscriber => subscriber(payload));
    }

    /// <summary>
    /// Observable signal whose subscribers do awaitable work — writing to a store, re-fetching a feed. Subscribers
    /// run in sequence and <see cref="PublishAsync"/> completes only after the last one settles, so a caller can
    /// await the full fan-out instead of firing and forgetting.
    /// </summary>
    /// <typeparam name="TPayload"> What each publish hands to its subscribers. </typeparam>
    /// <param name="reportSubscriberFailures"> See <see cref="EventChannelBase{TSubscriber}(bool)"/>. </param>
    public sealed class AsyncEventChannel<TPayload>(bool reportSubscriberFailures = true)
        : EventChannelBase<Func<TPayload, CancellationToken, Task>>(reportSubscriberFailures)
    {
        /// <summary> Notifies every subscriber with <paramref name="payload"/> and waits for all of them. </summary>
        /// <param name="payload"> Value handed to each subscriber. </param>
        /// <param name="cancellationToken"> Passed straight through to every subscriber. </param>
        /// <returns> A task that completes once every subscriber has finished or failed. </returns>
        public Task PublishAsync(TPayload payload, CancellationToken cancellationToken = default)
            => DispatchAsync(subscriber => subscriber(payload, cancellationToken));
    }
}

