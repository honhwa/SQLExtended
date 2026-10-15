using EnvDTE;
using EnvDTE80;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQLExtended.Diagnostics;
using SQLExtended.Settings;
using System;
using System.Linq;
using System.Text;

namespace SQLExtended.Safety;

/// <summary>
/// Asks before SSMS executes an UPDATE or DELETE with no WHERE clause, and cancels the execution on No.
///
/// <para>Hooks the shell's own <c>Query.Execute</c> command (F5, Ctrl+E, the toolbar's Execute) through
/// <see cref="CommandEvents.BeforeExecute"/>, which can cancel it. The command's guid and id are resolved by name
/// at startup rather than hard-coded — they belong to SSMS's SQL editor package and are not documented — and the
/// session log says whether the hook took. If it did not, the guard is silently absent, which is why that line
/// is a warning.</para>
///
/// <para>Checks what SSMS is about to run: the selection when there is one, otherwise the whole document.
/// A script that does not parse is let through (see <see cref="UnsafeDmlResult.ParseFailed"/>).</para>
/// </summary>
internal static class UnsafeDmlGuard
{
    private const string Source = "UnsafeDmlGuard";
    private const int MaxListed = 8;

    private static IServiceProvider _serviceProvider;

    // DTE event sources are COM objects: an unreferenced one is collected and its handlers stop firing.
    private static CommandEvents _executeEvents;

    public static void Initialize(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _serviceProvider = package;

        var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
        Command command;
        try { command = dte?.Commands.Item("Query.Execute"); }
        catch (ArgumentException) { command = null; }   // Commands.Item throws, rather than returning null, for an unknown name
        if (command == null)
        {
            SQLExtendedLog.Warning(Source, "Query.Execute was not found — UPDATE/DELETE without WHERE will not be checked this session.");
            return;
        }

        _executeEvents = dte.Events.CommandEvents[command.Guid, command.ID];
        _executeEvents.BeforeExecute += OnBeforeExecute;
        SQLExtendedLog.Info(Source, $"Hooked Query.Execute ({command.Guid}, {command.ID}).");
    }

    private static void OnBeforeExecute(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // Never let a failure here block, or crash, an execution: the guard fails open.
        try
        {
            if (!SQLExtendedSettings.Current.UnsafeDmlGuardEnabled) return;

            string sql = GetTextToExecute();
            if (string.IsNullOrWhiteSpace(sql)) return;

            var result = UnsafeDmlAnalyzer.Analyze(sql);
            if (result.ParseFailed)
            {
                SQLExtendedLog.Info(Source, "Script did not parse; executed without the UPDATE/DELETE check.");
                return;
            }
            if (result.Findings.Count == 0) return;

            if (!Confirm(result)) cancelDefault = true;
        }
        catch (Exception ex)
        {
            SQLExtendedLog.Error(Source, "Check failed; execution allowed", ex);
        }
    }

    private static string GetTextToExecute()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
        if (dte?.ActiveDocument?.Object("TextDocument") is not TextDocument doc) return null;

        // SSMS executes only the selection when there is one.
        if (!doc.Selection.IsEmpty) return doc.Selection.Text;
        return doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
    }

    private static bool Confirm(UnsafeDmlResult result)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var text = new StringBuilder();
        text.Append(result.Findings.Count == 1 ? "This statement has no WHERE clause" : $"These {result.Findings.Count} statements have no WHERE clause")
            .Append(" and will affect every row of the table")
            .AppendLine(DescribeConnection() is { } where ? $" on {where}:" : ":")
            .AppendLine();

        foreach (var f in result.Findings.Take(MaxListed))
            text.AppendLine($"  Line {f.Line}: {f.Kind} {f.Target}").AppendLine($"      {f.Snippet}");
        if (result.Findings.Count > MaxListed)
            text.AppendLine($"  … and {result.Findings.Count - MaxListed} more");

        text.AppendLine().Append("Execute anyway?");

        // DEFBUTTON_SECOND puts the focus on No: an Enter pressed out of habit must not run it.
        int answer = VsShellUtilities.ShowMessageBox(_serviceProvider, text.ToString(), "SQLExtended — UPDATE/DELETE without WHERE",
            OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);

        bool run = answer == (int)Microsoft.VisualStudio.VSConstants.MessageBoxResult.IDYES;
        SQLExtendedLog.Info(Source, $"{result.Findings.Count} statement(s) without WHERE — user chose {(run ? "to execute" : "to cancel")}.");
        return run;
    }

    /// <summary>"[db] on SERVER", or null when the window's connection cannot be read.</summary>
    private static string DescribeConnection()
    {
        try
        {
            string connStr = ConnectionHelper.GetActiveConnectionString();
            if (string.IsNullOrEmpty(connStr)) return null;
            var builder = new SqlConnectionStringBuilder(connStr);
            return string.IsNullOrEmpty(builder.InitialCatalog) ? builder.DataSource : $"[{builder.InitialCatalog}] on {builder.DataSource}";
        }
        catch
        {
            return null;
        }
    }
}
