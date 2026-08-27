using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.Validation;

/// <summary>
/// Command handler that opens the Schema Validation tool window (Ctrl+Alt+V),
/// which scans the selected database(s) for broken object, cross-database, and linked-server references.
/// </summary>
internal sealed class SchemaValidationCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int SchemaValidateCommandId = 0x0a00;

    private readonly AsyncPackage _package;
    private static SchemaValidationCommand _instance;

    private SchemaValidationCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, SchemaValidateCommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new SchemaValidationCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            var window = await _package.ShowToolWindowAsync(
                typeof(SchemaValidationToolWindow),
                0,
                create: true,
                _package.DisposalToken);

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (window?.Frame is IVsWindowFrame frame)
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
        });
    }
}
