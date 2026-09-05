using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;

namespace ChaySocial.MainProject.Services
{
    /// <summary> One account this device is carrying, as the switcher lists it. </summary>
    /// <param name="Address"> The account's address. </param>
    /// <param name="Secret"> Its master seed, in the form the device stores it. </param>
    public readonly record struct CarriedAccount(string Address, string Secret);

    /// <summary>
    /// The choices somebody makes about their own anonymity, and the accounts this device is carrying. Everything
    /// here lives on the device and reaches no server: whether to insist on Tor is nobody else's business, and
    /// neither is how many accounts one person keeps.
    /// </summary>
    public static class AnonymityService
    {
        /// <summary> Key the "insist on Tor" choice is kept under on this device. </summary>
        const string TorOnlyKey = "chay.tor-only";

        /// <summary> Key the carried accounts are kept under. </summary>
        const string CarriedAccountsKey = "chay.accounts";

        /// <summary> Separator between two carried accounts in storage. </summary>
        const char AccountSeparator = '\n';

        /// <summary> Value written for a choice that is on. </summary>
        const string OnValue = "1";

        /// <summary> What a Tor address ends with. </summary>
        const string OnionSuffix = ".onion";

        /// <summary> Most accounts one device carries at once, past which the switcher stops being a switcher. </summary>
        public const int MaximumCarriedAccounts = 12;

        /// <summary> True when this device has been told to refuse anything that is not Tor. </summary>
        /// <returns> The stored choice, false when none was made. </returns>
        public static async Task<bool> IsTorOnlyAsync()
            => await AppServices.LocalStore.ReadAsync(TorOnlyKey) == OnValue;

        /// <summary> Remembers whether to insist on Tor. </summary>
        /// <param name="torOnly"> True to refuse anything that is not Tor. </param>
        /// <returns> A task that completes once the choice is stored. </returns>
        public static Task SetTorOnlyAsync(bool torOnly)
            => torOnly
                ? AppServices.LocalStore.WriteAsync(TorOnlyKey, OnValue)
                : AppServices.LocalStore.DeleteAsync(TorOnlyKey);

        /// <summary>
        /// Whether the app is being reached over Tor, judged the only way a page honestly can: by the address it
        /// was served from. A hidden service answers on a <c>.onion</c> host, and nothing served from anywhere
        /// else can claim to be one.
        /// </summary>
        /// <param name="baseUri"> The address this app was loaded from. </param>
        /// <returns> True when it came from a Tor hidden service. </returns>
        /// <remarks>
        /// This deliberately does not ask any server "am I on Tor?". A server's answer would be worth nothing —
        /// it is on the far side of exactly the connection in question, and a hostile one would simply say yes.
        /// </remarks>
        public static bool IsReachedOverTor(string baseUri)
        {
            if (string.IsNullOrEmpty(baseUri)) return false;

            return Uri.TryCreate(baseUri, UriKind.Absolute, out Uri? parsed)
                   && parsed.Host.EndsWith(OnionSuffix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary> Reads the accounts this device is carrying, in the order they were added. </summary>
        /// <returns> The carried accounts, empty when none were kept. </returns>
        public static async Task<IReadOnlyList<CarriedAccount>> ReadCarriedAsync()
        {
            string? stored = await AppServices.LocalStore.ReadAsync(CarriedAccountsKey);
            if (string.IsNullOrEmpty(stored)) return [];

            List<CarriedAccount> carried = [];

            foreach (string line in stored.Split(AccountSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                // A stored secret that no longer parses is skipped rather than thrown over: one corrupted line
                // must not cost somebody every other account they were carrying.
                if (!MasterSeedText.TryParse(line, out byte[] seed)) continue;

                carried.Add(new CarriedAccount(
                    Cryptography.AppCryptography.Identities.Open(seed).Public.Address,
                    line.Trim()));
            }

            return carried;
        }

        /// <summary> Adds an account to the ones this device carries, or does nothing when it is already there. </summary>
        /// <param name="secret"> The account's master seed, as text. </param>
        /// <returns> A task that completes once the account is carried. </returns>
        public static async Task CarryAsync(string secret)
        {
            if (!MasterSeedText.TryParse(secret, out byte[] seed)) return;

            string address = Cryptography.AppCryptography.Identities.Open(seed).Public.Address;
            IReadOnlyList<CarriedAccount> carried = await ReadCarriedAsync();

            if (carried.Any(account => account.Address == address)) return;
            if (carried.Count >= MaximumCarriedAccounts) return;

            await WriteCarriedAsync([.. carried, new CarriedAccount(address, secret.Trim())]);
        }

        /// <summary>
        /// Stops carrying one account. The account itself is untouched and reopens with its secret; this only
        /// takes it off this device.
        /// </summary>
        /// <param name="address"> Address of the account to drop. </param>
        /// <returns> A task that completes once it is gone from this device. </returns>
        public static async Task DropAsync(string address)
        {
            IReadOnlyList<CarriedAccount> carried = await ReadCarriedAsync();
            await WriteCarriedAsync([.. carried.Where(account => account.Address != address)]);
        }

        /// <summary> Forgets every carried account, for somebody handing the device to somebody else. </summary>
        /// <returns> A task that completes once nothing is left. </returns>
        public static Task DropAllAsync() => AppServices.LocalStore.DeleteAsync(CarriedAccountsKey);

        /// <summary> Writes the carried set back to the device. </summary>
        /// <param name="carried"> The accounts to keep. </param>
        /// <returns> A task that completes once they are stored. </returns>
        static Task WriteCarriedAsync(IReadOnlyList<CarriedAccount> carried)
            => carried.Count == 0
                ? AppServices.LocalStore.DeleteAsync(CarriedAccountsKey)
                : AppServices.LocalStore.WriteAsync(
                    CarriedAccountsKey,
                    string.Join(AccountSeparator, carried.Select(account => account.Secret)));
    }
}
