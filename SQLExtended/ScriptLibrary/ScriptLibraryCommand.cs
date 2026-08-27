using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.ScriptLibrary;

/// <summary>
/// Command handler that opens the Script Library tool window (Ctrl+Alt+L).
/// </summary>
internal sealed class ScriptLibraryCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int ScriptLibraryCommandId = 0x0900;

    private readonly AsyncPackage _package;
    private static ScriptLibraryCommand _instance;

    private ScriptLibraryCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, ScriptLibraryCommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new ScriptLibraryCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            var window = await _package.ShowToolWindowAsync(
                typeof(ScriptLibraryToolWindow),
                0,
                create: true,
                _package.DisposalToken);

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (window?.Frame is IVsWindowFrame frame)
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
        });
    }
}
