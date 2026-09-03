namespace Groundwork.Diagnostics
{
    /// <summary> How loud a <see cref="DiagnosticEntry"/> is, from routine tracing up to a failure that broke something. </summary>
    public enum DiagnosticSeverity
    {
        /// <summary> Step-by-step tracing that only matters while debugging. </summary>
        Debug,

        /// <summary> A normal milestone worth seeing in a log: a store connected, an identity unlocked. </summary>
        Info,

        /// <summary> Something unexpected happened but the operation still finished. </summary>
        Warning,

        /// <summary> An operation failed. <see cref="DiagnosticEntry.Error"/> usually carries the exception. </summary>
        Error
    }

    /// <summary>
    /// One line written into <see cref="DiagnosticLog"/>: what happened, who reported it, and the exception if the
    /// report came from a catch block.
    /// </summary>
    /// <param name="Severity"> How loud the line is. </param>
    /// <param name="Source"> Type or subsystem that wrote the line, used to group entries when reading a log. </param>
    /// <param name="Message"> The human-readable text. </param>
    /// <param name="Error"> Exception behind the line, or null when nothing was thrown. </param>
    public readonly record struct DiagnosticEntry(
        DiagnosticSeverity Severity,
        string Source,
        string Message,
        Exception? Error = null)
    {
        /// <summary> Renders the entry as one flat line: <c>[Severity] Source: Message -> Error</c>. </summary>
        /// <returns> The formatted line, with the exception appended only when <see cref="Error"/> is set. </returns>
        public override string ToString()
            => Error is null
                ? $"[{Severity}] {Source}: {Message}"
                : $"[{Severity}] {Source}: {Message} -> {Error}";
    }
}

