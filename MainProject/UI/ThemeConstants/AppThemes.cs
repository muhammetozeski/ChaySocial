namespace ChaySocial.MainProject.Constants.ThemeConstants
{
    /// <summary>
    /// Ships every concrete <see cref="AppTheme"/> instance the app can switch into. Adding a new palette
    /// is a single new <c>public static readonly AppTheme</c> field here plus an entry in <see cref="All"/> —
    /// never edit individual color constants scattered across the UI.
    /// </summary>
    public static class AppThemes
    {
        //https://gemini.google.com/app/273756111760fb9a

        // [Rule] sen, yapay zeka gerizekalı olduğu için FromArgb fonksiyonunun ARGB şeklinde hex kodu beklediğini anlayamıyorsun ve RGBA giriyorsun.
        // bu yüzden FromArgb yerine FromRgba fonksiyonunu kullan. string girebilirsin ona.

        /// <summary>
        /// Default palette: midnight-violet aurora backdrop, bright indigo primary, warm coral secondary,
        /// amber accent. Readable, premium, warm.
        /// </summary>
        public static readonly AppTheme PlayfulStarlight = new()
        {
            Name = "Starlight",

            BackgroundDeep = Color.FromRgba("#070A1A"),
            BackgroundBase = Color.FromRgba("#0E1230"),

            Primary = Color.FromRgba("#7C7BFF"),
            PrimaryLight = Color.FromRgba("#A78BFA"),
            PrimaryDark = Color.FromRgba("#4F46E5"),
            Secondary = Color.FromRgba("#FF7A6B"),
            SecondaryDark = Color.FromRgba("#E5563F"),
            Accent = Color.FromRgba("#FFB547"),
            AccentDark = Color.FromRgba("#E0901B"),

            TextPrimary = Color.FromRgba("#F4F6FF"),
            TextSecondary = Color.FromRgba("#A8B1D9"),
            TextMuted = Color.FromRgba("#6B73A3"),
            TextOnFilledSurface = Colors.White,

            SurfaceSubtle = Colors.White.WithAlpha(0.05f),
            SurfaceNormal = Colors.White.WithAlpha(0.08f),
            SurfaceStrong = Colors.White.WithAlpha(0.14f),
            SurfaceTintPrimary = Color.FromRgba("#7C7BFF").WithAlpha(0.12f),
            SurfaceTintAccent = Color.FromRgba("#FFB547").WithAlpha(0.12f),
            SurfaceDarken = Colors.Black.WithAlpha(0.25f),

            GlassBorderTop = Colors.White.WithAlpha(0.20f),
            GlassBorderBottom = Colors.White.WithAlpha(0.08f),
            GlassBorderDefault = Colors.White.WithAlpha(0.12f),
            BorderStrong = Colors.White.WithAlpha(0.26f),
            BorderSoft = Colors.White.WithAlpha(0.13f),

            TooltipBackground = Color.FromRgba("#1E1B3A"),
            PaywallBackground = Color.FromRgba("#0F0C29"),

            Success = Color.FromRgba("#10B981"),
            SuccessDark = Color.FromRgba("#059669"),
            Error = Color.FromRgba("#EF4444"),
            ErrorDark = Color.FromRgba("#DC2626"),
            Warning = Color.FromRgba("#FFB547"),

            Gold = Color.FromRgba("#FFD700"),
            GoldDark = Color.FromRgba("#B8860B"),
            PremiumText = Color.FromRgba("#FFF6D9"),
            Diamond = Color.FromRgba("#38BDF8"),
            DiamondDark = Color.FromRgba("#0284C7"),
            Silver = Color.FromRgba("#C0C0C0"),
            SilverDark = Color.FromRgba("#808080"),
            Bronze = Color.FromRgba("#CD7F32"),
            BronzeDark = Color.FromRgba("#8B4513"),

            MonthGradients = BuildMonthGradients(),

            AuroraStops =
            [
                new(Color.FromRgba("#3B2A8C"), "20% 15%", "55% 60%"),
                new(Color.FromRgba("#5B2E94"), "85% 35%", "60% 55%"),
                new(Color.FromRgba("#7A2350"), "30% 95%", "60% 50%")
            ]
        };

        /// <summary>
        /// Light palette: warm paper rather than white. Ink-brown text on cream, muted terracotta and sage
        /// accents, and edges drawn in ink rather than in light — on a pale ground a white hairline disappears,
        /// so every border here is a dark one at low opacity.
        /// </summary>
        public static readonly AppTheme CreamPaper = new()
        {
            Name = "Cream",
            IsLight = true,

            BackgroundDeep = Color.FromRgba("#F6EFE2"),
            BackgroundBase = Color.FromRgba("#FBF6EC"),

            Primary = Color.FromRgba("#9A5B3D"),
            PrimaryLight = Color.FromRgba("#C08256"),
            PrimaryDark = Color.FromRgba("#6F3D28"),
            Secondary = Color.FromRgba("#C2603F"),
            SecondaryDark = Color.FromRgba("#9C4526"),
            Accent = Color.FromRgba("#8A8C4E"),
            AccentDark = Color.FromRgba("#66682F"),

            TextPrimary = Color.FromRgba("#332A22"),
            TextSecondary = Color.FromRgba("#6B5D51"),
            TextMuted = Color.FromRgba("#998A7C"),
            TextOnFilledSurface = Color.FromRgba("#FDF8F0"),

            SurfaceSubtle = Colors.White.WithAlpha(0.55f),
            SurfaceNormal = Colors.White.WithAlpha(0.75f),
            SurfaceStrong = Colors.White.WithAlpha(0.92f),
            SurfaceTintPrimary = Color.FromRgba("#9A5B3D").WithAlpha(0.10f),
            SurfaceTintAccent = Color.FromRgba("#8A8C4E").WithAlpha(0.12f),
            SurfaceDarken = Color.FromRgba("#332A22").WithAlpha(0.18f),

            GlassBorderTop = Colors.White.WithAlpha(0.85f),
            GlassBorderBottom = Color.FromRgba("#332A22").WithAlpha(0.10f),
            GlassBorderDefault = Color.FromRgba("#332A22").WithAlpha(0.14f),
            BorderStrong = Color.FromRgba("#332A22").WithAlpha(0.24f),
            BorderSoft = Color.FromRgba("#332A22").WithAlpha(0.12f),

            TooltipBackground = Color.FromRgba("#3B3128"),
            PaywallBackground = Color.FromRgba("#F1E7D6"),

            Success = Color.FromRgba("#4C7A52"),
            SuccessDark = Color.FromRgba("#385C3D"),
            Error = Color.FromRgba("#B3453A"),
            ErrorDark = Color.FromRgba("#8C3129"),
            Warning = Color.FromRgba("#B67A2E"),

            Gold = Color.FromRgba("#B98B36"),
            GoldDark = Color.FromRgba("#8A6522"),
            PremiumText = Color.FromRgba("#4A3A1E"),
            Diamond = Color.FromRgba("#4E8CA8"),
            DiamondDark = Color.FromRgba("#356175"),
            Silver = Color.FromRgba("#9AA0A6"),
            SilverDark = Color.FromRgba("#6E7479"),
            Bronze = Color.FromRgba("#A9743F"),
            BronzeDark = Color.FromRgba("#7C5029"),

            MonthGradients = BuildMonthGradients(),

            AuroraStops =
            [
                new(Color.FromRgba("#E9D9BE"), "18% 12%", "60% 55%"),
                new(Color.FromRgba("#EFD6C4"), "82% 30%", "55% 50%"),
                new(Color.FromRgba("#E3E1CB"), "35% 92%", "62% 48%")
            ]
        };

        /// <summary>
        /// Dark palette in green rather than violet: deep pine ground, moss primary, clay secondary. For anyone
        /// who wants the night without the purple.
        /// </summary>
        public static readonly AppTheme ForestDusk = new()
        {
            Name = "Forest",

            BackgroundDeep = Color.FromRgba("#08120E"),
            BackgroundBase = Color.FromRgba("#0F1F19"),

            Primary = Color.FromRgba("#5FBF8E"),
            PrimaryLight = Color.FromRgba("#8FD9AE"),
            PrimaryDark = Color.FromRgba("#2F8A5F"),
            Secondary = Color.FromRgba("#E2A15C"),
            SecondaryDark = Color.FromRgba("#B87833"),
            Accent = Color.FromRgba("#D9CE6B"),
            AccentDark = Color.FromRgba("#A99F41"),

            TextPrimary = Color.FromRgba("#EDF6F0"),
            TextSecondary = Color.FromRgba("#A3C0B1"),
            TextMuted = Color.FromRgba("#6C8A79"),
            TextOnFilledSurface = Color.FromRgba("#08120E"),

            SurfaceSubtle = Colors.White.WithAlpha(0.05f),
            SurfaceNormal = Colors.White.WithAlpha(0.08f),
            SurfaceStrong = Colors.White.WithAlpha(0.14f),
            SurfaceTintPrimary = Color.FromRgba("#5FBF8E").WithAlpha(0.14f),
            SurfaceTintAccent = Color.FromRgba("#D9CE6B").WithAlpha(0.12f),
            SurfaceDarken = Colors.Black.WithAlpha(0.28f),

            GlassBorderTop = Colors.White.WithAlpha(0.18f),
            GlassBorderBottom = Colors.White.WithAlpha(0.07f),
            GlassBorderDefault = Colors.White.WithAlpha(0.12f),
            BorderStrong = Color.FromRgba("#5FBF8E").WithAlpha(0.34f),
            BorderSoft = Colors.White.WithAlpha(0.12f),

            TooltipBackground = Color.FromRgba("#16281F"),
            PaywallBackground = Color.FromRgba("#0A1611"),

            Success = Color.FromRgba("#4ECB88"),
            SuccessDark = Color.FromRgba("#2E9560"),
            Error = Color.FromRgba("#E86A5C"),
            ErrorDark = Color.FromRgba("#B84A3E"),
            Warning = Color.FromRgba("#E2A15C"),

            Gold = Color.FromRgba("#D9B45C"),
            GoldDark = Color.FromRgba("#A8862F"),
            PremiumText = Color.FromRgba("#F6EFD5"),
            Diamond = Color.FromRgba("#6FD3D0"),
            DiamondDark = Color.FromRgba("#39918F"),
            Silver = Color.FromRgba("#B6C4BB"),
            SilverDark = Color.FromRgba("#7C8B82"),
            Bronze = Color.FromRgba("#C08A4E"),
            BronzeDark = Color.FromRgba("#8A5F2F"),

            MonthGradients = BuildMonthGradients(),

            AuroraStops =
            [
                new(Color.FromRgba("#1B4D38"), "20% 15%", "55% 60%"),
                new(Color.FromRgba("#2A5E3F"), "85% 35%", "60% 55%"),
                new(Color.FromRgba("#3E4F22"), "30% 95%", "60% 50%")
            ]
        };

        /// <summary> Every palette the app can switch into, in the order a picker should offer them. </summary>
        public static readonly AppTheme[] All = [PlayfulStarlight, CreamPaper, ForestDusk];

        /// <summary> Finds a palette by the name it stores itself under. </summary>
        /// <param name="name"> Value of <see cref="AppTheme.Name"/>. </param>
        /// <returns> The matching palette, or null when no palette carries that name. </returns>
        public static AppTheme? FindByName(string name)
            => All.FirstOrDefault(theme => theme.Name == name);

        /// <summary>
        /// The twelve month-indexed gradient pairs. Shared by every palette because they colour calendar cards by
        /// month rather than by theme, and a month should not change colour when the theme does.
        /// </summary>
        /// <returns> Twelve start/end colour pairs. </returns>
        static (Color Start, Color End)[] BuildMonthGradients() =>
        [
            (Color.FromRgba("#4F46E5"), Color.FromRgba("#2563EB")),
            (Color.FromRgba("#FF7A6B"), Color.FromRgba("#E5563F")),
            (Color.FromRgba("#A78BFA"), Color.FromRgba("#7C3AED")),
            (Color.FromRgba("#34D399"), Color.FromRgba("#059669")),
            (Color.FromRgba("#FFB547"), Color.FromRgba("#E0901B")),
            (Color.FromRgba("#38BDF8"), Color.FromRgba("#0284C7")),
            (Color.FromRgba("#FB7185"), Color.FromRgba("#E11D48")),
            (Color.FromRgba("#2DD4BF"), Color.FromRgba("#0D9488")),
            (Color.FromRgba("#F97316"), Color.FromRgba("#C2410C")),
            (Color.FromRgba("#8B5CF6"), Color.FromRgba("#6D28D9")),
            (Color.FromRgba("#F59E0B"), Color.FromRgba("#B45309")),
            (Color.FromRgba("#60A5FA"), Color.FromRgba("#1D4ED8"))
        ];
    }
}
