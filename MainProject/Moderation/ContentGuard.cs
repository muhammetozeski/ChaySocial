using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Services;

namespace ChaySocial.MainProject.Moderation
{
    /// <summary>
    /// The one thing a reader decides for themselves: how far a line has to go before this device draws a curtain
    /// over it. The setting lives on the device and is never sent anywhere, so nobody — least of all whoever runs a
    /// server — knows or controls what any reader chose.
    /// </summary>
    /// <remarks>
    /// A curtain is not a deletion and not a block. The line is fetched, kept and drawn; what changes is that the
    /// reader is told what it was read as and gets to decide whether to look. The writer is never told, never
    /// throttled and never marked, because a reader tidying their own feed is not a punishment to hand out.
    /// </remarks>
    public static class ContentGuard
    {
        /// <summary>
        /// The lowest band this device covers. Someone who never opens Settings gets the top band only: threats and
        /// the heaviest attacks, the one place almost nobody wants to be surprised. Everything short of that arrives
        /// exactly as it was written, which is what keeps the platform feeling as free as it claims to be.
        /// </summary>
        public static ContentBand Covered { get; private set; } = ContentBand.Extreme;

        /// <summary> True when this device covers nothing at all. </summary>
        public static bool IsOff => Covered == ContentBand.Nothing;

        /// <summary> The bands a reader can choose between, from covering nothing to covering the most. </summary>
        public static IReadOnlyList<ContentBand> Choices { get; } =
            [ContentBand.Nothing, ContentBand.Extreme, ContentBand.Abusive, ContentBand.Coarse];

        /// <summary> What a reader is told each choice does, in the order of <see cref="Choices"/>. </summary>
        /// <param name="band"> The band being described. </param>
        /// <returns> A short English line for the settings screen. </returns>
        public static string Describe(ContentBand band) => band switch
        {
            ContentBand.Nothing => "Show me everything, uncovered",
            ContentBand.Extreme => "Cover threats and the heaviest attacks",
            ContentBand.Abusive => "Also cover anything aimed at a person",
            ContentBand.Coarse => "Also cover swearing",
            _ => string.Empty
        };

        /// <summary> Changes what this device covers and tells the tree to redraw. </summary>
        /// <param name="band"> The lowest band to cover, or <see cref="ContentBand.Nothing"/> to cover none. </param>
        public static void Apply(ContentBand band)
        {
            if (band == Covered) return;

            Covered = band;
            MainEvents.Trigger(MainEvents.Names.ContentGuardChanged, band);
        }

        /// <summary> Changes the setting and writes it to the device, so the next visit opens the same way. </summary>
        /// <param name="band"> The lowest band to cover. </param>
        /// <returns> A task that completes once the choice has been stored. </returns>
        public static async Task ApplyAndRememberAsync(ContentBand band)
        {
            Apply(band);

            if (AppServices.LocalStore is null) return;

            await AppServices.LocalStore.WriteAsync(LocalStoreKeys.ContentGuard, band.ToString());
        }

        /// <summary>
        /// Puts back what this device last chose. Called once at startup; leaves the default in place when nothing
        /// was stored or the stored value no longer names a band.
        /// </summary>
        /// <returns> A task that completes once the stored choice has been read and applied. </returns>
        public static async Task RestoreAsync()
        {
            if (AppServices.LocalStore is null) return;

            string? stored = await AppServices.LocalStore.ReadAsync(LocalStoreKeys.ContentGuard);
            if (stored is null) return;

            if (Enum.TryParse(stored, out ContentBand band) && Choices.Contains(band)) Apply(band);
        }
    }
}
