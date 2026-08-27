using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.ResultsGrid.Find;

/// <summary>
/// "Find in Results" (Ctrl+Alt+S) — opens the find window for the results grid, and is also on the grid's
/// right-click menu, next to Grid Aggregates.
///
/// <para>Invoking it again focuses the box and selects the term already in it, so pressing the shortcut a
/// second time is the same gesture as Ctrl+F anywhere else rather than a no-op on an already-open window.</para>
/// </summary>
internal sealed class GridFindCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int CommandId = 0x0c20;

    private readonly AsyncPackage _package;
    private static GridFindCommand _instance;

    private GridFindCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            _instance = new GridFindCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e) => Show(_package);

    /// <summary>Shows the window, brings it to the front and puts the caret in the box. Fire and forget;
    /// failures land in the ActivityLog rather than in the user's way.</summary>
    public static void Show(AsyncPackage package)
    {
        if (package == null)
            return;

        _ = package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                var window = await package.ShowToolWindowAsync(typeof(GridFindToolWindow), 0, create: true, package.DisposalToken);
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

                if (window?.Frame is IVsWindowFrame frame)
                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());

                (window as GridFindToolWindow)?.Control?.FocusSearchBox();
            }
            catch (Exception ex)
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                ActivityLogHelper.LogError(package, "SQLExtended Grid Find", $"Show failed: {ex}");
                System.Diagnostics.Debug.WriteLine($"[SQLExtended] Grid Find show failed: {ex}");
            }
        });
    }
}
