namespace ChaySocial.MainProject.Persistence
{
    /// <summary>
    /// Thrown when the server refuses a write because the account has not earned its writing permit. It is its own
    /// exception rather than a general failure because it is the one refusal a person can act on: they go to their
    /// settings, spend the minutes once, and it never happens again.
    /// </summary>
    public sealed class WritingNotPermittedException() : Exception(DefaultMessage)
    {
        /// <summary> What the exception says when nobody catches it and it reaches a log. </summary>
        const string DefaultMessage = "This account has not earned its writing permit yet.";
    }
}
