using System.Security.Cryptography;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// Emptying this device, and the secret that empties it instead of opening it.
    /// </summary>
    /// <remarks>
    /// Encryption stops a server. It does nothing at all for somebody holding the phone and asking for it to be
    /// opened. A second secret that takes the real accounts off the device while opening a believable, boring one
    /// is the difference between an account that is hidden and an account that survives a checkpoint.
    /// <para>
    /// This is not deniable storage and does not pretend to be: somebody who copies the browser's storage before
    /// the duress secret is used still has what was there. What it defends against is the ordinary case — a device
    /// handed over and unlocked while its owner is standing there.
    /// </para>
    /// </remarks>
    public static class PanicService
    {
        /// <summary>
        /// Erases every trace this application keeps on the device and closes the session.
        /// </summary>
        /// <returns> A task that completes once nothing is left. </returns>
        /// <remarks>
        /// Nothing else needs clearing. The writing permit is only ever held in memory and is dropped on sign-out,
        /// and on the server it belongs to an address rather than to a device, so an emptied device carries no
        /// trace of one.
        /// </remarks>
        public static async Task WipeDeviceAsync()
        {
            if (AppServices.LocalStore is null) return;

            foreach (string key in LocalStoreKeys.All) await AppServices.LocalStore.DeleteAsync(key);

            await SessionService.SignOutAsync();
        }

        /// <summary>
        /// Sets the secret that empties this device.
        /// </summary>
        /// <param name="secretText"> The secret, written the way a secret is written. </param>
        /// <returns> True when the secret was one this app could read and the mark is now set. </returns>
        /// <remarks>
        /// Only a digest is stored. A device taken apart yields a hash rather than a working account, so the decoy
        /// cannot be opened by whoever finds the mark — and the mark itself is erased along with everything else
        /// the moment it is used.
        /// </remarks>
        public static async Task<bool> SetDuressMarkAsync(string secretText)
        {
            if (AppServices.LocalStore is null) return false;
            if (!MasterSeedText.TryParse(secretText, out byte[] seed)) return false;

            await AppServices.LocalStore.WriteAsync(LocalStoreKeys.DuressMark, MarkOf(seed));
            return true;
        }

        /// <summary> True when this device has a duress secret set. </summary>
        /// <returns> Whether a mark is on record. </returns>
        public static async Task<bool> HasDuressMarkAsync()
            => AppServices.LocalStore is not null && await AppServices.LocalStore.ReadAsync(LocalStoreKeys.DuressMark) is not null;

        /// <summary> Forgets the duress secret, leaving everything else alone. </summary>
        /// <returns> A task that completes once the mark is gone. </returns>
        public static async Task ForgetDuressMarkAsync()
        {
            if (AppServices.LocalStore is null) return;

            await AppServices.LocalStore.DeleteAsync(LocalStoreKeys.DuressMark);
        }

        /// <summary>
        /// Judges a secret somebody is signing in with, and empties the device when it is the duress one.
        /// </summary>
        /// <param name="secretText"> The secret being offered. </param>
        /// <returns> True when this was the duress secret and the device has been emptied. </returns>
        /// <remarks>
        /// The caller then signs in with the same secret through the ordinary path, so what is on screen a moment
        /// later is a device that looks as though it was set up yesterday with one unremarkable account. Every sign
        /// in goes through here, which means the welcome screen and the account switcher both honour it without
        /// either of them knowing it exists.
        /// </remarks>
        public static async Task<bool> TryEnterDuressAsync(string secretText)
        {
            if (AppServices.LocalStore is null) return false;

            string? mark = await AppServices.LocalStore.ReadAsync(LocalStoreKeys.DuressMark);
            if (mark is null) return false;

            if (!MasterSeedText.TryParse(secretText, out byte[] seed)) return false;

            // Compared in fixed time. The comparison is local and the attacker is holding the device rather than
            // timing it, but a security check that is careless in the small tends to be careless in the large.
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromBase64String(mark),
                    Convert.FromBase64String(MarkOf(seed)))) return false;

            await WipeDeviceAsync();
            return true;
        }

        /// <summary> The stored form of a duress secret: a digest of its seed, never the seed. </summary>
        /// <param name="seed"> The master seed the secret spells out. </param>
        /// <returns> The mark to store and compare against. </returns>
        static string MarkOf(ReadOnlySpan<byte> seed) => Convert.ToBase64String(SHA256.HashData(seed));
    }
}
