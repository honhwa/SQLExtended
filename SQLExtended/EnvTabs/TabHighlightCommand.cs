using System;
using System.ComponentModel.Design;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using SQLExtended.Settings;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.EnvTabs;

/// <summary>
/// "Highlight Tab" on the document tab's right-click menu: paints one tab in a loud colour until cleared or closed.
///
/// The colouring is the Environment Tabs mechanism (a full-path pattern in the shell's config, colour pinned through
/// <see cref="FileColorServiceProxy"/>), so <see cref="EnvTabsService"/> owns the state and this class only routes
/// the menu. The tab acted on is the active document frame: right-clicking a tab activates it before the menu opens.
/// </summary>
internal sealed class TabHighlightCommand
{
    /// <summary>Command id → palette index, in menu order. Keep in step with the TabHighlight* buttons in the .vsct.</summary>
    private static readonly (int CommandId, int ColorIndex)[] Colors =
    {
        (0x0510, 3),  // Rose
        (0x0511, 7),  // Pumpkin
        (0x0512, 1),  // Gold
        (0x0513, 9),  // Volt
        (0x0514, 4),  // Green
        (0x0515, 6),  // Sky
        (0x0516, 11), // Magenta
    };

    private const int ClearCommandId = 0x051F;

    private readonly AsyncPackage _package;

    private TabHighlightCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;

        foreach (var (commandId, colorIndex) in Colors)
        {
            // Menu commands are invoked and queried on the main thread; the asserts say so to the analyzer.
            var command = new OleMenuCommand((s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); OnHighlight(colorIndex); }, new CommandID(SettingsCommand.CommandSet, commandId));
            command.BeforeQueryStatus += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); OnQueryColor((OleMenuCommand)s, colorIndex); };
            commandService.AddCommand(command);
        }

        var clear = new OleMenuCommand((s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); OnClear(); }, new CommandID(SettingsCommand.CommandSet, ClearCommandId));
        clear.BeforeQueryStatus += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); OnQueryClear((OleMenuCommand)s); };
        commandService.AddCommand(clear);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            _ = new TabHighlightCommand(package, commandService);
    }

    private string ActivePath()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return DocumentTabs.Enumerate(_package).FirstOrDefault(t => t.IsActive)?.Path;
    }

    private void OnQueryColor(OleMenuCommand command, int colorIndex)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string path = ActivePath();
        command.Enabled = path != null && EnvTabsService.Instance != null;
        command.Checked = path != null && EnvTabsService.Instance?.HighlightOf(path) == colorIndex;
    }

    private void OnQueryClear(OleMenuCommand command)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        command.Enabled = EnvTabsService.Instance?.HighlightOf(ActivePath()) != null;
    }

    private void OnHighlight(int colorIndex)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            string path = ActivePath();
            if (path != null) EnvTabsService.Instance?.SetHighlight(path, colorIndex);
        }
        catch (Exception ex)
        {
            EnvTabsDiagnostics.Note("Could not highlight the tab: " + ex.Message);
        }
    }

    private void OnClear()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            EnvTabsService.Instance?.ClearHighlight(ActivePath());
        }
        catch (Exception ex)
        {
            EnvTabsDiagnostics.Note("Could not clear the tab highlight: " + ex.Message);
        }
    }
}
