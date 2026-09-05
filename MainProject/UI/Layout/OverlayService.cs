using ChaySocial.MainProject.Events;
using Microsoft.AspNetCore.Components;

namespace ChaySocial.MainProject.UI.Layout
{
    /// <summary>
    /// Where anything that has to cover the screen is drawn from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CSS gives an element that carries <c>backdrop-filter</c>, <c>filter</c> or <c>transform</c> the job of being
    /// the containing block for every <c>position: fixed</c> element inside it. Every card in this app is frosted,
    /// so a full-screen panel opened from inside one is not full-screen at all: it is trapped in the card's own
    /// rectangle. No rule written on the panel can escape that — it has to be drawn somewhere else in the document.
    /// </para>
    /// <para>
    /// So a component that opens a panel keeps writing it exactly where it always did, wrapped in an
    /// <c>AppOverlay</c>, and this hands the markup to the one host that sits outside every frosted card. The
    /// panel's buttons still belong to the component that wrote them: only where the browser draws it changes.
    /// </para>
    /// </remarks>
    public static class OverlayService
    {
        /// <summary> Panels currently offered, in the order their components registered them. </summary>
        static readonly List<(object Owner, RenderFragment Content)> Offered = [];

        /// <summary> Everything the host should draw. </summary>
        public static IReadOnlyList<RenderFragment> Panels => [.. Offered.Select(entry => entry.Content)];

        /// <summary>
        /// Hands the host a component's panel, replacing whatever that same component offered before. Called on
        /// every render of the component, because the markup carries its state and yesterday's copy of it is wrong.
        /// </summary>
        /// <param name="owner"> The component the panel belongs to. </param>
        /// <param name="content"> The markup to draw. </param>
        public static void Offer(object owner, RenderFragment content)
        {
            int existing = IndexOf(owner);

            if (existing < 0) Offered.Add((owner, content));
            else Offered[existing] = (owner, content);

            MainEvents.Trigger(MainEvents.Names.OverlayChanged);
        }

        /// <summary> Takes a component's panel back, for when it is disposed. </summary>
        /// <param name="owner"> The component whose panel should go. </param>
        public static void Withdraw(object owner)
        {
            int existing = IndexOf(owner);
            if (existing < 0) return;

            Offered.RemoveAt(existing);
            MainEvents.Trigger(MainEvents.Names.OverlayChanged);
        }

        /// <summary> Finds where a component's panel sits, or -1 when it has offered none. </summary>
        /// <param name="owner"> The component to look for. </param>
        /// <returns> Its position, or -1. </returns>
        static int IndexOf(object owner) => Offered.FindIndex(entry => ReferenceEquals(entry.Owner, owner));
    }
}
