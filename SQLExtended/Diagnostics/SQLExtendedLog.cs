using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace SQLExtended.Diagnostics;

/// <summary>
/// The extension's session log: where the failures that this codebase deliberately swallows go, so they
/// can be read.
///
/// <para><b>Why it exists.</b> Almost every catch block in this extension is a soft one, on purpose — a
/// cache load that throws must not take SSMS with it, a dashboard section that fails must cost one tab.
/// The cost is that the reason disappears. The two places it could have gone do not work here:
/// <c>Debug.WriteLine</c> is <c>[Conditional("DEBUG")]</c> and so is not compiled into a Release VSIX at
/// all, and <c>ActivityLog.xml</c> is only written when SSMS was launched with <c>/log</c> — which it was
/// not, on the machine where the problem is happening. The same reasoning already produced
/// <see cref="EnvTabs.EnvTabsDiagnostics"/> for one subsystem; this is that, for all of them.</para>
///
/// <para><b>It is off unless asked for, and it is a session.</b> Nothing is captured while
/// <see cref="Enabled"/> is false, and what is captured lives in memory and dies with SSMS. That is what
/// <see cref="Settings.SQLExtendedSettings.DiagnosticLogEnabled"/> buys — not a verbosity level.
/// <see cref="DiagnosticLogToFile"/> is the separate opt-in that also puts the lines somewhere that
/// survives, under <see cref="LogDirectory"/>, for a problem that has to be sent to someone.</para>
///
/// <para><b>Nothing in here may throw, and nothing in here may read settings.</b> The first because every
/// caller is already handling a failure and a logger that throws turns a warning into a crash — every
/// method swallows its own errors, including the file sink's. The second because most callers are on a
/// worker thread and <c>SQLExtendedSettings.Current</c> must not be faulted in from one (the same rule
/// <c>PerfRecentDumpDays</c> follows): the flags are pushed in by <see cref="Configure"/> from the UI
/// thread instead, and read as plain volatile bools on the hot path.</para>
/// </summary>
internal static class SQLExtendedLog
{
    /// <summary>
    /// The ring itself. Public so the Diagnostics tab can bind and subscribe to it; it is the only state
    /// here that outlives a call.
    /// </summary>
    public static readonly DiagnosticLogBuffer Buffer = new();

    private static volatile bool _enabled;
    private static volatile bool _toFile;

    // Written every occurrence, so the file needs its own bounds. The ring collapses repeats; a file
    // being grepped later is more useful with the whole timeline in it, and a tight failing loop
    // (completion runs per keystroke) is what the cap is for rather than the disk.
    private const int MaxFileLinesPerSession = 20_000;
    private const int PruneAfterDays = 7;

    private static readonly object FileGate = new();
    private static int _fileLines;
    private static bool _inFileFailure;

    /// <summary>Whether anything is being captured at all.</summary>
    public static bool Enabled => _enabled;

    /// <summary>Whether entries are also being appended to <see cref="CurrentFilePath"/>.</summary>
    public static bool FileEnabled => _toFile;

    /// <summary>
    /// <c>%APPDATA%\SQLExtended\SSMS\logs</c> — beside the settings and history files, which is where the
    /// dialog's "Open Log Folder" button already knows how to look.
    /// </summary>
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SQLExtended", "SSMS", "logs");

    /// <summary>One file per day. Rolling by name rather than by size keeps "what happened on Tuesday" answerable.</summary>
    public static string CurrentFilePath => Path.Combine(
        LogDirectory, "sqlextended-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");

    /// <summary>
    /// Applies the user's choices. Call once at package load and again whenever the settings dialog saves —
    /// <b>on the UI thread</b>, since it is the only thing here allowed to have read the settings.
    /// </summary>
    public static void Configure(bool enabled, bool toFile)
    {
        bool wasEnabled = _enabled;

        _enabled = enabled;
        _toFile = enabled && toFile;

        if (!enabled) return;

        if (_toFile)
        {
            // Reset the session's file budget and the sticky failure, so turning the option off and on
            // again is a real retry rather than a no-op.
            _fileLines = 0;
            TryPrepareFile();
        }

        if (!wasEnabled)
        {
            Info("Diagnostics", _toFile
                ? "Session log started. Also writing to " + CurrentFilePath
                : "Session log started (memory only).");
        }
    }

    public static void Error(string source, string message, Exception ex = null) => Write(DiagnosticLevel.Error, source, message, ex);

    public static void Warning(string source, string message, Exception ex = null) => Write(DiagnosticLevel.Warning, source, message, ex);

    public static void Info(string source, string message) => Write(DiagnosticLevel.Info, source, message, null);

    private static void Write(DiagnosticLevel level, string source, string message, Exception ex)
    {
        if (!_enabled) return;

        try
        {
            var entry = Buffer.Add(level, source, message, Describe(ex), DateTime.Now);
            if (entry != null && _toFile)
                AppendToFile(entry, message, source, level, ex);
        }
        catch
        {
            // A logger that throws turns the caller's handled failure into an unhandled one.
        }
    }

    /// <summary>
    /// The exception, its inner chain and the outermost stack, as text.
    ///
    /// <para>The chain matters more than it looks: reflection failures arrive wrapped in
    /// <c>TargetInvocationException</c>, whose own message ("Exception has been thrown by the target of an
    /// invocation") says nothing at all — the same problem <c>JobDialogLauncher</c> unwraps by hand. A
    /// <c>SqlException</c> behind an aggregate is the other common case.</para>
    /// </summary>
    public static string Describe(Exception ex)
    {
        if (ex == null) return "";

        try
        {
            var sb = new StringBuilder();
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (sb.Length > 0) sb.AppendLine(" ---> ");
                sb.Append(current.GetType().FullName).Append(": ").Append(current.Message);

                // SQL Server's own number is what a permission or login failure is actually looked up by.
                if (current is Microsoft.Data.SqlClient.SqlException sql)
                    sb.Append(" (Error ").Append(sql.Number.ToString(CultureInfo.InvariantCulture))
                      .Append(", State ").Append(sql.State.ToString(CultureInfo.InvariantCulture))
                      .Append(", Class ").Append(sql.Class.ToString(CultureInfo.InvariantCulture)).Append(')');
            }

            if (!string.IsNullOrEmpty(ex.StackTrace))
                sb.AppendLine().Append(ex.StackTrace);

            return sb.ToString();
        }
        catch
        {
            return ex.GetType().FullName;
        }
    }

    // --- File sink ---

    private static void TryPrepareFile()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            Prune();
        }
        catch (Exception ex)
        {
            FileSinkFailed(ex);
        }
    }

    /// <summary>
    /// Drops log files older than <see cref="PruneAfterDays"/>. Filters on the extension rather than
    /// trusting a <c>*.log</c> wildcard, for the reason the schema export documents: Windows matches
    /// three-character extension patterns against longer ones, so the pattern alone would also catch a
    /// <c>.logsomething</c>.
    /// </summary>
    private static void Prune()
    {
        var cutoff = DateTime.UtcNow.AddDays(-PruneAfterDays);

        foreach (string path in Directory.GetFiles(LogDirectory, "sqlextended-*.log"))
        {
            try
            {
                if (!path.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
                if (File.GetLastWriteTimeUtc(path) < cutoff) File.Delete(path);
            }
            catch
            {
                // A file someone has open in an editor is not a reason to stop pruning the rest.
            }
        }
    }

    private static void AppendToFile(DiagnosticLogEntry entry, string message, string source, DiagnosticLevel level, Exception ex)
    {
        if (Interlocked.Increment(ref _fileLines) > MaxFileLinesPerSession)
        {
            if (_toFile)
            {
                _toFile = false;
                Buffer.Add(DiagnosticLevel.Warning, "Diagnostics",
                    $"Stopped writing to the log file after {MaxFileLinesPerSession:N0} lines this session. The session log above is still recording.",
                    "", DateTime.Now);
            }
            return;
        }

        try
        {
            // Composed fresh rather than reusing entry.ToText(): a repeat has already been collapsed in
            // the ring, and the file wants the occurrence.
            string line = new DiagnosticLogEntry(DateTime.Now, level, source, message, Describe(ex)).ToText();

            lock (FileGate)
                File.AppendAllText(CurrentFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception failure)
        {
            FileSinkFailed(failure);
        }
    }

    /// <summary>
    /// Turns the file sink off and says why, once. The session log keeps running — losing the file is not
    /// a reason to lose the log, and a sink that silently stopped writing would be read as "no errors
    /// since".
    /// </summary>
    private static void FileSinkFailed(Exception ex)
    {
        if (_inFileFailure) return;

        _inFileFailure = true;
        try
        {
            _toFile = false;
            Buffer.Add(DiagnosticLevel.Warning, "Diagnostics",
                "Could not write the log file — file logging is off for this session. The session log below is unaffected.",
                Describe(ex), DateTime.Now);
        }
        catch
        {
            // Nothing left to report it with.
        }
        finally
        {
            _inFileFailure = false;
        }
    }
}
