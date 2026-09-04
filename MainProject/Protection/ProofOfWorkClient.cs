using System.Net.Http.Json;

namespace ChaySocial.MainProject.Protection
{
    /// <summary>
    /// Keeps a completed challenge ready so writing never makes anyone wait. It fetches a challenge and solves it
    /// in the background while the person is still reading or typing; by the time they publish, the answer is
    /// already in hand. Only when they write faster than the machine solves does anyone actually wait.
    /// </summary>
    /// <param name="httpClient"> Client pointed at the server that issues challenges. </param>
    public sealed class ProofOfWorkClient(HttpClient httpClient)
    {
        /// <summary> Answers held ready for the next writes, so a burst of posts does not queue behind one search. </summary>
        readonly Queue<ProofSolution> _ready = [];

        readonly Lock _gate = new();

        /// <summary> Guards the refill so two callers cannot solve the same challenge twice over. </summary>
        readonly SemaphoreSlim _refillGate = new(1, 1);

        /// <summary> How many write answers are kept ready. </summary>
        const int ReadyAnswerTarget = 2;

        /// <summary> Answer the next write must spend instead of a prepared one, set while an account is being created. </summary>
        string? _reservedAnswer;

        /// <summary>
        /// Solves the account challenge and holds the answer for the very next write, which is the write that
        /// brings the account into being. Kept here rather than in the store so the store stays unaware that
        /// different writes can cost different amounts.
        /// </summary>
        /// <param name="onAttempt"> Reports attempts as the search runs, for a progress display. </param>
        /// <param name="cancellationToken"> Abandons the search. </param>
        /// <returns> True once an answer is reserved; false when the server refused to issue a challenge. </returns>
        public async Task<bool> ReserveAccountAnswerAsync(Action<long>? onAttempt = null, CancellationToken cancellationToken = default)
        {
            string? answer = await SolveAccountAnswerAsync(onAttempt, cancellationToken);
            _reservedAnswer = answer;
            return answer is not null;
        }

        /// <summary>
        /// Starts filling the ready queue without making the caller wait. Called when a screen that can write
        /// opens, so the answer is there before the writer needs it.
        /// </summary>
        public void PrepareInBackground()
        {
            if (CountReady() >= ReadyAnswerTarget) return;

            _ = FillQuietlyAsync();
        }

        /// <summary>
        /// Hands over an answer for one write, solving on the spot only when none was prepared.
        /// </summary>
        /// <param name="cancellationToken"> Abandons the wait. </param>
        /// <returns> The header value to send with the write, or null when the server refused to issue a challenge. </returns>
        public async Task<string?> TakeWriteAnswerAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _reservedAnswer, null) is string reserved) return reserved;

            lock (_gate)
            {
                if (_ready.Count > 0)
                {
                    string prepared = ProofRoutes.FormatSolution(_ready.Dequeue());
                    PrepareInBackground();
                    return prepared;
                }
            }

            ProofSolution? solved = await SolveOneAsync(ProofDifficulty.Write, cancellationToken);
            PrepareInBackground();

            return solved is null ? null : ProofRoutes.FormatSolution(solved.Value);
        }

        /// <summary>
        /// Solves the heavier challenge that creating an account costs. Not prepared ahead of time, because it
        /// happens once and the person is watching a screen that explains the wait.
        /// </summary>
        /// <param name="onAttempt"> Reports attempts as the search runs, for a progress display. </param>
        /// <param name="cancellationToken"> Abandons the search. </param>
        /// <returns> The header value to send, or null when the server refused to issue a challenge. </returns>
        public async Task<string?> SolveAccountAnswerAsync(Action<long>? onAttempt = null, CancellationToken cancellationToken = default)
        {
            ProofSolution? solved = await SolveOneAsync(ProofDifficulty.Account, cancellationToken, onAttempt);
            return solved is null ? null : ProofRoutes.FormatSolution(solved.Value);
        }

        /// <summary> Answers currently waiting in the queue. </summary>
        /// <returns> How many prepared answers are held. </returns>
        int CountReady()
        {
            lock (_gate) return _ready.Count;
        }

        /// <summary> Runs a refill without letting its failure escape into an unobserved task. </summary>
        async Task FillQuietlyAsync()
        {
            try
            {
                await FillAsync();
            }
            catch (Exception error)
            {
                Log($"Preparing a proof-of-work answer in the background failed.\n{error}", LogLevel.Warning);
            }
        }

        /// <summary> Solves until the ready queue is back at its target, one challenge at a time. </summary>
        async Task FillAsync()
        {
            await _refillGate.WaitAsync();

            try
            {
                while (CountReady() < ReadyAnswerTarget)
                {
                    ProofSolution? solved = await SolveOneAsync(ProofDifficulty.Write, CancellationToken.None);
                    if (solved is null) return;

                    lock (_gate) _ready.Enqueue(solved.Value);
                }
            }
            finally
            {
                _refillGate.Release();
            }
        }

        /// <summary> Fetches one challenge and solves it. </summary>
        /// <param name="difficultyBits"> Difficulty to ask the server for. </param>
        /// <param name="cancellationToken"> Abandons the request and the search. </param>
        /// <param name="onAttempt"> Reports attempts as the search runs. </param>
        /// <returns> The answer, or null when the challenge could not be fetched. </returns>
        async Task<ProofSolution?> SolveOneAsync(int difficultyBits, CancellationToken cancellationToken, Action<long>? onAttempt = null)
        {
            try
            {
                ProofChallenge? challenge = await httpClient.GetFromJsonAsync<ProofChallenge>(
                    $"{ProofRoutes.Challenge}?{ProofRoutes.DifficultyQueryName}={difficultyBits}",
                    cancellationToken);

                if (challenge is null) return null;

                // Yielding between attempts rather than Task.Run: a browser gives this one thread, the same one
                // that draws, so the only way to stay responsive is to hand it back between attempts.
                return await ProofOfWork.SolveAsync(challenge, onAttempt, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception error)
            {
                Log($"Could not obtain a proof-of-work challenge.\n{error}", LogLevel.Warning);
                return null;
            }
        }
    }
}
