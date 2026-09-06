using System.Security;
using System.Text;
using ChaySocial.MainProject.Constants.ThemeConstants;
using ChaySocial.MainProject.Services;
using Microsoft.Maui.Graphics;

namespace ChaySocial.MainProject.Identity
{
    /// <summary>
    /// Draws an account as one self-contained SVG document, so it can be carried out of this app.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An account here cannot be shown anywhere else today: forty-one characters of base32 is not something
    /// anybody puts on another site, so nobody can be found. A card turns an account into an object worth
    /// putting somewhere — and it is the first thing in this whole project a person can carry outside it.
    /// </para>
    /// <para>
    /// It costs nothing and reaches no server: the card is drawn on the device from data the device already has,
    /// and it carries no script of any kind — it is a picture, and a picture is all it is.
    /// </para>
    /// </remarks>
    public static class IdentityCardDrawing
    {
        /// <summary> How wide the card is, in the drawing's own units. </summary>
        const int CardWidthPx = 640;

        /// <summary> How tall it is. </summary>
        const int CardHeightPx = 360;

        /// <summary> Room kept clear inside its edge. </summary>
        const int CardPaddingPx = 40;

        /// <summary> How wide and tall one cell of the mark is. </summary>
        const int SigilCellSpanPx = 24;

        /// <summary> Where the mark's top-left cell starts across. </summary>
        const int SigilOriginXPx = CardPaddingPx;

        /// <summary> Where it starts down. </summary>
        const int SigilOriginYPx = 118;

        /// <summary> Where the name sits, measured to its baseline. </summary>
        const int NameBaselinePx = 90;

        /// <summary>
        /// Where the address line sits: under the mark rather than beside it, because forty-one characters plus
        /// their group spaces need the card's whole width to be readable off a picture.
        /// </summary>
        const int AddressLineBaselinePx = 280;

        /// <summary> Where the joining date sits. </summary>
        const int JoinedBaselinePx = 308;

        /// <summary> Where the app's own name sits, at the foot of the card. </summary>
        const int BrandBaselinePx = 335;

        /// <summary> Characters per group in the printed address, so forty-one of them can be read off a picture. </summary>
        const int AddressGroupLength = 8;

        /// <summary> Where the writing starts across, clear of the mark. </summary>
        const int TextOriginXPx = CardPaddingPx;

        /// <summary> Where the name's own emoji sits, to the left of it. </summary>
        const int AvatarBaselinePx = NameBaselinePx;

        /// <summary> Size of the account's name. </summary>
        const int NameFontSizePx = 40;

        /// <summary> Size of the emoji beside it. </summary>
        const int AvatarFontSizePx = 44;

        /// <summary> Size of the printed address. </summary>
        const int AddressFontSizePx = 20;

        /// <summary> Size of the joining line and the app's name. </summary>
        const int FootnoteFontSizePx = 16;

        /// <summary> How far the name is pushed right of its emoji. </summary>
        const int NameIndentPx = 60;

        /// <summary> Corner radius of the card itself. </summary>
        const int CardCornerRadiusPx = 28;

        /// <summary> Corner radius of one cell of the mark. </summary>
        const int SigilCellRadiusPx = 4;

        /// <summary> How far across the mark reaches. </summary>
        const int SigilWidthPx = AddressSigil.GridColumnCount * SigilCellSpanPx;

        /// <summary> Typeface stack for the name: whatever the machine opening the file happens to have. </summary>
        const string NameFontStack = "Segoe UI, Helvetica, Arial, sans-serif";

        /// <summary> Typeface stack for the address, where every character has to be told apart. </summary>
        const string AddressFontStack = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";

        /// <summary> What the line above the joining date says. </summary>
        const string JoinedLabel = "Joined";

        /// <summary> The app's own name, printed at the foot so a card found anywhere says where it came from. </summary>
        const string BrandLine = "Chay Social — an account is a secret you keep";

        /// <summary> Builds the card. </summary>
        /// <param name="address"> The account's address. </param>
        /// <param name="displayName"> The name it goes by. </param>
        /// <param name="avatarEmoji"> The emoji beside that name. </param>
        /// <param name="createdAtUnixMs"> When the account was made. </param>
        /// <returns> One SVG document, complete in itself and carrying no script. </returns>
        public static string Build(string address, string displayName, string avatarEmoji, long createdAtUnixMs)
        {
            // Escaped before anything else touches it. The on-screen copy is drawn through MarkupString, which
            // skips Blazor's own escaping, so a display name carrying < or & would otherwise break the card or
            // put markup into the page.
            string safeName = SecurityElement.Escape(displayName) ?? string.Empty;
            string safeAvatar = SecurityElement.Escape(avatarEmoji) ?? string.Empty;
            string safeAddress = SecurityElement.Escape(GroupAddress(address)) ?? string.Empty;
            string safeJoined = SecurityElement.Escape($"{JoinedLabel} {RelativeTimeFormatter.FormatExact(createdAtUnixMs)}") ?? string.Empty;

            StringBuilder card = new();

            card.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {CardWidthPx} {CardHeightPx}\" width=\"{CardWidthPx}\" height=\"{CardHeightPx}\" role=\"img\">");
            card.Append($"<rect width=\"{CardWidthPx}\" height=\"{CardHeightPx}\" rx=\"{CardCornerRadiusPx}\" fill=\"{Hex(AppColors.BackgroundDeep)}\" />");

            AppendSigil(card, address);

            card.Append($"<text x=\"{TextOriginXPx}\" y=\"{AvatarBaselinePx}\" font-size=\"{AvatarFontSizePx}\">{safeAvatar}</text>");
            card.Append($"<text x=\"{TextOriginXPx + NameIndentPx}\" y=\"{NameBaselinePx}\" font-family=\"{NameFontStack}\" font-size=\"{NameFontSizePx}\" font-weight=\"700\" fill=\"{Hex(AppColors.TextPrimary)}\">{safeName}</text>");
            card.Append($"<text x=\"{TextOriginXPx}\" y=\"{AddressLineBaselinePx}\" font-family=\"{AddressFontStack}\" font-size=\"{AddressFontSizePx}\" fill=\"{Hex(AppColors.TextSecondary)}\">{safeAddress}</text>");
            card.Append($"<text x=\"{TextOriginXPx}\" y=\"{JoinedBaselinePx}\" font-family=\"{NameFontStack}\" font-size=\"{FootnoteFontSizePx}\" fill=\"{Hex(AppColors.TextMuted)}\">{safeJoined}</text>");
            card.Append($"<text x=\"{TextOriginXPx}\" y=\"{BrandBaselinePx}\" font-family=\"{NameFontStack}\" font-size=\"{FootnoteFontSizePx}\" fill=\"{Hex(AppColors.TextMuted)}\">{BrandLine}</text>");

            card.Append("</svg>");
            return card.ToString();
        }

        /// <summary>
        /// Draws the account's mark, cell by cell, the way the screen draws it — including the spine down the
        /// middle column, so the file and the app never show two different marks for one account.
        /// </summary>
        /// <param name="card"> The document being built. </param>
        /// <param name="address"> The account's address. </param>
        static void AppendSigil(StringBuilder card, string address)
        {
            if (AddressSigil.Build(address) is not AddressSigil mark) return;

            Color[] inks = Inks;

            for (int row = 0; row < AddressSigil.GridRowCount; row++)
            {
                for (int column = 0; column < AddressSigil.GridColumnCount; column++)
                {
                    if (!mark.IsLit(column, row)) continue;

                    bool isSpine = column == AddressSigil.GridColumnCount / 2;
                    Color ink = inks[isSpine ? mark.AccentIndex(inks.Length) : mark.InkIndex(inks.Length)];

                    int x = SigilOriginXPx + (column * SigilCellSpanPx);
                    int y = SigilOriginYPx + (row * SigilCellSpanPx);

                    card.Append($"<rect x=\"{x}\" y=\"{y}\" width=\"{SigilCellSpanPx}\" height=\"{SigilCellSpanPx}\" rx=\"{SigilCellRadiusPx}\" fill=\"{Hex(ink)}\" />");
                }
            }
        }

        /// <summary> The colours a mark can be drawn in, read from the live theme exactly as the screen reads them. </summary>
        static Color[] Inks =>
        [
            AppColors.Primary,
            AppColors.PrimaryLight,
            AppColors.Secondary,
            AppColors.Accent,
            AppColors.AccentDark,
            AppColors.SecondaryDark
        ];

        /// <summary> Breaks an address into groups, so forty-one characters can be read off a picture. </summary>
        /// <param name="address"> The address as it is written. </param>
        /// <returns> The address in groups separated by spaces. </returns>
        static string GroupAddress(string address)
        {
            StringBuilder grouped = new(address.Length + (address.Length / AddressGroupLength));

            for (int index = 0; index < address.Length; index += AddressGroupLength)
            {
                if (index > 0) grouped.Append(' ');
                grouped.Append(address.AsSpan(index, Math.Min(AddressGroupLength, address.Length - index)));
            }

            return grouped.ToString();
        }

        /// <summary> One colour as an SVG fill. </summary>
        /// <param name="colour"> The colour to write. </param>
        /// <returns> Its hex form. </returns>
        static string Hex(Color colour) => colour.ToRgbaHex(true);
    }
}
