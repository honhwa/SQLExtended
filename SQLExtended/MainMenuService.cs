using EnvDTE;
using Microsoft.VisualStudio.CommandBars;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended;

/// <summary>
/// Adds the top-level "SQLExtended" menu to the SSMS main menu bar at runtime.
///
/// SSMS 22's shell does not reliably merge the VSCT-declared top-level menu (the command table
/// merges without error yet the menu never renders, and only appeared once under a debugger-forced
/// rebuild). Rather than keep fighting the static command-table merge, we build the menu against the
/// live menu model via DTE <see cref="CommandBars"/> — the same "inject at runtime" approach
/// <see cref="ObjectExplorer.ObjectExplorerMenuService"/> uses for the Object Explorer tree.
///
/// Each item routes to the command that is already registered on the package's command service
/// (by <c>*Command.InitializeAsync</c>), so the existing handlers are reused as-is. Everything is
/// wrapped in try/catch and polls for the menu bar, so a failure here never crashes SSMS.
/// </summary>
internal static class MainMenuService
{
    private static readonly Guid CommandSet = new Guid("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const string MenuCaption = "SQLExtended";
    private const string MenuBarName = "MenuBar";

    // Keep the event sinks alive: CommandBarEvents are held only weakly by the automation model, so
    // without a strong reference the GC collects them and the Click handlers silently stop firing.
    private static readonly List<CommandBarEvents> _eventSinks = new List<CommandBarEvents>();

    private static AsyncPackage _package;
    private static OleMenuCommandService _commandService;
    private static bool _built;

    // Items in display order. A null caption inserts a separator before the next item.
    private static readonly (string Caption, int CommandId)[] Items =
    {
        ("View Schema", 0x0100),
        ("Format SQL", 0x0200),
        ("Formatter...", 0x0210),
        ("Snippets...", 0x0220),
        (null, 0),
        ("Refresh Schema Cache", 0x0300),
        ("Schema Cache...", 0x0330),
        ("Regroup Servers Now", 0x0b00),
        (null, 0),
        ("SQL Search", 0x0400),
        ("Validate Schema References", 0x0a00),
        ("SQL History", 0x0700),
        ("Script Library", 0x0900),
        ("Script Results as INSERT", 0x0c00),
        ("Grid Aggregates", 0x0c10),
        ("Find in Results", 0x0c20),
        ("Parse Statistics", 0x0d00),
        (null, 0),
        ("Performance Monitor", 0x0f00),
        ("Always On Monitor", 0x0e00),
        ("Agent Jobs", 0x0f10),
        ("Replication Monitor", 0x0f20),
        (null, 0),
        ("SQLExtended Settings...", 0x0500),
        ("Environment Tabs...", 0x0505),
        ("Check for Updates...", 0x0800),
    };

    /// <summary>
    /// Resolves the main menu bar and injects the SQLExtended menu. The menu bar may not be ready when
    /// the package loads, so we poll for a short while before giving up.
    /// </summary>
    public static async Task InitializeAsync(AsyncPackage package)
    {
        _package = package;
        _commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;

        for (int attempt = 0; attempt < 20 && !_built; attempt++)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (TryBuild())
                return;
            await Task.Delay(1000);
        }
    }

    private static bool TryBuild()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (!(Package.GetGlobalService(typeof(SDTE)) is DTE dte))
                return false;

            var commandBars = dte.CommandBars as CommandBars;
            var menuBar = commandBars?[MenuBarName];
            if (menuBar == null)
                return false;

            // Idempotent: remove any SQLExtended popup we (or a prior session) left behind.
            for (int i = menuBar.Controls.Count; i >= 1; i--)
            {
                var existing = menuBar.Controls[i];
                if (string.Equals(existing.Caption, MenuCaption, StringComparison.Ordinal))
                    existing.Delete(false);
            }

            var popup = (CommandBarPopup)menuBar.Controls.Add(
                MsoControlType.msoControlPopup, Type.Missing, Type.Missing, Type.Missing, true);
            popup.Caption = MenuCaption;

            foreach (var item in Items)
            {
                if (item.Caption == null)
                {
                    // Next added button starts a new group (separator line above it).
                    _pendingSeparator = true;
                    continue;
                }

                var button = (CommandBarButton)popup.CommandBar.Controls.Add(
                    MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
                button.Caption = item.Caption;
                button.Style = MsoButtonStyle.msoButtonCaption;
                if (_pendingSeparator)
                {
                    button.BeginGroup = true;
                    _pendingSeparator = false;
                }

                int commandId = item.CommandId;
                var sink = (CommandBarEvents)dte.Events.CommandBarEvents[button];
                sink.Click += (object ctrl, ref bool handled, ref bool cancelDefault) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    try { _commandService?.GlobalInvoke(new CommandID(CommandSet, commandId)); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SQLExtended] menu invoke {commandId:X} failed: {ex}"); }
                    handled = true;
                };
                _eventSinks.Add(sink);
            }

            _built = true;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SQLExtended] MainMenuService.TryBuild failed: {ex}");
            return false;
        }
    }

    [ThreadStatic]
    private static bool _pendingSeparator;
}
