using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.SqlServer.Management.UI.Grid;

namespace SQLExtended.ResultsGrid.Find;

/// <summary>
/// Tints the matching cells of one grid, by handing the grid a different background brush for them as it
/// paints. <see cref="GridControl.CustomizeCellGDIObjects"/> is the only hook for this and it is a real
/// public event, so nothing here is reflection — but three things about how the grid uses it decide whether
/// this works, and all three were read out of the control's paint path:
///
/// <list type="bullet">
/// <item><b>The grid does not own the brushes it is handed.</b> It paints with them and forgets them — it
/// never disposes them — so the brushes here are static and shared. Allocating a brush per cell would churn
/// two objects for every cell painted on every scroll; disposing one the grid still held would take the
/// grid down.</item>
/// <item><b>Never set <c>CellFont</c>.</b> The grid reuses a single event-args instance for every cell and
/// reads <c>CellFont</c> back unconditionally for all of them, so a font set for one match leaks onto the
/// rest of the grid — and stays there.</item>
/// <item><b>Selection is applied after this hook, so the current match paints itself.</b> The grid overrides
/// the background of selected cells further down the same method, which means the one match the user is
/// standing on shows in the selection colour and the others in this tint, with no special casing here. It is
/// also why the controller turns <see cref="GridControl.AlwaysHighlightSelection"/> on: the search box has
/// the keyboard focus, and without it the grid paints no selection at all while unfocused.</item>
/// </list>
///
/// <para>The tint is chosen against the cell's own incoming background rather than fixed, so it works in
/// SSMS's light and dark themes without either having to be detected.</para>
/// </summary>
internal sealed class GridFindHighlighter : IDisposable
{
    // Amber, the colour every find-in-page has used for thirty years. Two pairs, picked against the
    // brightness of the cell's normal background so neither theme gets unreadable text.
    private static readonly SolidBrush LightBack = new(Color.FromArgb(255, 250, 217, 97));
    private static readonly SolidBrush LightText = new(Color.FromArgb(255, 30, 30, 30));
    private static readonly SolidBrush DarkBack = new(Color.FromArgb(255, 106, 84, 20));
    private static readonly SolidBrush DarkText = new(Color.FromArgb(255, 245, 226, 155));

    private readonly GridControl _grid;
    private readonly bool _priorAlwaysHighlightSelection;
    private HashSet<GridCellPosition> _matches = new();
    private bool _disposed;

    public GridFindHighlighter(GridControl grid)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _priorAlwaysHighlightSelection = _grid.AlwaysHighlightSelection;

        // The user is typing in the search window, so the grid is never the focused control while it is
        // being stepped through. Without this the current match is selected and invisible.
        _grid.AlwaysHighlightSelection = true;
        _grid.CustomizeCellGDIObjects += OnCustomizeCellGDIObjects;
        _grid.Disposed += OnGridDisposed;
    }

    public GridControl Grid => _grid;

    /// <summary>Replaces the tinted set and repaints. Called repeatedly while a scan is still running, so it
    /// takes the matches found so far rather than waiting for a total.</summary>
    public void SetMatches(IEnumerable<GridCellPosition> matches)
    {
        if (_disposed)
            return;

        var set = new HashSet<GridCellPosition>();
        if (matches != null)
        {
            foreach (GridCellPosition match in matches)
                set.Add(match);
        }

        _matches = set;
        Invalidate();
    }

    public void Clear() => SetMatches(null);

    private void OnCustomizeCellGDIObjects(object sender, CustomizeCellGDIObjectsEventArgs e)
    {
        var matches = _matches;
        if (matches.Count == 0)
            return;

        // The grid reports the *storage* column index here (it passes the column's own ColumnIndex, not its
        // position on screen), so a grid whose columns have been dragged around needs no adjustment — but
        // column 0 is still the row-number column, and data starts at 1.
        int dataColumn = e.ColumnIndex - 1;
        if (dataColumn < 0)
            return;

        if (!matches.Contains(new GridCellPosition(e.RowIndex, dataColumn)))
            return;

        bool dark = IsDark(e.BKBrush?.Color ?? Color.White);
        e.BKBrush = dark ? DarkBack : LightBack;
        e.TextBrush = dark ? DarkText : LightText;
    }

    private static bool IsDark(Color color) => (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) < 128.0;

    private void Invalidate()
    {
        try
        {
            if (!_grid.IsDisposed)
                _grid.Invalidate();
        }
        catch { }
    }

    private void OnGridDisposed(object sender, EventArgs e) => Dispose();

    /// <summary>Detaches and puts the grid back the way it was found — including
    /// <see cref="GridControl.AlwaysHighlightSelection"/>, which is a visible change to a control we do not
    /// own and would otherwise outlive the search by the life of the query window.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            _grid.CustomizeCellGDIObjects -= OnCustomizeCellGDIObjects;
            _grid.Disposed -= OnGridDisposed;

            if (!_grid.IsDisposed)
            {
                _grid.AlwaysHighlightSelection = _priorAlwaysHighlightSelection;
                _grid.Invalidate();
            }
        }
        catch { }
    }
}
