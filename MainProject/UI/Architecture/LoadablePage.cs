using ChaySocial.MainProject.Events;

namespace ChaySocial.MainProject.UI.Layout.Architecture
{
    /// <summary>
    /// A page that fetches something before it can draw. Owns the three states every such page has — loading,
    /// failed, ready — so no page re-implements them, and re-runs its own load when one of the events it named
    /// fires. Subclasses supply only <see cref="LoadAsync"/> and, optionally, which events should reload them.
    /// </summary>
    public abstract class LoadablePage : PageBase, IDisposable
    {
        /// <summary> True while <see cref="LoadAsync"/> is running and the page has nothing to show yet. </summary>
        protected bool IsLoading { get; private set; } = true;

        /// <summary> Message shown instead of the page's content when the last load threw; null when it succeeded. </summary>
        protected string? LoadFailureMessage { get; private set; }

        /// <summary> Shown when a load fails, so a page does not have to phrase this itself. </summary>
        protected const string DefaultLoadFailureMessage = "We couldn't load this. Give it another try?";

        /// <summary>
        /// Names from <see cref="MainEvents.Names"/> that should make this page reload. Empty means the page loads
        /// once and stays as it is.
        /// </summary>
        protected virtual string[] ReloadOnEvents => [];

        /// <summary> Fetches what the page needs. Called on first render and again on every event named in <see cref="ReloadOnEvents"/>. </summary>
        protected abstract Task LoadAsync();

        protected override async Task OnInitializedAsync()
        {
            if (ReloadOnEvents.Length > 0) MainEvents.Subscribe(HandleAppEvent);
            await ReloadAsync();
        }

        /// <summary> Runs <see cref="LoadAsync"/> again, showing the spinner while it works and the failure line if it throws. </summary>
        protected async Task ReloadAsync()
        {
            IsLoading = true;
            LoadFailureMessage = null;
            StateHasChanged();

            try
            {
                await LoadAsync();
            }
            catch (Exception error)
            {
                LoadFailureMessage = DefaultLoadFailureMessage;
                Log($"{GetType().Name} failed to load.\n{error}", LogLevel.Error);
            }
            finally
            {
                IsLoading = false;
                StateHasChanged();
            }
        }

        /// <summary> Reloads when one of this page's declared events fires. </summary>
        /// <param name="eventName"> Name of the event that fired. </param>
        /// <param name="data"> Payload the event carried; unused here because a reload re-reads everything anyway. </param>
        void HandleAppEvent(string eventName, object? data)
        {
            if (!ReloadOnEvents.Contains(eventName)) return;

            InvokeAsync(ReloadAsync);
        }

        public virtual void Dispose()
        {
            if (ReloadOnEvents.Length > 0) MainEvents.Unsubscribe(HandleAppEvent);
            GC.SuppressFinalize(this);
        }
    }
}
