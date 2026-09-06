using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.Text;
using Microsoft.Maui.Graphics;

namespace ChaySocial.MainProject.UI.Elements.Social
{
    /// <summary>
    /// The inks a snippet is coloured in, read from the live theme on every call exactly as a drawing's are. What a
    /// token carries is what kind of thing it is, never a colour — the code belongs to whoever wrote it and the
    /// colours to whoever is reading it, so the same snippet arrives in Starlight, Cream and Forest wearing that
    /// reader's palette.
    /// </summary>
    public static class CodeInks
    {
        /// <summary> The palette, in the order <see cref="CodeTokenKind"/> declares its kinds. </summary>
        public static Color[] Palette =>
        [
            AppColors.TextPrimary,
            AppColors.TextLink,
            AppColors.TextSuccess,
            AppColors.TextWarning,
            AppColors.TextMuted
        ];

        /// <summary> Turns a token's kind into something a colour attribute can take. </summary>
        /// <param name="kind"> The kind the reader worked out. </param>
        /// <returns> The colour for that kind under the theme currently on screen. </returns>
        /// <remarks>
        /// Wrapped rather than trusted, for the same reason a drawing's ink index is: a kind from a future version
        /// with more of them would otherwise reach past the end of this palette, and a token in a wrong-but-real
        /// colour is a better outcome than a snippet that throws while a feed scrolls past it.
        /// </remarks>
        public static string HexOf(CodeTokenKind kind)
        {
            Color[] palette = Palette;
            int wrapped = (((int)kind % palette.Length) + palette.Length) % palette.Length;

            return palette[wrapped].ToRgbaHex(true);
        }
    }
}
