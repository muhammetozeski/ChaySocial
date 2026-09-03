using Groundwork.Diagnostics;

namespace Groundwork.Observability
{
    /// <summary>
    /// Subscriber storage every event channel shares. Handlers live in a <see cref="HashSet{T}"/>, so subscribing
    /// the same handler twice registers it once and it fires once — the duplicate-subscription bug that plain
    /// <c>event Action</c> fields allow cannot happen here.
    /// </summary>
    /// <typeparam name="TSubscriber"> Delegate shape the concrete channel accepts, e.g. <c>Action</c> or <c>Action&lt;TPayload&gt;</c>. </typeparam>
    public abstract class EventChannelBase<TSubscriber> where TSubscriber : Delegate
    {
        readonly HashSet<TSubscriber> _subscribers = [];
        readonly Lock _gate = new();
        readonly bool _reportSubscriberFailures;

        /// <param name="reportSubscriberFailures">
        /// True writes a failing handler into <see cref="DiagnosticLog"/>. Pass false only for the channel that
        /// backs <see cref="DiagnosticLog"/> itself, which would otherwise re-enter its own reporting path;
        /// that channel falls back to <see cref="System.Diagnostics.Debug"/>.
        /// </param>
        protected EventChannelBase(bool reportSubscriberFailures = true)
            => _reportSubscriberFailures = reportSubscriberFailures;

        /// <summary> Handlers registered right now. </summary>
        public int SubscriberCount
        {
            get { lock (_gate) return _subscribers.Count; }
        }

        /// <summary> Registers a handler. </summary>
        /// <param name="subscriber"> Handler to invoke on every publish. </param>
        /// <returns> True when it was added; false when the very same handler was already registered. </returns>
        public bool Subscribe(TSubscriber subscriber)
        {
            ArgumentNullException.ThrowIfNull(subscriber);
            lock (_gate) return _subscribers.Add(subscriber);
        }

        /// <summary> Removes a handler so it stops receiving publishes. </summary>
        /// <param name="subscriber"> The handler that was passed to <see cref="Subscribe"/>. </param>
        /// <returns> True when it was found and removed; false when it was not registered. </returns>
        public bool Unsubscribe(TSubscriber subscriber)
        {
            ArgumentNullException.ThrowIfNull(subscriber);
            lock (_gate) return _subscribers.Remove(subscriber);
        }

        /// <summary>
        /// Registers a handler and hands back the token that removes it again. Lets a component subscribe in
        /// <c>OnInitialized</c> and drop the token in <c>Dispose</c> without keeping the delegate in a field.
        /// </summary>
        /// <param name="subscriber"> Handler to invoke on every publish. </param>
        /// <returns> Token whose <see cref="IDisposable.Dispose"/> unsubscribes the handler. </returns>
        public IDisposable SubscribeScoped(TSubscriber subscriber)
        {
            Subscribe(subscriber);
            return new EventSubscription(() => Unsubscribe(subscriber));
        }

        /// <summary> Drops every handler at once — used when a whole screen or session is torn down. </summary>
        public void Clear()
        {
            lock (_gate) _subscribers.Clear();
        }

        /// <summary>
        /// Runs <paramref name="invoke"/> against a snapshot of the subscribers, so a handler may subscribe or
        /// unsubscribe while the publish is still running. One failing handler is reported and skipped; the
        /// remaining handlers still receive the publish.
        /// </summary>
        /// <param name="invoke"> Calls a single subscriber with the payload the concrete channel carries. </param>
        protected void Dispatch(Action<TSubscriber> invoke)
        {
            TSubscriber[] snapshot;
            lock (_gate) snapshot = [.. _subscribers];

            foreach (TSubscriber subscriber in snapshot)
            {
                try
                {
                    invoke(subscriber);
                }
                catch (Exception error)
                {
                    ReportFailure(subscriber, error);
                }
            }
        }

        /// <summary>
        /// Same contract as <see cref="Dispatch"/> for handlers that return a <see cref="Task"/>. Handlers run one
        /// after another so a publish keeps its declared ordering guarantee, and each awaits inside its own guard.
        /// </summary>
        /// <param name="invokeAsync"> Calls a single subscriber and returns the task it started. </param>
        /// <returns> A task that completes once every subscriber has finished or failed. </returns>
        protected async Task DispatchAsync(Func<TSubscriber, Task> invokeAsync)
        {
            TSubscriber[] snapshot;
            lock (_gate) snapshot = [.. _subscribers];

            foreach (TSubscriber subscriber in snapshot)
            {
                try
                {
                    await invokeAsync(subscriber).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    ReportFailure(subscriber, error);
                }
            }
        }

        /// <summary> Records a handler that threw, either through <see cref="DiagnosticLog"/> or, for the log's own channel, straight to the debugger. </summary>
        /// <param name="subscriber"> The handler that threw. </param>
        /// <param name="error"> The exception it threw. </param>
        void ReportFailure(TSubscriber subscriber, Exception error)
        {
            string description = $"Subscriber '{subscriber.Method.DeclaringType?.Name}.{subscriber.Method.Name}' threw during publish.";

            if (_reportSubscriberFailures)
            {
                DiagnosticLog.Write(DiagnosticSeverity.Error, GetType().Name, description, error);
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Error] {GetType().Name}: {description} -> {error}");
        }
    }
}

