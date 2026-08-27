using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;

namespace SQLExtended;

/// <summary>
/// Writes entries to the SSMS ActivityLog (%AppData%\Roaming\Microsoft\SSMS\22.0_&lt;hash&gt;\ActivityLog.xml),
/// which SSMS records when launched with /log. Used to make otherwise-silent failures diagnosable in the field.
/// All methods swallow their own errors — logging must never throw into a caller.
/// </summary>
internal static class ActivityLogHelper
{
    /// <summary>Logs an error entry. Must be called on the UI thread.</summary>
    public static void LogError(IServiceProvider serviceProvider, string source, string message)
    {
        try
        {
            // Mirrored into the session log first. The ActivityLog is only written when SSMS was launched
            // with /log, which on the machine where the problem is happening it was not - so everything
            // already routed here (all four monitoring dashboards, the history window, the job dialog) would
            // otherwise report into a file that does not exist.
            Diagnostics.SQLExtendedLog.Error(source, message);

            ThreadHelper.ThrowIfNotOnUIThread();
            if (serviceProvider?.GetService(typeof(SVsActivityLog)) is IVsActivityLog log)
                log.LogEntry((uint)__ACTIVITYLOG_ENTRYTYPE.ALE_ERROR, source, message);
        }
        catch
        {
            // Logging is best-effort.
        }
    }
}
