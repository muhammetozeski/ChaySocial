using Groundwork.Observability;

namespace Groundwork.Diagnostics
{
    /// <summary>
    /// Where every Groundwork subsystem reports what it did and what broke. Groundwork deliberately owns no logging
    /// implementation: it publishes <see cref="DiagnosticEntry"/> values on <see cref="Entries"/> and the host app
    /// subscribes to route them into whatever logger it already has. Until something subscribes, entries still reach
    /// the debugger so nothing is silently lost.
    /// </summary>
    public static class DiagnosticLog
    {
        /// <summary> Subscribe here to receive everything Groundwork reports. The channel does not report its own subscriber failures, which would re-enter this log. </summary>
        public static readonly EventChannel<DiagnosticEntry> Entries = new(reportSubscriberFailures: false);

        /// <summary> Entries quieter than this are dropped before they are published. Raise it in a release build to silence tracing. </summary>
        public static DiagnosticSeverity MinimumSeverity { get; set; } = DiagnosticSeverity.Debug;

        /// <summary> Publishes one entry to every subscriber, or to the debugger when nothing is subscribed. </summary>
        /// <param name="severity"> How loud the line is; dropped when quieter than <see cref="MinimumSeverity"/>. </param>
        /// <param name="source"> Type or subsystem writing the line. </param>
        /// <param name="message"> The human-readable text. </param>
        /// <param name="error"> Exception behind the line when writing from a catch block. </param>
        public static void Write(DiagnosticSeverity severity, string source, string message, Exception? error = null)
        {
            if (severity < MinimumSeverity) return;

            DiagnosticEntry entry = new(severity, source, message, error);

            if (Entries.SubscriberCount == 0)
            {
                System.Diagnostics.Debug.WriteLine(entry);
                return;
            }

            Entries.Publish(entry);
        }
    }
}

