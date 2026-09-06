using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.Text;
using Org.BouncyCastle.Crypto.Digests;

namespace ChaySocial.MainProject.Identity
{
    /// <summary>
    /// A short phrase two accounts can read aloud to each other to check they are holding the same two addresses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The only certain way to know an account is the account is to compare forty-one characters of base32, and
    /// nobody reads that down a telephone. A name is no help: a display name says about itself that it is neither
    /// unique nor verified, and an impostor wearing the same one lands beside the real account in a search. The
    /// marks this app already draws — the sigil, the palette, the card — go to the eye, and the sigil's own notes
    /// say it reads fifteen bits and can be ground into a chosen shape. This goes to the ear instead.
    /// </para>
    /// <para>
    /// <b>What it is not.</b> The phrase shows that both ends hold the same pair of addresses. It does not show who
    /// is on the other end of the line — somebody reading it back may be reading it off a screen somebody else is
    /// holding — and it says nothing at all about any moment after the one it was read in.
    /// </para>
    /// <para>
    /// Nothing is stored and nothing is asked of any server. The phrase is worked out from the two addresses every
    /// time it is drawn, which is also why the two devices agree without having spoken first.
    /// </para>
    /// </remarks>
    public static class PairPhrase
    {
        /// <summary>
        /// Words in a phrase. The word list holds one word per byte, so each word carries eight bits and six carry
        /// forty-eight.
        /// </summary>
        /// <remarks>
        /// What that is worth, in the same units the chosen-address search was measured in: a browser drew 5,0
        /// candidate accounts per second, so grinding out an account whose phrase against a given one matches a
        /// wanted phrase takes about nine hundred thousand years on average. On a machine drawing a million
        /// candidates a second it is still about four and a half years. Five words would cut both by 256; seven
        /// makes a phrase too long to read down a telephone without losing the listener.
        /// </remarks>
        public const int WordsPerPhrase = 6;

        /// <summary> What this phrase is derived for, so the same fingerprints never produce it for anything else. </summary>
        static readonly byte[] PhraseDomain = "ChaySocial/PairPhrase/v1"u8.ToArray();

        /// <summary> How wide the digest is asked to be, in bits. </summary>
        const int DigestBits = 256;

        /// <summary>
        /// Works out the phrase two accounts share.
        /// </summary>
        /// <param name="addressA"> One address. </param>
        /// <param name="addressB"> The other. </param>
        /// <returns> The words, or an empty list when either address is not one of this app's. </returns>
        /// <remarks>
        /// The two fingerprints are put in order before hashing, so both devices arrive at the same phrase without
        /// agreeing on who goes first. Ordered by the fingerprints rather than by the written addresses, because
        /// the fingerprints are what goes into the hash.
        /// </remarks>
        public static IReadOnlyList<string> For(string addressA, string addressB)
        {
            if (!AppCryptography.Addresses.TryGetFingerprint(addressA, out byte[] first)) return [];
            if (!AppCryptography.Addresses.TryGetFingerprint(addressB, out byte[] second)) return [];

            (byte[] lower, byte[] higher) = first.AsSpan().SequenceCompareTo(second) <= 0
                ? (first, second)
                : (second, first);

            ShakeDigest digest = new(DigestBits);
            digest.BlockUpdate(PhraseDomain, 0, PhraseDomain.Length);
            digest.BlockUpdate(lower);
            digest.BlockUpdate(higher);

            byte[] picked = new byte[WordsPerPhrase];
            digest.OutputFinal(picked, 0, picked.Length);

            string[] words = new string[WordsPerPhrase];
            for (int index = 0; index < WordsPerPhrase; index++)
            {
                words[index] = PairWords.Spoken[picked[index]];
            }

            return words;
        }
    }
}
