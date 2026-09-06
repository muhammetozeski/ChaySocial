using ChaySocial.MainProject.Cryptography;
using ChaySocial.MainProject.DataModels;
using ChaySocial.MainProject.Events;
using ChaySocial.MainProject.Identity;
using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Text;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// One account standing behind another, in its own name and with its own signature on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Between "this signature verifies" and "this is the person I know" there is nothing today. An address
    /// commits to its keys and proves every post came from whoever holds them; it never proves who that is, and
    /// a display name says so about itself. A vouch is somebody putting their own name behind the answer —
    /// signed so it cannot be invented, and withdrawable so it does not stick forever.
    /// </para>
    /// <para>
    /// A vouch that does not verify is never drawn. That is the whole difference between this and a display name:
    /// the server holds these documents and could write one for any account, and a forged one falls out here
    /// rather than appearing on somebody's profile in a stranger's name.
    /// </para>
    /// </remarks>
    public static class VouchService
    {
        /// <summary> Vouches read for one account in one pass. </summary>
        public const int VouchPageSize = 50;

        /// <summary> What these signatures are over, so one can never be replayed as another kind of signature. </summary>
        static readonly byte[] VouchSignatureDomain = "ChaySocial/Vouch/v1"u8.ToArray();

        /// <summary>
        /// Records one account standing behind another, and tells the account it is about.
        /// </summary>
        /// <param name="voucher"> The unlocked account doing the vouching. </param>
        /// <param name="subjectAddress"> Address of the account being vouched for. </param>
        /// <param name="knownAsName"> What the voucher calls them; trimmed, and refused when too long. </param>
        /// <returns> The stored vouch, or null when it was refused. </returns>
        public static async Task<VouchData?> VouchAsync(PrivateIdentity voucher, string subjectAddress, string knownAsName)
        {
            string voucherAddress = voucher.Public.Address;
            if (!IsVouchable(voucherAddress, subjectAddress)) return null;

            string trimmed = knownAsName.Trim();
            if (trimmed.Length == 0 || trimmed.Length > VouchData.MaximumKnownAsLength) return null;

            long createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            byte[] transcript = BuildTranscript(voucherAddress, subjectAddress, trimmed, createdAt);

            VouchData vouch = new()
            {
                VoucherAddress = voucherAddress,
                SubjectAddress = subjectAddress,
                KnownAsName = trimmed,
                CreatedAtUnixMs = createdAt,
                Signature = Convert.ToBase64String(voucher.Sign(transcript))
            };

            await AppServices.Documents.WriteAsync(vouch.Id, vouch);

            // No preview: the name is free text the voucher chose, and an alert line is not the place for one
            // account to write whatever it likes into another account's screen.
            await NotificationService.NotifyAsync(subjectAddress, voucherAddress, NotificationKind.Vouch);

            MainEvents.Trigger(MainEvents.Names.VouchChanged, subjectAddress);

            return vouch;
        }

        /// <summary> Takes a vouch back. </summary>
        /// <param name="voucher"> The unlocked account withdrawing it. </param>
        /// <param name="subjectAddress"> Account it was about. </param>
        /// <returns> A task that completes once it is gone. </returns>
        public static async Task WithdrawAsync(PrivateIdentity voucher, string subjectAddress)
        {
            if (!IsVouchable(voucher.Public.Address, subjectAddress)) return;

            await AppServices.Documents.DeleteAsync(VouchData.IdFor(voucher.Public.Address, subjectAddress));

            MainEvents.Trigger(MainEvents.Names.VouchChanged, subjectAddress);
        }

        /// <summary> True when one account has already vouched for another. </summary>
        /// <param name="voucherAddress"> The voucher. </param>
        /// <param name="subjectAddress"> The subject. </param>
        /// <returns> True while the document exists. </returns>
        public static async Task<bool> HasVouchedAsync(string voucherAddress, string subjectAddress)
            => await AppServices.Documents.ReadAsync(VouchData.IdFor(voucherAddress, subjectAddress)) is not null;

        /// <summary> Reads every vouch stored for one account, newest first, checked or not. </summary>
        /// <param name="subjectAddress"> The account being vouched for. </param>
        /// <param name="limit"> Largest number to return. </param>
        /// <returns> The stored vouches. </returns>
        public static async Task<IReadOnlyList<VouchData>> ReadForSubjectAsync(string subjectAddress, int limit = VouchPageSize)
        {
            if (subjectAddress.Length == 0) return [];

            DocumentQuery<VouchData> query = new DocumentQuery<VouchData>()
                .WithMatch(VouchData.SubjectField, subjectAddress)
                .WithSort(VouchData.CreatedAtField, descending: true)
                .WithLimit(limit);

            return (await AppServices.Documents.QueryAsync(query)).Documents;
        }

        /// <summary>
        /// Reads the vouches one reader should actually be shown for one account: the ones whose signature holds,
        /// left by accounts they have not blocked and who have not blocked them.
        /// </summary>
        /// <param name="viewerAddress"> The reader. </param>
        /// <param name="subjectAddress"> The account being looked at. </param>
        /// <returns> The vouches worth drawing, newest first, each with the profile behind it. </returns>
        public static async Task<IReadOnlyList<(VouchData Vouch, ProfileData Voucher)>> ReadVisibleForSubjectAsync(
            string viewerAddress,
            string subjectAddress)
        {
            IReadOnlyList<VouchData> stored = await ReadForSubjectAsync(subjectAddress);
            if (stored.Count == 0) return [];

            HashSet<string> hidden = await ReadHiddenAsync(viewerAddress);

            VouchData[] candidates = [.. stored.Where(vouch => !hidden.Contains(vouch.VoucherAddress))];
            if (candidates.Length == 0) return [];

            ProfileData?[] profiles = await Task.WhenAll(
                candidates.Select(vouch => ProfileService.ReadAsync(vouch.VoucherAddress)));

            List<(VouchData, ProfileData)> shown = new(candidates.Length);
            for (int index = 0; index < candidates.Length; index++)
            {
                // Checked against the profile the voucher publishes, not against the document. A vouch the server
                // wrote falls out here, which is the only reason this is worth more than a display name.
                if (profiles[index] is ProfileData profile && Verify(candidates[index], profile))
                {
                    shown.Add((candidates[index], profile));
                }
            }

            return shown;
        }

        /// <summary>
        /// Checks that a vouch really was made by the account it names.
        /// </summary>
        /// <param name="vouch"> The vouch to check. </param>
        /// <param name="voucherProfile"> Profile of the account it names, or null when it could not be read. </param>
        /// <returns> True when the signature holds against the key that account publishes. </returns>
        public static bool Verify(VouchData vouch, ProfileData? voucherProfile)
        {
            if (voucherProfile is null || voucherProfile.Address != vouch.VoucherAddress) return false;
            if (vouch.Signature.Length == 0) return false;

            try
            {
                byte[] signingKey = Convert.FromBase64String(voucherProfile.SigningPublicKey);
                byte[] encryptionKey = Convert.FromBase64String(voucherProfile.EncryptionPublicKey);

                // The address commits to the keys, so checking that first is what stops somebody publishing a
                // profile full of their own keys under another account's name and vouching in it.
                if (!AppCryptography.Addresses.Matches(voucherProfile.Address, signingKey, encryptionKey)) return false;

                PublicIdentity owner = new(voucherProfile.Address, signingKey, encryptionKey);
                byte[] transcript = BuildTranscript(
                    vouch.VoucherAddress, vouch.SubjectAddress, vouch.KnownAsName, vouch.CreatedAtUnixMs);

                return AppCryptography.Identities.Verify(transcript, Convert.FromBase64String(vouch.Signature), owner);
            }
            catch (FormatException error)
            {
                Log($"Vouch by '{vouch.VoucherAddress}' carries malformed base64.\n{error}", LogLevel.Warning);
                return false;
            }
        }

        /// <summary> Builds the exact bytes a voucher signs and a reader verifies. </summary>
        /// <param name="voucherAddress"> The voucher. </param>
        /// <param name="subjectAddress"> The subject. </param>
        /// <param name="knownAsName"> What they are called, already trimmed. </param>
        /// <param name="createdAtUnixMs"> When it was made. </param>
        /// <returns> The transcript to sign. </returns>
        /// <remarks>
        /// The time is inside, so a vouch cannot be re-dated under a signature that still holds; and both
        /// addresses are, so one cannot be lifted onto a different account.
        /// </remarks>
        static byte[] BuildTranscript(string voucherAddress, string subjectAddress, string knownAsName, long createdAtUnixMs)
        {
            TranscriptWriter transcript = new();
            transcript.WriteBytes(VouchSignatureDomain);
            transcript.WriteText(voucherAddress);
            transcript.WriteText(subjectAddress);
            transcript.WriteText(knownAsName);
            transcript.WriteInt64(createdAtUnixMs);

            return transcript.ToArray();
        }

        /// <summary> Accounts whose vouches this reader should not be shown, in either direction of a block. </summary>
        /// <param name="viewerAddress"> The reader. </param>
        /// <returns> Addresses to leave out. </returns>
        static async Task<HashSet<string>> ReadHiddenAsync(string viewerAddress)
        {
            if (viewerAddress.Length == 0) return [];

            Task<IReadOnlyList<string>> blocked = ModerationService.ReadBlockedAddressesAsync(viewerAddress);
            Task<IReadOnlyList<string>> blockedBy = ModerationService.ReadBlockedByAddressesAsync(viewerAddress);

            await Task.WhenAll(blocked, blockedBy);

            return [.. await blocked, .. await blockedBy];
        }

        /// <summary> True when one account may vouch for another at all. </summary>
        /// <param name="voucherAddress"> The voucher. </param>
        /// <param name="subjectAddress"> The subject. </param>
        /// <returns> False for a blank address and for vouching for yourself. </returns>
        static bool IsVouchable(string voucherAddress, string subjectAddress)
            => voucherAddress.Length > 0 && subjectAddress.Length > 0 && voucherAddress != subjectAddress;
    }
}
