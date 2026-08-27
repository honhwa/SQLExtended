using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQLExtended.Settings;
using System;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.Updates;

/// <summary>
/// Menu command that triggers an immediate update check, bypassing the 20h cooldown.
/// Shown as "Check for Updates..." in the SQLExtended menu.
/// </summary>
internal sealed class CheckForUpdatesCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int CommandId = 0x0800;

    private readonly AsyncPackage _package;
    private static CheckForUpdatesCommand _instance;

    private CheckForUpdatesCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(OnExecute, new CommandID(CommandSet, CommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService svc)
            _instance = new CheckForUpdatesCommand(package, svc);
    }

    private void OnExecute(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var settings = SQLExtendedSettings.Load();
        if (!settings.UpdateCheckEnabled || string.IsNullOrWhiteSpace(settings.UpdateFeedUrl))
        {
            VsShellUtilities.ShowMessageBox(
                _package,
                "Update checking is disabled or no feed URL is configured. Open SQLExtended Settings > Updates to configure.",
                "Check for Updates",
                OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            return;
        }

        // Bypass cooldown by zeroing the timestamp; also forget any previously-skipped version so a
        // manual check always surfaces whatever's on the feed.
        settings.UpdateLastCheckUtc = DateTime.MinValue;
        settings.UpdateSkippedVersion = "";
        settings.Save();

        UpdateCheckService.RunManualCheck();
    }
}
