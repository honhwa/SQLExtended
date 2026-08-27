using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.Search;

/// <summary>
/// Command handler that opens the SQL Search tool window (Ctrl+Alt+F).
/// </summary>
internal sealed class SqlSearchCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int SqlSearchCommandId = 0x0400;

    private readonly AsyncPackage _package;
    private static SqlSearchCommand _instance;

    private SqlSearchCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, SqlSearchCommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new SqlSearchCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                var window = await _package.ShowToolWindowAsync(
                    typeof(SqlSearchToolWindow),
                    0,
                    create: true,
                    _package.DisposalToken);

                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (window?.Frame is IVsWindowFrame frame)
                {
                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
                }
            }
            catch (Exception ex)
            {
                // The tool window constructs an AvalonEdit-based control; a missing/blocked
                // ICSharpCode.AvalonEdit.dll or System.Data.SQLite.dll throws here. Surface it
                // instead of failing silently (this RunAsync is fire-and-forget).
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                ActivityLogHelper.LogError(_package, "SQLExtended SQL Search", $"Failed to open SQL Search window: {ex}");
                System.Windows.MessageBox.Show(
                    "SQLExtended for SSMS could not open the SQL Search window.\n\n" +
                    $"{ex.Message}\n\n" +
                    "This usually means a required file (ICSharpCode.AvalonEdit.dll or System.Data.SQLite.dll) is missing, blocked, " +
                    "or conflicts with another installed SSMS extension. Reinstalling SQLExtended for SSMS normally fixes it. " +
                    "Full details are in the SSMS ActivityLog.",
                    "SQLExtended for SSMS",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        });
    }
}
