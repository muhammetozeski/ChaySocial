namespace Groundwork.Observability
{
    /// <summary>
    /// The token <see cref="EventChannelBase{TSubscriber}.SubscribeScoped"/> hands back. Disposing it unsubscribes,
    /// and disposing it again does nothing, so a component's <c>Dispose</c> stays safe to call twice.
    /// </summary>
    /// <param name="unsubscribe"> Removes the handler this token stands for. </param>
    public sealed class EventSubscription(Action unsubscribe) : IDisposable
    {
        Action? _unsubscribe = unsubscribe;

        public void Dispose()
        {
            Action? pending = Interlocked.Exchange(ref _unsubscribe, null);
            pending?.Invoke();
        }
    }
}

