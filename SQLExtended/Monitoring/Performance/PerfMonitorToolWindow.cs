using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended.Monitoring.Performance;

/// <summary>
/// Dockable tool window for the live performance dashboard. Hosts <see cref="PerfMonitorControl"/>.
/// Docked at the bottom and sized wide — the Activity tab carries a lot of columns.
///
/// Registered <c>MultiInstances</c> (see the package's ProvideToolWindow attribute), so several of these can be
/// open at once, each pinned to a different server. <see cref="MonitorWindows"/> owns instance ids and reuse.
/// </summary>
[Guid("B7E3F1A2-9C4D-4E8F-A1B2-C3D4E5F60008")]
public sealed class PerfMonitorToolWindow : ToolWindowPane, IPinnedMonitorPane
{
    private const string BaseCaption = "Performance";

    public PerfMonitorToolWindow() : base(null)
    {
        Caption = BaseCaption;

        var control = new PerfMonitorControl();

        // With several windows open the caption is the only thing distinguishing their tabs, so the control names
        // the server it is pinned to here. It starts as the connect target and is refined to the instance's own
        // name once the first poll returns SERVERPROPERTY('ServerName').
        control.CaptionChanged = server => Caption = string.IsNullOrWhiteSpace(server) ? BaseCaption : BaseCaption + " — " + server;

        Content = control;
    }

    /// <summary>The hosted control, so the command can pin the window and trigger refreshes.</summary>
    internal PerfMonitorControl Control => Content as PerfMonitorControl;

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
