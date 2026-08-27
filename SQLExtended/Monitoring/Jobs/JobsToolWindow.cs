using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended.Monitoring.Jobs;

/// <summary>
/// Dockable tool window for the Agent jobs dashboard. Hosts <see cref="JobsControl"/>.
/// Docked at the bottom and sized wide — the Jobs tab carries a lot of columns.
///
/// Registered <c>MultiInstances</c> (see the package's ProvideToolWindow attribute), so several of these can be
/// open at once, each pinned to a different server. <see cref="JobsCommand"/> owns instance ids and window reuse.
/// </summary>
[Guid("B7E3F1A2-9C4D-4E8F-A1B2-C3D4E5F60009")]
public sealed class JobsToolWindow : ToolWindowPane, IPinnedMonitorPane
{
    private const string BaseCaption = "Agent Jobs";

    public JobsToolWindow() : base(null)
    {
        Caption = BaseCaption;

        var control = new JobsControl();

        // With several windows open the caption is the only thing distinguishing their tabs, so the control names
        // the server it is pinned to here. It starts as the connect target and is refined to the instance's own
        // name once the first poll returns SERVERPROPERTY('ServerName').
        control.CaptionChanged = server => Caption = string.IsNullOrWhiteSpace(server) ? BaseCaption : BaseCaption + " — " + server;

        Content = control;
    }

    /// <summary>The hosted control, so the command can pin the window and trigger refreshes.</summary>
    internal JobsControl Control => Content as JobsControl;

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
