using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Identity
{
    /// <summary>
    /// Drawing seeds until one produces an address that begins with the letters somebody asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Identity here is the address itself, and until now it was dealt out like a hand of cards. Spending your own
    /// processor to make your cryptographic name start with letters you chose is self-expression at the deepest
    /// layer this application has — and it happens before the account exists, so no server ever learns that a
    /// search took place, or what was searched for, or how long it took.
    /// </para>
    /// <para>
    /// There is no shortcut and there is not meant to be one: each attempt derives a whole identity and hashes it,
    /// which is exactly the work an attacker would have to do to grind somebody else's address. The cost is the
    /// point.
    /// </para>
    /// </remarks>
    public static class ChosenAddressSearch
    {
        /// <summary>
        /// Most letters somebody may ask for. Each letter multiplies the work by the size of the alphabet, so this
        /// is set from what a browser measurably manages rather than from what sounds generous.
        /// </summary>
        /// <remarks>
        /// Measured in the browser at 5,0 candidates a second, because every candidate is a whole ML-DSA-65 and
        /// ML-KEM-768 key generation and the address commits to both. That puts one letter at about six seconds
        /// and two at about three and a half minutes on average; three would be roughly an hour and three
        /// quarters, which is not a wait anybody would sit through, so two is where the field stops.
        /// </remarks>
        public const int ChosenLettersMaximumLength = 2;

        /// <summary> How many candidates are tried between handing the thread back. </summary>
        /// <remarks>
        /// WebAssembly gives one thread, so a loop that never yields freezes the page and its own progress line
        /// with it. Yielding in groups rather than every attempt is what keeps a hidden tab from paying the
        /// browser's throttling penalty on every single candidate.
        /// </remarks>
        const int AttemptsBetweenYields = 4;

        /// <summary> True when these letters are ones an address could actually begin with. </summary>
        /// <param name="letters"> The letters somebody typed. </param>
        /// <returns> Whether a search for them could ever finish. </returns>
        /// <remarks>
        /// An address is base32 after its prefix, so a letter outside that alphabet — or too many letters — would
        /// send the search off after something that cannot exist, and it would look like slowness rather than like
        /// a mistake.
        /// </remarks>
        public static bool IsSearchable(string letters)
        {
            string wanted = Normalise(letters);
            if (wanted.Length == 0 || wanted.Length > ChosenLettersMaximumLength) return false;

            foreach (char letter in wanted)
            {
                if (!Base32.IsDigit(letter)) return false;
            }

            return true;
        }

        /// <summary>
        /// Draws seeds until one of them names an address beginning with the letters asked for.
        /// </summary>
        /// <param name="wantedLetters"> Letters the address should begin with, after the application's own prefix. </param>
        /// <param name="onAttempt"> Called as the count climbs, so somebody waiting can see it is working. </param>
        /// <param name="cancellationToken"> Cancelled when the search is given up on. </param>
        /// <returns> The master seed that produced the address, or null when the search was given up on. </returns>
        public static async Task<byte[]?> SearchAsync(
            string wantedLetters,
            Action<long>? onAttempt = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsSearchable(wantedLetters)) return null;

            string wanted = AppCryptography.AddressPrefix + Normalise(wantedLetters);
            long attempts = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] candidate = IdentityScheme.CreateMasterSeed();
                attempts++;

                if (AppCryptography.Identities.DerivePublicIdentity(candidate).Address.StartsWith(wanted, StringComparison.Ordinal))
                {
                    onAttempt?.Invoke(attempts);
                    return candidate;
                }

                if (attempts % AttemptsBetweenYields != 0) continue;

                onAttempt?.Invoke(attempts);
                await Task.Yield();
            }

            return null;
        }

        /// <summary> The form letters are searched for in: trimmed, lowercase, and without the prefix if it was typed. </summary>
        /// <param name="letters"> Letters as they were typed. </param>
        /// <returns> What to look for after the prefix. </returns>
        static string Normalise(string letters)
        {
            string trimmed = letters.Trim().ToLowerInvariant();

            // Somebody typing the whole beginning of an address rather than only the part they get to choose is
            // asking for the same thing, so the prefix is taken off instead of searched for twice.
            return trimmed.StartsWith(AppCryptography.AddressPrefix, StringComparison.Ordinal)
                ? trimmed[AppCryptography.AddressPrefix.Length..]
                : trimmed;
        }
    }
}
