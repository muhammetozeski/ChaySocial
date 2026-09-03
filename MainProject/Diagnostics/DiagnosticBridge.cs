using System.Runtime.CompilerServices;
using Groundwork.Diagnostics;

namespace ChaySocial.MainProject.Diagnostics
{
    /// <summary>
    /// Routes everything Groundwork reports into this app's <see cref="Logger"/>. Groundwork ships no logger of its
    /// own so it stays reusable; this file is the one place that decides where its entries end up.
    /// </summary>
    static class DiagnosticBridge
    {
        [ModuleInitializer]
        internal static void Initialize()
            => DiagnosticLog.Entries.Subscribe(static entry => Log(entry.ToString(), ToLoggerLevel(entry.Severity)));

        /// <summary> Maps a Groundwork severity onto the matching <see cref="Logger.LogLevel"/>. </summary>
        /// <param name="severity"> Severity carried by the entry. </param>
        /// <returns> The logger level with the same meaning. </returns>
        static Logger.LogLevel ToLoggerLevel(DiagnosticSeverity severity) => severity switch
        {
            DiagnosticSeverity.Info => Logger.LogLevel.Info,
            DiagnosticSeverity.Warning => Logger.LogLevel.Warning,
            DiagnosticSeverity.Error => Logger.LogLevel.Error,
            _ => Logger.LogLevel.Debug
        };
    }
}
