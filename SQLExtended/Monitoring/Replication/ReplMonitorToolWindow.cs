using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended.Monitoring.Replication;

/// <summary>
/// Dockable tool window for the replication monitor. Hosts <see cref="ReplMonitorControl"/>.
/// Docked at the bottom and sized wide — the Subscriptions tab carries a lot of columns.
///
/// Registered <c>MultiInstances</c> (see the package's ProvideToolWindow attribute), so several of these can be
/// open at once, each pinned to a different instance. <see cref="MonitorWindows"/> owns instance ids and reuse.
///
/// The GUID is …F6000A: it was …F60008 (a copy-paste of the Performance window's, which still has it). Two panes
/// sharing a GUID means VS cannot tell their frames apart for activation or persistence, so both windows fight
/// over one identity.
/// </summary>
[Guid("B7E3F1A2-9C4D-4E8F-A1B2-C3D4E5F6000A")]
public sealed class ReplMonitorToolWindow : ToolWindowPane, IPinnedMonitorPane
{
    private const string BaseCaption = "Replication";

    public ReplMonitorToolWindow() : base(null)
    {
        Caption = BaseCaption;

        var control = new ReplMonitorControl();

        // With several windows open the caption is the only thing distinguishing their tabs, so the control names
        // the instance it is pinned to here. It starts as the connect target and is refined to the instance's own
        // name once the first poll returns SERVERPROPERTY('ServerName').
        control.CaptionChanged = server => Caption = string.IsNullOrWhiteSpace(server) ? BaseCaption : BaseCaption + " — " + server;

        Content = control;
    }

    /// <summary>The hosted control, so the command can pin the window and trigger refreshes.</summary>
    internal ReplMonitorControl Control => Content as ReplMonitorControl;

    // Explicit implementations: the pane type has to be public for VS to instantiate it, but nothing outside this
    // assembly has any business with its instance id or its pin.
    int IPinnedMonitorPane.InstanceId { get; set; }
    string IPinnedMonitorPane.PinnedServerKey => Control?.PinnedServerKey;

    protected override void Dispose(bool disposing)
    {
        if (disposing) MonitorWindows.Forget(this);
        base.Dispose(disposing);
    }
}
