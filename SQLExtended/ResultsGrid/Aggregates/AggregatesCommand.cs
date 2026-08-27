using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.ResultsGrid.Aggregates;

/// <summary>
/// "Grid Aggregates" (Ctrl+Alt+G) — opens the aggregates window for the results grid. Also on the
/// results grid's right-click menu, which is where it is most likely to be wanted: the user has just
/// selected a range and wants its total.
///
/// Opening the window is all this does. From then on the window follows the selection itself
/// (<see cref="GridAggregatesWatcher"/>), so there is no "compute now" command to re-invoke.
/// </summary>
internal sealed class AggregatesCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int CommandId = 0x0c10;

    private readonly AsyncPackage _package;
    private static AggregatesCommand _instance;

    private AggregatesCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new AggregatesCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e) => Show(_package);

    /// <summary>Shows the window and brings it to the front. Fire and forget; failures land in the
    /// ActivityLog rather than in the user's way.</summary>
    public static void Show(AsyncPackage package)
    {
        if (package == null)
            return;

        _ = package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                var window = await package.ShowToolWindowAsync(typeof(AggregatesToolWindow), 0, create: true, package.DisposalToken);
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

                if (window?.Frame is IVsWindowFrame frame)
                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
            }
            catch (Exception ex)
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                ActivityLogHelper.LogError(package, "SQLExtended Grid Aggregates", $"Show failed: {ex}");
                System.Diagnostics.Debug.WriteLine($"[SQLExtended] Grid Aggregates show failed: {ex}");
            }
        });
    }
}
