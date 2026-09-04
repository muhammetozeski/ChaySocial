using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Identity
{
    /// <summary>
    /// How a master seed is shown to its owner and read back from them. The seed is the whole account, so the text
    /// form has to survive being copied out of a chat, typed off a piece of paper, or pasted with stray spaces —
    /// hence the grouping on the way out and the forgiving parse on the way in.
    /// </summary>
    public static class MasterSeedText
    {
        /// <summary> Characters per group in the displayed form. </summary>
        const int GroupLength = 4;

        /// <summary> Character written between groups. </summary>
        const char GroupSeparator = '-';

        /// <summary> Renders a seed as grouped base32 text. </summary>
        /// <param name="masterSeed"> The account's master seed. </param>
        /// <returns> The text to show the owner, e.g. <c>k7m2-q9x4-nes2-…</c>. </returns>
        public static string Format(ReadOnlySpan<byte> masterSeed)
        {
            string encoded = Base32.Encode(masterSeed);

            System.Text.StringBuilder grouped = new(encoded.Length + encoded.Length / GroupLength);
            for (int index = 0; index < encoded.Length; index += GroupLength)
            {
                if (index > 0) grouped.Append(GroupSeparator);
                grouped.Append(encoded.AsSpan(index, Math.Min(GroupLength, encoded.Length - index)));
            }

            return grouped.ToString();
        }

        /// <summary>
        /// Reads a seed back from whatever the owner pasted, ignoring separators, spaces and letter case.
        /// </summary>
        /// <param name="text"> Text the owner supplied. </param>
        /// <param name="masterSeed"> Receives the seed, or an empty array when the text was not a valid seed. </param>
        /// <returns> True when the text decoded to a seed of the right length. </returns>
        public static bool TryParse(string? text, out byte[] masterSeed)
        {
            masterSeed = [];
            if (string.IsNullOrWhiteSpace(text)) return false;

            string cleaned = new([.. text.Where(char.IsLetterOrDigit)]);

            if (!Base32.TryDecode(cleaned, out byte[] decoded)) return false;
            if (decoded.Length != IdentityScheme.MasterSeedSize) return false;

            masterSeed = decoded;
            return true;
        }
    }
}
