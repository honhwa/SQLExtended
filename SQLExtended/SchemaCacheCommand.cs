using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended;

/// <summary>
/// Command handler that opens the Schema Cache tool window (Ctrl+Alt+C),
/// which shows what the shared schema cache currently holds per server.
/// </summary>
internal sealed class SchemaCacheCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int SchemaCacheCommandId = 0x0330;

    private readonly AsyncPackage _package;
    private static SchemaCacheCommand _instance;

    private SchemaCacheCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, SchemaCacheCommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new SchemaCacheCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            var window = await _package.ShowToolWindowAsync(
                typeof(SchemaCacheToolWindow),
                0,
                create: true,
                _package.DisposalToken);

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (window?.Frame is IVsWindowFrame frame)
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
        });
    }
}
