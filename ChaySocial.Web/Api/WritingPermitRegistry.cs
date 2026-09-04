using ChaySocial.MainProject.Protection;

namespace ChaySocial.Web.Api
{
    /// <summary>
    /// Remembers which accounts have paid for the right to write. A permit is granted once, kept for good, and
    /// survives a restart, because asking somebody to spend minutes again because the server was redeployed would
    /// be the same as not having granted it.
    /// </summary>
    /// <param name="storagePath"> File the granted addresses are kept in, one per line. </param>
    public sealed class WritingPermitRegistry(string storagePath)
    {
        readonly HashSet<string> _granted = new(StringComparer.Ordinal);
        readonly Lock _gate = new();

        /// <summary> True when this account has a permit. </summary>
        /// <param name="address"> Account to check; an empty address never has one. </param>
        /// <returns> True when it may write. </returns>
        public bool IsGranted(string address)
        {
            if (string.IsNullOrEmpty(address)) return false;

            lock (_gate) return _granted.Contains(address);
        }

        /// <summary> Records a permit and writes it through to disk. Granting one already held changes nothing. </summary>
        /// <param name="address"> Account to grant. </param>
        public void Grant(string address)
        {
            if (string.IsNullOrEmpty(address)) return;

            lock (_gate)
            {
                if (!_granted.Add(address)) return;
            }

            Append(address);
        }

        /// <summary> Reads back every permit granted before the last restart. </summary>
        /// <returns> How many permits were restored. </returns>
        public int RestoreFromDisk()
        {
            if (!File.Exists(storagePath)) return 0;

            lock (_gate)
            {
                foreach (string line in File.ReadAllLines(storagePath))
                {
                    string address = line.Trim();
                    if (address.Length > 0) _granted.Add(address);
                }

                return _granted.Count;
            }
        }

        /// <summary>
        /// Appends one address to the file. A failure here loses the permit across a restart but not within this
        /// run, so it is logged rather than thrown: refusing the grant would be the worse of the two outcomes.
        /// </summary>
        /// <param name="address"> Address to append. </param>
        void Append(string address)
        {
            try
            {
                string? directory = Path.GetDirectoryName(storagePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                File.AppendAllText(storagePath, address + Environment.NewLine);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"Writing permit for '{address}' could not be saved: {error}");
            }
        }
    }

    /// <summary> Publishes the routes that claim and report a writing permit. </summary>
    public static class WritingPermitApi
    {
        /// <summary> Registers both permit routes on the application. </summary>
        /// <param name="app"> Application to register on. </param>
        public static void MapWritingPermitApi(this WebApplication app)
        {
            ProofChallengeRegistry challenges = app.Services.GetRequiredService<ProofChallengeRegistry>();
            WritingPermitRegistry permits = app.Services.GetRequiredService<WritingPermitRegistry>();

            app.MapGet($"{ProofRoutes.Permit}/{{address}}", (string address)
                => Results.Json(new PermitState(permits.IsGranted(address))));

            app.MapPost(ProofRoutes.Permit, (PermitClaim claim) =>
            {
                if (string.IsNullOrWhiteSpace(claim.Address)) return Results.BadRequest();

                // Already granted: answering again is harmless and costs the claimant nothing, which matters
                // because a client that lost its answer mid-flight would otherwise have to pay all over again.
                if (permits.IsGranted(claim.Address)) return Results.Json(new PermitState(true));

                bool paid = challenges.Redeem(
                    new ProofSolution(claim.ChallengeId, claim.Nonce),
                    ProofDifficulty.WritingPermit,
                    DateTimeOffset.UtcNow);

                if (!paid) return Results.StatusCode(StatusCodes.Status402PaymentRequired);

                permits.Grant(claim.Address);
                return Results.Json(new PermitState(true));
            });
        }
    }
}
