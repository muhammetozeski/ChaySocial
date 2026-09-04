namespace ChaySocial.MainProject.Text
{
    /// <summary>
    /// RFC 4648 base32 without padding, in lower case. Chosen over base64 for anything a person may read aloud,
    /// retype or see in a URL: the alphabet has no case distinction to lose, and no character pairs that blur into
    /// each other in a typical font.
    /// </summary>
    public static class Base32
    {
        const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        const int BitsPerCharacter = 5;
        const int BitsPerByte = 8;

        /// <summary> Encodes bytes into base32 text. </summary>
        /// <param name="data"> Bytes to encode. </param>
        /// <returns> The lower-case, unpadded base32 text. </returns>
        public static string Encode(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty) return string.Empty;

            System.Text.StringBuilder text = new((data.Length * BitsPerByte + BitsPerCharacter - 1) / BitsPerCharacter);
            int buffer = 0;
            int bufferedBits = 0;

            foreach (byte value in data)
            {
                buffer = (buffer << BitsPerByte) | value;
                bufferedBits += BitsPerByte;

                while (bufferedBits >= BitsPerCharacter)
                {
                    bufferedBits -= BitsPerCharacter;
                    text.Append(Alphabet[(buffer >> bufferedBits) & 0x1F]);
                }
            }

            // Whatever is left over is padded with zero bits on the right to fill one last character.
            if (bufferedBits > 0)
            {
                text.Append(Alphabet[(buffer << (BitsPerCharacter - bufferedBits)) & 0x1F]);
            }

            return text.ToString();
        }

        /// <summary> Decodes base32 text back into bytes, accepting either case. </summary>
        /// <param name="text"> Text produced by <see cref="Encode"/>. </param>
        /// <param name="data"> Receives the decoded bytes, or an empty array when the text was not valid base32. </param>
        /// <returns> True when every character was part of the alphabet and the text decoded cleanly. </returns>
        public static bool TryDecode(string text, out byte[] data)
        {
            data = [];
            if (string.IsNullOrEmpty(text)) return true;

            List<byte> decoded = new(text.Length * BitsPerCharacter / BitsPerByte);
            int buffer = 0;
            int bufferedBits = 0;

            foreach (char character in text)
            {
                int value = Alphabet.IndexOf(char.ToLowerInvariant(character));
                if (value < 0) return false;

                buffer = (buffer << BitsPerCharacter) | value;
                bufferedBits += BitsPerCharacter;

                if (bufferedBits < BitsPerByte) continue;

                bufferedBits -= BitsPerByte;
                decoded.Add((byte)((buffer >> bufferedBits) & 0xFF));
            }

            // The trailing bits only exist to fill the last character; they must be zero, otherwise the text carries
            // information that no byte sequence could have produced and is therefore not a clean encoding.
            if (bufferedBits > 0 && (buffer & ((1 << bufferedBits) - 1)) != 0) return false;

            data = [.. decoded];
            return true;
        }
    }
}

