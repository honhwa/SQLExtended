using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Linq;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.ObjectExplorer;

/// <summary>
/// Command that forces an immediate server-grouping pass over the Object Explorer tree, instead of
/// waiting for the poll timer. Handy for testing and for re-applying grouping after changing the
/// Registered Servers layout. Routed from the top-level SQLExtended menu (see MainMenuService).
/// </summary>
internal sealed class RegroupServersCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int RegroupServersCommandId = 0x0b00;

    private readonly AsyncPackage _package;
    private static RegroupServersCommand _instance;

    private RegroupServersCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, RegroupServersCommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new RegroupServersCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var report = ServerGroupFolderService.RegroupNow();
            if (report == null)
            {
                Show("Object Explorer isn't available yet — connect a server and try again.");
                return;
            }

            int moved = report.Count(r => r.Contains("MOVED"));
            int unmatched = report.Count(r => r.Contains("NO MATCH"));
            int errors = report.Count(r => r.Contains("EXCEPTION"));

            Show($"Server grouping pass complete.\n\n" +
                 $"Moved: {moved}\nUnmatched (left at root): {unmatched}\nErrors: {errors}\n\n" +
                 $"Details written to:\n%APPDATA%\\SQLExtended\\SSMS\\server-grouping.log");
        }
        catch (Exception ex)
        {
            Show("Regroup failed: " + ex.Message);
        }
    }

    private void Show(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(_package, message, "SQLExtended — Regroup Servers",
            OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
