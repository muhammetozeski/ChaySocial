namespace ChaySocial.MainProject.Services
{
    /// <summary> One account somebody might want to follow, and exactly why it is being suggested. </summary>
    /// <param name="Address"> The account being suggested. </param>
    /// <param name="ConnectingAddresses"> The reader's own people who already follow it. </param>
    public readonly record struct NeighbourCandidate(string Address, IReadOnlyList<string> ConnectingAddresses);

    /// <summary>
    /// Finding people two steps out, ordered by one number the reader can recount by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only suggestion a platform can make with nothing hidden in it. Every name on the list is there
    /// because accounts the reader themselves chose already follow it; the row says which ones, by name; and the
    /// order is that count and nothing else. Nothing is promoted, nothing is bought, and a suspicious reader can
    /// walk the stored follow records and arrive at the same list.
    /// </para>
    /// <para>
    /// A block holds here as it holds everywhere else. A suggestion that could name somebody the reader blocked —
    /// or somebody who blocked them — would make suggesting a way around a block.
    /// </para>
    /// </remarks>
    public static class NeighbourService
    {
        /// <summary> How many of the reader's own accounts the second circle is built from. </summary>
        public const int FirstCircleAccounts = 200;

        /// <summary> How many accounts each of those is read as following. </summary>
        public const int FollowingReadPerNeighbour = 200;

        /// <summary> How many of those lists are read at once, so following two hundred people is not two hundred requests at once. </summary>
        const int AccountsReadAtOnce = 8;

        /// <summary> Reads the accounts two steps out from a reader. </summary>
        /// <param name="viewerAddress"> The reader. </param>
        /// <param name="limit"> Largest number of suggestions to return. </param>
        /// <returns> Suggestions, most-connected first, each carrying the reader's own accounts that lead to it. </returns>
        public static async Task<IReadOnlyList<NeighbourCandidate>> ReadSecondCircleAsync(string viewerAddress, int limit)
        {
            if (string.IsNullOrEmpty(viewerAddress) || limit <= 0) return [];

            IReadOnlyList<string> firstCircle = await FollowService.ReadFollowingAsync(viewerAddress, FirstCircleAccounts);
            if (firstCircle.Count == 0) return [];

            HashSet<string> hidden = await ReadHiddenAsync(viewerAddress);
            HashSet<string> alreadyKnown = [viewerAddress, .. firstCircle];

            Dictionary<string, List<string>> leadsTo = [];

            foreach (string[] batch in Batches(firstCircle))
            {
                IReadOnlyList<string>[] theirFollowing = await Task.WhenAll(
                    batch.Select(address => FollowService.ReadFollowingAsync(address, FollowingReadPerNeighbour)));

                for (int index = 0; index < batch.Length; index++)
                {
                    foreach (string candidate in theirFollowing[index])
                    {
                        if (alreadyKnown.Contains(candidate) || hidden.Contains(candidate)) continue;

                        if (!leadsTo.TryGetValue(candidate, out List<string>? through))
                        {
                            through = [];
                            leadsTo[candidate] = through;
                        }

                        if (!through.Contains(batch[index], StringComparer.Ordinal)) through.Add(batch[index]);
                    }
                }
            }

            // Ordered by the count and nothing else. A second score, or a tie-breaker the row does not show, would
            // make the number on screen stop being the whole reason.
            return
            [
                .. leadsTo
                    .Select(entry => new NeighbourCandidate(entry.Key, entry.Value))
                    .OrderByDescending(candidate => candidate.ConnectingAddresses.Count)
                    .Take(limit)
            ];
        }

        /// <summary> Accounts that must not be suggested: the ones this reader blocked, and the ones that blocked them. </summary>
        /// <param name="viewerAddress"> The reader. </param>
        /// <returns> Addresses to leave out. </returns>
        static async Task<HashSet<string>> ReadHiddenAsync(string viewerAddress)
        {
            Task<IReadOnlyList<string>> blocked = ModerationService.ReadBlockedAddressesAsync(viewerAddress);
            Task<IReadOnlyList<string>> blockedBy = ModerationService.ReadBlockedByAddressesAsync(viewerAddress);

            await Task.WhenAll(blocked, blockedBy);

            return [.. await blocked, .. await blockedBy];
        }

        /// <summary> Cuts a list into the groups its reads are made in. </summary>
        /// <param name="addresses"> The accounts to read. </param>
        /// <returns> Groups of at most <see cref="AccountsReadAtOnce"/>. </returns>
        static IEnumerable<string[]> Batches(IReadOnlyList<string> addresses)
        {
            for (int start = 0; start < addresses.Count; start += AccountsReadAtOnce)
            {
                yield return [.. addresses.Skip(start).Take(AccountsReadAtOnce)];
            }
        }
    }
}
