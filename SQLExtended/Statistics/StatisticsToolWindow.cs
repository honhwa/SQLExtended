using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended.Statistics;

/// <summary>
/// Dockable tool window for parsed STATISTICS IO/TIME output. Hosts <see cref="StatisticsControl"/>.
/// Registered via <see cref="ProvideToolWindowAttribute"/> on the package; docked at the bottom so it sits alongside
/// the query window's own Results/Messages panes.
/// </summary>
[Guid("B7E3F1A2-9C4D-4E8F-A1B2-C3D4E5F60006")]
public sealed class StatisticsToolWindow : ToolWindowPane
{
    public StatisticsToolWindow() : base(null)
    {
        Caption = "Statistics";
        Content = new StatisticsControl();
    }

    /// <summary>The hosted control, so the command can push a fresh capture into an already-open window.</summary>
    internal StatisticsControl Control => Content as StatisticsControl;
}
