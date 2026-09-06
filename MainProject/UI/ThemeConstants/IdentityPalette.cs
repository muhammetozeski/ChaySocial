using ChaySocial.MainProject.Cryptography;
using Microsoft.Maui.Graphics;

namespace ChaySocial.MainProject.Constants.ThemeConstants
{
    /// <summary>
    /// A palette dealt by an account's own address.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The colour comes out of the address's fingerprint, so nobody chooses it, nobody else has it, and nobody can
    /// take it — which is also why it can never turn into a status game. The app stops looking like everybody's
    /// copy and starts wearing the key.
    /// </para>
    /// <para>
    /// It does a job as well as looking like something: two accounts carried on one device are told apart at a
    /// glance, so writing from the wrong identity looks wrong before a word of the post is written — on a platform
    /// whose whole premise is that an account is a secret you keep.
    /// </para>
    /// <para>
    /// Only the branded hues move. The backgrounds, the three text tones, every surface, glass and border token and
    /// the semantic success, error and warning colours are left exactly as the base palette set them, so legibility
    /// is never left to chance: turning a hue keeps its brightness, and fixed text keeps its contrast against a
    /// fixed ground.
    /// </para>
    /// </remarks>
    public static class IdentityPalette
    {
        /// <summary> What an identity palette's name begins with, so a stored one can be told from a shipped one. </summary>
        public const string IdentityPaletteNamePrefix = "Yours · ";

        /// <summary> What separates the base palette's name from the address head inside that name. </summary>
        public const string IdentityPaletteNameSeparator = " · ";

        /// <summary> Which fingerprint byte decides how far around the colour wheel the palette turns. </summary>
        const int HueByteIndex = 0;

        /// <summary> Which fingerprint byte decides how much the palette's colours deepen. </summary>
        const int SaturationByteIndex = 1;

        /// <summary>
        /// How far around the colour wheel one step of that byte turns the palette.
        /// </summary>
        /// <remarks>
        /// A whole turn spread across the 256 values a byte can take, so every value lands somewhere different and
        /// the two ends meet rather than overlapping. The fraction is deliberate: hue here runs from 0 to 1, not in
        /// degrees, and a constant named in degrees would be a lie about what the number measures.
        /// </remarks>
        const float HueTurnPerFingerprintStep = 1f / 256f;

        /// <summary> Most the palette's colours deepen, at the top of the byte that decides it. </summary>
        const float SaturationLiftFraction = 0.2f;

        /// <summary> Characters of the address kept in the palette's name, enough to tell two accounts apart. </summary>
        const int AddressHeadCharacterCount = 8;

        /// <summary> The strongest a colour may be pushed, which is where the colour space itself stops. </summary>
        const float FullSaturation = 1f;

        /// <summary> A whole turn of the colour wheel, which hue wraps around. </summary>
        const float WholeTurn = 1f;

        /// <summary> Builds the palette one address wears. </summary>
        /// <param name="address"> The account's address. </param>
        /// <param name="baseTheme"> The palette to turn, which decides everything this does not touch. </param>
        /// <returns> The account's own palette, or <paramref name="baseTheme"/> unchanged when the address is not one. </returns>
        public static AppTheme BuildFrom(string address, AppTheme baseTheme)
        {
            if (!AppCryptography.Addresses.TryGetFingerprint(address, out byte[] fingerprint)) return baseTheme;
            if (fingerprint.Length <= SaturationByteIndex) return baseTheme;

            float turn = fingerprint[HueByteIndex] * HueTurnPerFingerprintStep;
            float lift = fingerprint[SaturationByteIndex] * SaturationLiftFraction / byte.MaxValue;

            return baseTheme with
            {
                Name = NameFor(address, baseTheme),
                Primary = Dress(baseTheme.Primary, turn, lift),
                PrimaryLight = Dress(baseTheme.PrimaryLight, turn, lift),
                PrimaryDark = Dress(baseTheme.PrimaryDark, turn, lift),
                Secondary = Dress(baseTheme.Secondary, turn, lift),
                SecondaryDark = Dress(baseTheme.SecondaryDark, turn, lift),
                Accent = Dress(baseTheme.Accent, turn, lift),
                AccentDark = Dress(baseTheme.AccentDark, turn, lift),
                AuroraStops = [.. baseTheme.AuroraStops.Select(stop => stop with { Color = Dress(stop.Color, turn, lift) })]
            };
        }

        /// <summary>
        /// The base palette a stored identity palette was built from, so a device that opens before it knows which
        /// account is signed in still opens in something close to the right colours.
        /// </summary>
        /// <param name="storedName"> The palette name read back from the device. </param>
        /// <returns> The base palette, or null when the name does not belong to an identity palette. </returns>
        public static AppTheme? BaseThemeOf(string storedName)
        {
            if (!storedName.StartsWith(IdentityPaletteNamePrefix, StringComparison.Ordinal)) return null;

            string withoutPrefix = storedName[IdentityPaletteNamePrefix.Length..];
            int separator = withoutPrefix.LastIndexOf(IdentityPaletteNameSeparator, StringComparison.Ordinal);

            return AppThemes.FindByName(separator < 0 ? withoutPrefix : withoutPrefix[..separator]);
        }

        /// <summary> Names the palette after the base it turns and the account that wears it. </summary>
        /// <param name="address"> The account's address. </param>
        /// <param name="baseTheme"> The palette being turned. </param>
        /// <returns> A name no two accounts share, which is also what makes the layout repaint on a switch. </returns>
        static string NameFor(string address, AppTheme baseTheme)
        {
            string head = address.Length <= AddressHeadCharacterCount ? address : address[..AddressHeadCharacterCount];
            return IdentityPaletteNamePrefix + baseTheme.Name + IdentityPaletteNameSeparator + head;
        }

        /// <summary> Turns one colour around the wheel and deepens it, leaving how bright it is alone. </summary>
        /// <param name="colour"> The base palette's colour. </param>
        /// <param name="turn"> How far around the wheel to go, as a fraction of a whole turn. </param>
        /// <param name="lift"> How much to deepen it. </param>
        /// <returns> The dressed colour. </returns>
        static Color Dress(Color colour, float turn, float lift)
        {
            Color turned = colour.WithHue((colour.GetHue() + turn) % WholeTurn);
            return turned.WithSaturation(Math.Clamp(turned.GetSaturation() + lift, 0f, FullSaturation));
        }
    }
}
