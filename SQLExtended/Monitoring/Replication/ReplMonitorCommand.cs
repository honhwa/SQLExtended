using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.Monitoring.Replication;

/// <summary>
/// Command handler for "Replication Monitor" (Ctrl+Alt+R).
///
/// The monitor is <b>pinned</b> to the instance it was opened from rather than following the active query window, so
/// showing it is not simply "show the tool window": a server has to be resolved first and matched against the
/// windows already open. <see cref="MonitorWindows"/> holds those rules and the instance ids, shared by all four
/// monitors — and it matters most here, because which instance you are connected to decides what replication state
/// is visible at all.
/// </summary>
internal sealed class ReplMonitorCommand
{
    public static readonly Guid CommandSet = new Guid("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int ReplMonitorCommandId = 0x0f20;

    private readonly AsyncPackage _package;
    private static ReplMonitorCommand _instance;

    private ReplMonitorCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, ReplMonitorCommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new ReplMonitorCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e) => Show(_package);

    /// <summary>
    /// Shows the monitor for one instance. Fire-and-forget; never throws into the caller, failures land in the
    /// ActivityLog.
    /// </summary>
    /// <param name="connectionString">
    /// A connection to the instance to pin to. Null means "use the active query window", which is what the keyboard
    /// shortcut and the menu do.
    /// </param>
    /// <param name="serverLabel">How the caller names that server, for the caption before the first poll.</param>
    public static void Show(AsyncPackage package, string connectionString = null, string serverLabel = null)
    {
        if (package == null) return;

        _ = package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

                // Resolving the connection is a UI-thread reflection walk into SSMS's editor internals, and it has
                // to happen before the window is chosen — which window to use depends on which server this is.
                string connection = connectionString;
                if (string.IsNullOrWhiteSpace(connection))
                {
                    try { connection = ConnectionHelper.GetActiveConnectionString(); } catch { }
                }

                string key = string.IsNullOrWhiteSpace(connection) ? null : MonitorWindows.ServerKey(connection);
                var (pane, atCap) = await MonitorWindows.AcquireAsync(package, typeof(ReplMonitorToolWindow), key);

                var window = pane as ReplMonitorToolWindow;
                var control = window?.Control;
                if (control == null) return;

                if (window.Frame is IVsWindowFrame frame)
                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());

                control.SetPackage(package);

                if (string.IsNullOrWhiteSpace(connection))
                {
                    // No connection to pin to, and the window we landed on may already be watching an instance. The
                    // user asked for the monitor and now has it in front of them; replacing a working window's
                    // contents with an error because no editor had focus would be the wrong trade.
                    if (string.IsNullOrEmpty(control.PinnedServerKey)) control.ShowNoConnection(serverLabel);
                    return;
                }

                bool repinned = atCap && !string.Equals(control.PinnedServerKey, key, StringComparison.Ordinal);
                control.PinTo(connection, serverLabel);

                if (repinned) control.ShowRepinnedNotice(MonitorWindows.MaxWindows);
            }
            catch (Exception ex)
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                ActivityLogHelper.LogError(package, "SQLExtended Replication Monitor", "Show failed: " + ex);
                System.Diagnostics.Debug.WriteLine($"[SQLExtended] Replication Monitor show failed: {ex}");
            }
        });
    }
}
