using System.Globalization;
using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// The order this device puts its feed in, and the seed the shuffled order deals from. Kept exactly where the
    /// curtain and the palette are kept — on the device, through <see cref="AppServices.LocalStore"/> — so no
    /// server ever learns how anybody reads, and nobody running one can decide it for them.
    /// </summary>
    public static class FeedRecipe
    {
        /// <summary>
        /// How the feed is ordered. The default is what the wall has always done, so an account that never opens
        /// this reads exactly what it read yesterday.
        /// </summary>
        public static FeedOrder Order { get; private set; } = FeedOrder.Newest;

        /// <summary> The seed the shuffled order deals from; the same seed always deals the same page. </summary>
        public static int ShuffleSeed { get; private set; }

        /// <summary> The orders offered, in the order they are drawn. </summary>
        public static IReadOnlyList<FeedOrder> Choices { get; } = Enum.GetValues<FeedOrder>();

        /// <summary> What a reader is told each order does. </summary>
        /// <param name="order"> The order being described. </param>
        /// <returns> A short English line for the strip above the feed. </returns>
        public static string Describe(FeedOrder order) => order switch
        {
            FeedOrder.Newest => "Newest first",
            FeedOrder.Oldest => "Oldest first",
            FeedOrder.FewestChaysFirst => "Fewest chays first",
            FeedOrder.OneEachTurn => "One each, in turn",
            FeedOrder.Shuffled => "Shuffled",
            _ => string.Empty
        };

        /// <summary> Changes the order without writing anything to the device. </summary>
        /// <param name="order"> The order to read in. </param>
        public static void Apply(FeedOrder order) => Order = order;

        /// <summary> Changes the order and writes it to the device, so the next visit opens the same way. </summary>
        /// <param name="order"> The order to read in. </param>
        /// <returns> A task that completes once the choice has been stored. </returns>
        public static async Task ApplyAndRememberAsync(FeedOrder order)
        {
            Apply(order);

            if (AppServices.LocalStore is null) return;

            await AppServices.LocalStore.WriteAsync(LocalStoreKeys.FeedRecipe, order.ToString());
        }

        /// <summary>
        /// Throws the shuffle again. Drawn from the same source the app draws keys from, because a seed that is
        /// predictable would make one reader's shuffled page predictable to anybody who could guess it.
        /// </summary>
        /// <returns> A task that completes once the new seed has been stored. </returns>
        public static async Task ReshuffleAsync()
        {
            ShuffleSeed = BitConverter.ToInt32(RandomSource.Next(sizeof(int)));

            if (AppServices.LocalStore is null) return;

            await AppServices.LocalStore.WriteAsync(
                LocalStoreKeys.FeedShuffleSeed,
                ShuffleSeed.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Puts back what this device last chose. Called once at startup; leaves the defaults in place when nothing
        /// was stored or the stored value no longer names an order.
        /// </summary>
        /// <returns> A task that completes once the stored choice has been read and applied. </returns>
        public static async Task RestoreAsync()
        {
            if (AppServices.LocalStore is null) return;

            string? storedOrder = await AppServices.LocalStore.ReadAsync(LocalStoreKeys.FeedRecipe);
            if (storedOrder is not null && Enum.TryParse(storedOrder, out FeedOrder order) && Choices.Contains(order))
            {
                Apply(order);
            }

            string? storedSeed = await AppServices.LocalStore.ReadAsync(LocalStoreKeys.FeedShuffleSeed);
            if (storedSeed is not null && int.TryParse(storedSeed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
            {
                ShuffleSeed = seed;
            }
        }
    }
}
