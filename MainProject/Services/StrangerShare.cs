using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary> How much of a reader's following feed is let in from accounts they do not follow. </summary>
    public enum StrangerShareLevel
    {
        /// <summary> Nobody. The feed is exactly the accounts and subjects the reader chose. </summary>
        None,

        /// <summary> About one line in ten. </summary>
        OneInTen,

        /// <summary> About one line in five. </summary>
        OneInFive,

        /// <summary> About one line in three. </summary>
        OneInThree
    }

    /// <summary>
    /// The dial that decides how much of the following feed comes from outside it, kept where the order, the
    /// curtain and the palette are kept — on the device, through <see cref="AppServices.LocalStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other platform decides this for its readers and never marks which lines it decided about. Here the
    /// reader sets the proportion themselves, in named steps, and every line let in says on itself that it came
    /// from outside and which setting put it there.
    /// </para>
    /// <para>
    /// <see cref="StrangerShareLevel.None"/> really is none, because the mixing is arithmetic on this device
    /// rather than a promise made by a server.
    /// </para>
    /// </remarks>
    public static class StrangerShare
    {
        /// <summary>
        /// How much of the feed comes from outside it. The default lets nobody in, so a reader who never opens
        /// this reads exactly what they chose to read.
        /// </summary>
        public static StrangerShareLevel Level { get; private set; } = StrangerShareLevel.None;

        /// <summary> The levels offered, in the order they are drawn. </summary>
        public static IReadOnlyList<StrangerShareLevel> Choices { get; } = Enum.GetValues<StrangerShareLevel>();

        /// <summary> Followed lines laid out between two strangers at one in ten. </summary>
        const int LinesFollowedBetweenStrangersAtOneInTen = 9;

        /// <summary> The same at one in five. </summary>
        const int LinesFollowedBetweenStrangersAtOneInFive = 4;

        /// <summary> The same at one in three. </summary>
        const int LinesFollowedBetweenStrangersAtOneInThree = 2;

        /// <summary> True when this reader has asked for any strangers at all. </summary>
        public static bool IsOn => Level != StrangerShareLevel.None;

        /// <summary> What a reader is told each level does. </summary>
        /// <param name="level"> The level being described. </param>
        /// <returns> A short English line for the strip above the feed. </returns>
        public static string Describe(StrangerShareLevel level) => level switch
        {
            StrangerShareLevel.None => "Nobody I don't follow",
            StrangerShareLevel.OneInTen => "One line in ten",
            StrangerShareLevel.OneInFive => "One line in five",
            StrangerShareLevel.OneInThree => "One line in three",
            _ => string.Empty
        };

        /// <summary>
        /// How many followed lines are laid out before one from outside is placed between them.
        /// </summary>
        /// <param name="level"> The level in force. </param>
        /// <returns> The gap, or zero when nobody is being let in. </returns>
        public static int LinesFollowedBetweenStrangers(StrangerShareLevel level) => level switch
        {
            StrangerShareLevel.OneInTen => LinesFollowedBetweenStrangersAtOneInTen,
            StrangerShareLevel.OneInFive => LinesFollowedBetweenStrangersAtOneInFive,
            StrangerShareLevel.OneInThree => LinesFollowedBetweenStrangersAtOneInThree,
            _ => 0
        };

        /// <summary>
        /// How many lines from outside belong on a page of this size, at the level in force.
        /// </summary>
        /// <param name="lines"> How long the page is. </param>
        /// <param name="level"> The level in force. </param>
        /// <returns> How many of those lines come from accounts the reader does not follow. </returns>
        /// <remarks>
        /// One in every <c>gap + 1</c> lines: at one in three, two followed lines are laid out and then one from
        /// outside, so a page of thirty holds ten of them.
        /// </remarks>
        public static int StrangersOnAPageOf(int lines, StrangerShareLevel level)
        {
            int gap = LinesFollowedBetweenStrangers(level);

            return gap <= 0 ? 0 : lines / (gap + 1);
        }

        /// <summary> Changes the level without writing anything to the device. </summary>
        /// <param name="level"> The level to read at. </param>
        public static void Apply(StrangerShareLevel level) => Level = level;

        /// <summary> Changes the level and writes it to the device, so the next visit opens the same way. </summary>
        /// <param name="level"> The level to read at. </param>
        /// <returns> A task that completes once the choice has been stored. </returns>
        public static async Task ApplyAndRememberAsync(StrangerShareLevel level)
        {
            Apply(level);

            if (AppServices.LocalStore is null) return;

            await AppServices.LocalStore.WriteAsync(LocalStoreKeys.StrangerShare, level.ToString());
        }

        /// <summary>
        /// Puts back what this device last chose. Called once at startup; leaves the default in place when nothing
        /// was stored or the stored value no longer names a level.
        /// </summary>
        /// <returns> A task that completes once the stored choice has been read and applied. </returns>
        public static async Task RestoreAsync()
        {
            if (AppServices.LocalStore is null) return;

            string? stored = await AppServices.LocalStore.ReadAsync(LocalStoreKeys.StrangerShare);
            if (stored is not null && Enum.TryParse(stored, out StrangerShareLevel level) && Choices.Contains(level))
            {
                Apply(level);
            }
        }
    }
}
