namespace Groundwork.Outcomes
{
    /// <summary>
    /// Outcome of an operation that returns nothing: it either succeeded, or it failed with a reason a caller can
    /// show and an optional exception. Lets services report a handled failure without throwing, so call sites branch
    /// on <see cref="IsSuccess"/> instead of wrapping everything in try/catch.
    /// </summary>
    public readonly record struct Result
    {
        Result(bool isSuccess, string failureReason, Exception? error)
        {
            IsSuccess = isSuccess;
            FailureReason = failureReason;
            Error = error;
        }

        /// <summary> True when the operation completed as intended. A defaulted <see cref="Result"/> is a failure, so an unassigned value never reads as success. </summary>
        public bool IsSuccess { get; }

        /// <summary> Empty on success; otherwise the reason, phrased so it can be shown to a user. </summary>
        public string FailureReason { get; } = string.Empty;

        /// <summary> The exception behind a failure, or null when the failure was not caused by one. </summary>
        public Exception? Error { get; }

        /// <summary> Inverse of <see cref="IsSuccess"/>, so guard clauses read as <c>if (result.IsFailure) return;</c>. </summary>
        public bool IsFailure => !IsSuccess;

        /// <summary> The outcome of an operation that did what it was asked. </summary>
        /// <returns> A successful result. </returns>
        public static Result Success() => new(true, string.Empty, null);

        /// <summary> The outcome of an operation that did not complete. </summary>
        /// <param name="failureReason"> Why it failed, phrased for a user. </param>
        /// <param name="error"> The exception behind the failure, when there was one. </param>
        /// <returns> A failed result carrying the reason. </returns>
        public static Result Failure(string failureReason, Exception? error = null) => new(false, failureReason, error);
    }

    /// <summary>
    /// Outcome of an operation that produces a value. Success carries the value; failure carries the reason and
    /// leaves <see cref="Value"/> null, so a caller can never read a value that was never produced.
    /// </summary>
    /// <typeparam name="TValue"> What the operation produces when it succeeds. </typeparam>
    public readonly record struct Result<TValue>
    {
        Result(Result outcome, TValue? value)
        {
            Outcome = outcome;
            Value = value;
        }

        /// <summary> The success/failure half of this result, shared with the non-generic <see cref="Result"/> so the reason and exception live in one type. </summary>
        public Result Outcome { get; }

        /// <summary> The produced value on success; null on failure. </summary>
        public TValue? Value { get; }

        /// <inheritdoc cref="Result.IsSuccess"/>
        public bool IsSuccess => Outcome.IsSuccess;

        /// <inheritdoc cref="Result.IsFailure"/>
        public bool IsFailure => Outcome.IsFailure;

        /// <inheritdoc cref="Result.FailureReason"/>
        public string FailureReason => Outcome.FailureReason;

        /// <inheritdoc cref="Result.Error"/>
        public Exception? Error => Outcome.Error;

        /// <summary> The outcome of an operation that produced its value. </summary>
        /// <param name="value"> The produced value. </param>
        /// <returns> A successful result carrying <paramref name="value"/>. </returns>
        public static Result<TValue> Success(TValue value) => new(Result.Success(), value);

        /// <summary> The outcome of an operation that produced nothing. </summary>
        /// <param name="failureReason"> Why it failed, phrased for a user. </param>
        /// <param name="error"> The exception behind the failure, when there was one. </param>
        /// <returns> A failed result with no value. </returns>
        public static Result<TValue> Failure(string failureReason, Exception? error = null)
            => new(Result.Failure(failureReason, error), default);

        /// <summary> Forwards a failure from an operation that produced a different value type, so a reason survives a chain of calls without being retyped. </summary>
        /// <param name="failedOutcome"> A failed outcome to carry over; passing a successful one is a caller bug and throws. </param>
        /// <returns> A failed result with the same reason and exception. </returns>
        public static Result<TValue> Propagate(Result failedOutcome)
            => failedOutcome.IsFailure
                ? new(failedOutcome, default)
                : throw new ArgumentException("Only a failed outcome can be propagated.", nameof(failedOutcome));
    }
}

