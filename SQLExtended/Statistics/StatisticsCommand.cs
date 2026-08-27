using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.Statistics;

/// <summary>
/// Command handler for "Parse Statistics" (Ctrl+K, Ctrl+G): captures the active query window's Messages pane, parses
/// the STATISTICS IO/TIME output, and shows it in the Statistics tool window.
/// </summary>
internal sealed class StatisticsCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int StatisticsCommandId = 0x0d00;

    private readonly AsyncPackage _package;
    private static StatisticsCommand _instance;

    private StatisticsCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, StatisticsCommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new StatisticsCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e) => StatisticsPresenter.Show(_package, activate: true);
}
