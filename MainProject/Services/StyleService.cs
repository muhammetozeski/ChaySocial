using System.Text;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// How each account this device carries writes, worked out from what that account has already published.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything else in this app protects a person from the server. This protects a person from themselves: the
    /// app hands out up to <see cref="AnonymityService.MaximumCarriedAccounts"/> accounts that no key material
    /// connects, and then says nothing about the fact that sentences carry a fingerprint no key can hide. People
    /// who get every other part right are identified this way.
    /// </para>
    /// <para>
    /// The posts folded in here are documents the server already publishes to anybody who asks for them, and the
    /// arithmetic on top of them never leaves the device. Nothing is written anywhere, and no account learns that
    /// another was compared against it.
    /// </para>
    /// </remarks>
    public static class StyleService
    {
        /// <summary> How many of an account's newest posts are folded into its fingerprint. </summary>
        public const int SamplePostCount = 40;

        /// <summary> What separates two folded posts, so the last word of one does not join the first of the next. </summary>
        const string PostSeparator = "\n\n";

        /// <summary> Measures how one account writes, from the posts it has published. </summary>
        /// <param name="address"> The account. </param>
        /// <returns> Its fingerprint, or <see cref="StyleFingerprint.Unwritten"/> when it has published nothing. </returns>
        public static async Task<StyleFingerprint> ReadForAsync(string address)
        {
            if (string.IsNullOrEmpty(address)) return StyleFingerprint.Unwritten;

            // Group posts are already left out by the wall read, which is right here too: what somebody said inside
            // a room is not part of the writing anybody outside it can compare them against.
            IReadOnlyList<PostData> posts = await WallService.ReadAuthorPostsAsync(address, SamplePostCount);
            if (posts.Count == 0) return StyleFingerprint.Unwritten;

            StringBuilder folded = new();

            foreach (PostData post in posts)
            {
                Fold(folded, WritingOf(post.Text, post.Title, post.LongBody));
            }

            return StyleFingerprint.Of(folded.ToString());
        }

        /// <summary>
        /// Folds the three places a post carries writing into the one body a fingerprint is measured from.
        /// </summary>
        /// <param name="text"> The line. </param>
        /// <param name="title"> The line set above a long piece, empty when the post is not one. </param>
        /// <param name="longBody"> The whole of a long piece, empty for an ordinary post. </param>
        /// <returns> The three of them as one body, in the order they are written.  </returns>
        /// <remarks>
        /// Published here so a draft is folded by exactly the same code the corpus is. A draft measured from the
        /// line alone, against accounts measured from line and title and piece, is not the comparison the mirror
        /// claims to be making — and it is silent altogether for somebody writing a long piece, which is the
        /// writing that carries a fingerprint best.
        /// </remarks>
        public static string WritingOf(string text, string title, string longBody)
        {
            StringBuilder folded = new();

            Fold(folded, text);
            Fold(folded, title);
            Fold(folded, longBody);

            return folded.ToString();
        }

        /// <summary>
        /// Measures every account this device carries. They are read together rather than one after another; there
        /// are at most <see cref="AnonymityService.MaximumCarriedAccounts"/> of them, which is small enough that no
        /// batching is worth the words it would take to explain.
        /// </summary>
        /// <returns> A fingerprint per carried address, including the ones with nothing published yet. </returns>
        public static async Task<IReadOnlyDictionary<string, StyleFingerprint>> ReadCarriedAsync()
        {
            IReadOnlyList<CarriedAccount> carried = await AnonymityService.ReadCarriedAsync();
            if (carried.Count == 0) return new Dictionary<string, StyleFingerprint>();

            string[] addresses = [.. carried.Select(account => account.Address).Distinct(StringComparer.Ordinal)];
            StyleFingerprint[] measured = await Task.WhenAll(addresses.Select(ReadForAsync));

            Dictionary<string, StyleFingerprint> byAddress = new(addresses.Length, StringComparer.Ordinal);
            for (int index = 0; index < addresses.Length; index++)
            {
                byAddress[addresses[index]] = measured[index];
            }

            return byAddress;
        }

        /// <summary> Adds one piece of writing to the folded body, when there is any of it. </summary>
        /// <param name="folded"> The body being built. </param>
        /// <param name="written"> The piece to add. </param>
        static void Fold(StringBuilder folded, string written)
        {
            if (string.IsNullOrWhiteSpace(written)) return;

            if (folded.Length > 0) folded.Append(PostSeparator);
            folded.Append(written);
        }
    }
}
