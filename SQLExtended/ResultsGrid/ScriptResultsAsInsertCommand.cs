using EnvDTE;
using Microsoft.SqlServer.Management.UI.Grid;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.ResultsGrid;

/// <summary>
/// "Script Results as INSERT" — turns a results grid into a temp-table CREATE + INSERT script and
/// opens it in a new query window. Reached from the results grid's right-click menu (VSCT places
/// our group into SQLEditors' IDM_SQLWB_SQLRESGRID_CONTEXT menu) and from the SQLExtended main menu.
/// Right-clicking a grid scripts that grid; the main menu scripts every grid in the active window.
/// </summary>
internal sealed class ScriptResultsAsInsertCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int CommandId = 0x0c00;

    private readonly AsyncPackage _package;
    private static ScriptResultsAsInsertCommand _instance;

    private ScriptResultsAsInsertCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new ScriptResultsAsInsertCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var grids = new List<GridControl>();
            var focused = ResultsGridReader.GetFocusedGrid();
            if (focused != null)
                grids.Add(focused);
            else
            {
                var dte = Package.GetGlobalService(typeof(SDTE)) as DTE;
                IntPtr hwnd = dte?.ActiveWindow?.HWnd ?? IntPtr.Zero;
                grids.AddRange(ResultsGridReader.FindGridsUnder(hwnd));
            }

            if (grids.Count == 0)
            {
                ShowMessage("No results grid found. Execute a query with results to grid, then try again.", OLEMSGICON.OLEMSGICON_INFO);
                return;
            }

            var resultSets = new List<ResultGridData>();
            long skipped = 0;
            foreach (var grid in grids)
            {
                resultSets.Add(ResultsGridReader.Read(grid, out long totalRows));
                skipped += Math.Max(0, totalRows - ResultsGridReader.MaxRows);
            }

            string script = InsertScriptGenerator.Generate(resultSets);
            if (skipped > 0)
                script = $"-- WARNING: output truncated — {skipped} row(s) beyond the {ResultsGridReader.MaxRows}-row limit were skipped.\r\n" + script;

            OpenInNewQueryWindow(script);
        }
        catch (Exception ex)
        {
            ShowMessage($"Script Results as INSERT failed: {ex.Message}", OLEMSGICON.OLEMSGICON_WARNING);
        }
    }

    private static void OpenInNewQueryWindow(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var dte = Package.GetGlobalService(typeof(SDTE)) as DTE;
        if (dte == null)
            return;

        // Same pattern as SQL History: open via a temp .sql file so SSMS treats it as a query window.
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SQLExtended", "Scripts");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"ResultsAsInsert_{DateTime.Now:yyyyMMdd_HHmmss_fff}.sql");
            System.IO.File.WriteAllText(path, text ?? "", new System.Text.UTF8Encoding(false));
            dte.ItemOperations.OpenFile(path, EnvDTE.Constants.vsViewKindTextView);
        }
        catch
        {
            try { System.Windows.Clipboard.SetText(text); } catch { }
        }
    }

    private void ShowMessage(string message, OLEMSGICON icon)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(_package, message, "SQLExtended",
            icon, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
