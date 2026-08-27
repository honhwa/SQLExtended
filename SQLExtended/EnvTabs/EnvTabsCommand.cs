using System;
using System.ComponentModel.Design;
using SQLExtended.Settings;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.EnvTabs;

/// <summary>Menu entry for the environment-tabs rule editor.</summary>
internal sealed class EnvTabsCommand
{
    private const int CommandId = 0x0505;

    private readonly AsyncPackage _package;

    private EnvTabsCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(OnOpen, new CommandID(SettingsCommand.CommandSet, CommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            _ = new EnvTabsCommand(package, commandService);
    }

    private void OnOpen(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            new EnvTabRulesDialog().ShowDialog();
        }
        catch (Exception ex)
        {
            VsShellUtilities.ShowMessageBox(
                _package, ex.Message, "Environment Tabs - Error",
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
