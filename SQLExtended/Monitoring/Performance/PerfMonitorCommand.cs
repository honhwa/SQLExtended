using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.Monitoring.Performance;

/// <summary>
/// Command handler for "Performance Monitor" (Ctrl+Alt+P).
///
/// The dashboard is <b>pinned</b> to the server it was opened from rather than following the active query window,
/// so showing it is not simply "show the tool window": a server has to be resolved first and matched against the
/// windows already open. <see cref="MonitorWindows"/> holds those rules and the instance ids, shared by all four
/// monitors.
/// </summary>
internal sealed class PerfMonitorCommand
{
    public static readonly Guid CommandSet = new Guid("a1b2c3d4-e5f6-7890-abcd-123456789abc");
    private const int PerfMonitorCommandId = 0x0f00;

    private readonly AsyncPackage _package;
    private static PerfMonitorCommand _instance;

    private PerfMonitorCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, PerfMonitorCommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new PerfMonitorCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e) => Show(_package);

    /// <summary>
    /// Shows the dashboard for one server. Fire-and-forget; never throws into the caller, failures land in the
    /// ActivityLog.
    /// </summary>
    /// <param name="connectionString">
    /// A connection to the server to pin to. Null means "use the active query window", which is what the keyboard
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
                var (pane, atCap) = await MonitorWindows.AcquireAsync(package, typeof(PerfMonitorToolWindow), key);

                var window = pane as PerfMonitorToolWindow;
                var control = window?.Control;
                if (control == null) return;

                if (window.Frame is IVsWindowFrame frame)
                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());

                control.SetPackage(package);

                if (string.IsNullOrWhiteSpace(connection))
                {
                    // No connection to pin to, and the window we landed on may already be watching a server. The
                    // user asked for the dashboard and now has it in front of them; replacing a working window's
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
                ActivityLogHelper.LogError(package, "SQLExtended Performance Monitor", "Show failed: " + ex);
                System.Diagnostics.Debug.WriteLine($"[SQLExtended] Performance Monitor show failed: {ex}");
            }
        });
    }
}
