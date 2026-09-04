using System.Net.Http.Json;

namespace ChaySocial.MainProject.Protection
{
    /// <summary>
    /// Handles the one thing this app charges for: the permit that lets an account write. Nothing else asks anybody
    /// to wait — reading, opening an account, following and pouring somebody a chay all go straight through.
    /// </summary>
    /// <remarks>
    /// The cost is deliberately at one door and paid once. Charging every write meant a second and a half of
    /// somebody's phone burnt per message, paid by everyone who used the app, to inconvenience a farm that is happy
    /// to spend it. Making a thousand posting accounts expensive is the part worth making expensive, and the way to
    /// do that is to charge per account, heavily, exactly once.
    /// </remarks>
    /// <param name="httpClient"> Client pointed at the server that issues challenges and grants permits. </param>
    public sealed class ProofOfWorkClient(HttpClient httpClient)
    {
        /// <summary> Account whose permit is sent with each write, set once the signed-in account is known to hold one. </summary>
        string _permittedAddress = string.Empty;

        /// <summary> True when the signed-in account has been found to hold a permit. </summary>
        public bool HasWritingPermit => _permittedAddress.Length > 0;

        /// <summary> The address a write should name, or empty when the signed-in account may not write. </summary>
        public string PermittedAddress => _permittedAddress;

        /// <summary> Forgets the permit held for the previous account, on sign-out or on switching accounts. </summary>
        public void Forget() => _permittedAddress = string.Empty;

        /// <summary>
        /// Asks the server whether an account already holds a permit, and remembers the answer so every later write
        /// can name it. Called when a session starts, so somebody who paid last week is not asked to pay again.
        /// </summary>
        /// <param name="address"> Account to ask about. </param>
        /// <param name="cancellationToken"> Abandons the request. </param>
        /// <returns> True when that account may write. </returns>
        public async Task<bool> RefreshPermitAsync(string address, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(address))
            {
                Forget();
                return false;
            }

            try
            {
                PermitState? state = await httpClient.GetFromJsonAsync<PermitState>(
                    ProofRoutes.PermitFor(address), cancellationToken);

                _permittedAddress = state?.Granted == true ? address : string.Empty;
                return HasWritingPermit;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // A server that cannot be reached is not the same as a permit that was refused, but either way this
                // account cannot write right now, and the screen that asks for one will say so.
                Log($"Could not read the writing permit for '{address}'.\n{error}", LogLevel.Warning);
                Forget();
                return false;
            }
        }

        /// <summary>
        /// Earns the permit: fetches a challenge, works through it, and claims the permit with the answer. This is
        /// the one long wait in the app, and the caller is expected to be showing progress while it runs.
        /// </summary>
        /// <param name="address"> Account the permit is for. </param>
        /// <param name="onAttempt"> Reports attempts as the search runs, for a progress display. </param>
        /// <param name="cancellationToken"> Abandons the search; nothing is lost but the time already spent. </param>
        /// <returns> True once the permit is granted. </returns>
        public async Task<bool> EarnWritingPermitAsync(
            string address,
            Action<long>? onAttempt = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(address)) return false;

            try
            {
                ProofChallenge? challenge = await httpClient.GetFromJsonAsync<ProofChallenge>(
                    $"{ProofRoutes.Challenge}?{ProofRoutes.DifficultyQueryName}={ProofDifficulty.WritingPermit}",
                    cancellationToken);

                if (challenge is null) return false;

                // Yielding between attempts rather than Task.Run: a browser gives this one thread, the same one
                // that draws, so the only way to keep the progress display moving is to hand it back between them.
                ProofSolution solution = await ProofOfWork.SolveAsync(challenge, onAttempt, cancellationToken);

                HttpResponseMessage response = await httpClient.PostAsJsonAsync(
                    ProofRoutes.Permit,
                    new PermitClaim(address, solution.ChallengeId, solution.Nonce),
                    cancellationToken);

                if (!response.IsSuccessStatusCode) return false;

                _permittedAddress = address;
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception error)
            {
                Log($"Earning a writing permit for '{address}' failed.\n{error}", LogLevel.Error);
                return false;
            }
        }
    }
}
