using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended.ResultsGrid.Aggregates;

/// <summary>
/// Dockable tool window showing aggregates for the current results-grid selection. Hosts
/// <see cref="AggregatesControl"/>.
///
/// Docked at the bottom by default so it sits alongside the query window's own Results and Messages
/// panes — this window is read next to a grid, not instead of one. Single-instance: it follows whichever
/// grid the selection is in, so a second copy would only ever show the same thing.
/// </summary>
[Guid("B7E3F1A2-9C4D-4E8F-A1B2-C3D4E5F6000B")]
public sealed class AggregatesToolWindow : ToolWindowPane
{
    public AggregatesToolWindow() : base(null)
    {
        Caption = "Grid Aggregates";
        Content = new AggregatesControl();
    }

    internal AggregatesControl Control => Content as AggregatesControl;
}
