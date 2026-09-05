using ChaySocial.MainProject.Constants.ThemeConstants;
using Microsoft.Maui.Graphics;

namespace ChaySocial.MainProject.UI.Elements.Social
{
    /// <summary>
    /// The inks a drawing may be made in. Read from the live theme on every call rather than stored, exactly as the
    /// address seal's colours are, so a drawing repaints itself when the reader picks a different theme. What is
    /// stored in a stroke is which ink, never which colour — the shape belongs to whoever drew it, the colour to
    /// whoever is looking.
    /// </summary>
    public static class DrawingInks
    {
        /// <summary> The palette, in the order the board offers it. </summary>
        public static Color[] Palette =>
        [
            AppColors.Primary,
            AppColors.Secondary,
            AppColors.Accent,
            AppColors.TextPrimary,
            AppColors.TextSuccess,
            AppColors.TextDanger
        ];

        /// <summary> The ink a drawing starts with, so the first stroke is in the app's own colour. </summary>
        public const int StartingInkIndex = 0;

        /// <summary> Turns a stored ink index into something a fill attribute can take. </summary>
        /// <param name="inkIndex"> Index as it was stored, which may have come from another device. </param>
        /// <returns> The colour for that ink under the theme currently on screen. </returns>
        /// <remarks>
        /// The index is wrapped rather than trusted. A sheet written by a future version with a longer palette would
        /// otherwise reach past the end of this one, and a drawing in a wrong-but-real colour is a far better outcome
        /// than a drawing that throws while a feed is scrolling past it.
        /// </remarks>
        public static string HexAt(int inkIndex)
        {
            Color[] palette = Palette;
            int wrapped = ((inkIndex % palette.Length) + palette.Length) % palette.Length;

            return palette[wrapped].ToRgbaHex(true);
        }
    }
}
