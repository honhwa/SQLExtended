using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended.ResultsGrid.Find;

/// <summary>
/// Dockable tool window for finding text in a results grid. Hosts <see cref="GridFindControl"/>.
///
/// Docked at the bottom and short, so it sits under the query window's own Results pane without covering
/// the grid it is searching — a find window that hides the matches it just scrolled to is worse than none.
/// Single-instance: it follows whichever grid the user was last in, so a second copy would fight the first
/// over the same grid's selection.
/// </summary>
[Guid("B7E3F1A2-9C4D-4E8F-A1B2-C3D4E5F6000C")]
public sealed class GridFindToolWindow : ToolWindowPane
{
    public GridFindToolWindow() : base(null)
    {
        Caption = "Find in Results";
        Content = new GridFindControl();
    }

    internal GridFindControl Control => Content as GridFindControl;
}
