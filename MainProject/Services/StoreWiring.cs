using ChaySocial.MainProject.Persistence;
using ChaySocial.MainProject.Protection;

namespace ChaySocial.MainProject.Services
{
    /// <summary>
    /// The one place the stores are pointed at a server. Startup and the settings screen both come through here, so
    /// there is a single answer to "what is this app talking to" rather than one at boot and another after a move.
    /// </summary>
    public static class StoreWiring
    {
        /// <summary>
        /// Points every store at one server.
        /// </summary>
        /// <param name="baseAddress"> Address of the server. </param>
        /// <param name="deviceStore"> Where this device keeps its own things; null to carry over the one already in use. </param>
        /// <param name="remember"> True to keep this address for the next visit; false when it is only where the page came from. </param>
        /// <returns> True when the address was usable and the stores now point at it. </returns>
        /// <remarks>
        /// The device store is carried across untouched: the master seed belongs to the device, not to any server,
        /// and moving must not cost somebody their account. What does not carry across is the writing permit —
        /// every server keeps its own — so it is asked for again here. Without that, somebody who earned a permit
        /// on the new server last week would be told to earn it a second time.
        /// </remarks>
        public static async Task<bool> ApplyAsync(string baseAddress, ILocalStore? deviceStore = null, bool remember = true)
        {
            string address = HomeServerService.Usable(baseAddress);
            if (address.Length == 0) return false;

            HttpClient http = new() { BaseAddress = new Uri(address) };
            ProofOfWorkClient proofOfWork = new(http);

            AppServices.Configure(
                new HttpDocumentStore(http, proofOfWork),
                deviceStore ?? AppServices.LocalStore,
                proofOfWork,
                new HttpBlobStore(http, proofOfWork));

            if (remember) await HomeServerService.SetAsync(address);
            else HomeServerService.NoteInUse(address);

            if (SessionService.IsSignedIn) await proofOfWork.RefreshPermitAsync(SessionService.CurrentAddress);

            return true;
        }
    }
}
