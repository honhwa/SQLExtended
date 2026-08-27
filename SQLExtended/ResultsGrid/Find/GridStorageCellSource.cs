using System;
using System.Collections.Specialized;
using System.Drawing;
using System.Reflection;
using Microsoft.SqlServer.Management.UI.Grid;

namespace SQLExtended.ResultsGrid.Find;

/// <summary>
/// Adapts one SSMS results grid to <see cref="IGridCellSource"/>. This is the only file in the search that
/// knows a grid's storage column 0 is the row-number column and that data starts at 1 — the same convention
/// <see cref="ResultsGridReader"/> and the aggregates reader follow.
///
/// <para><b>Cell text is taken exactly as the grid renders it.</b> <c>GetCellDataAsString</c> is the only
/// value accessor SSMS exposes, and it already returns "NULL" for a null cell. The search therefore looks
/// for what is on screen, which is both the only thing it can do and the behaviour a user reading the grid
/// expects — searching NULL finds NULLs, and also finds a varchar cell containing the word.</para>
///
/// <para>Everything is read once at construction: a grid is replaced wholesale by the next execution rather
/// than mutated, so a source that outlives its grid should stop producing values, not silently start
/// describing a different result set.</para>
/// </summary>
internal sealed class GridStorageCellSource : IGridCellSource
{
    private readonly IGridStorage _storage;
    private readonly StringCollection _columnNames;

    private GridStorageCellSource(GridControl grid, IGridStorage storage, StringCollection columnNames, long rowCount, int columnCount)
    {
        Grid = grid;
        _storage = storage;
        _columnNames = columnNames;
        RowCount = rowCount;
        ColumnCount = columnCount;
    }

    public GridControl Grid { get; }

    public long RowCount { get; }

    public int ColumnCount { get; }

    /// <summary>Builds a source for a grid, or null if the grid has no readable storage (it is being torn
    /// down, or the result set is still filling).</summary>
    public static GridStorageCellSource Create(GridControl grid)
    {
        if (grid == null || grid.IsDisposed)
            return null;

        try
        {
            IGridStorage storage = grid.GridStorage;
            if (storage == null)
                return null;

            StringCollection columnNames = null;
            try { columnNames = storage.GetType().GetProperty("ColumnNames", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(storage) as StringCollection; }
            catch { }

            long rowCount = storage.NumRows();
            int columnCount = columnNames?.Count ?? Math.Max(0, grid.ColumnsNumber - 1);
            if (rowCount <= 0 || columnCount <= 0)
                return null;

            return new GridStorageCellSource(grid, storage, columnNames, rowCount, columnCount);
        }
        catch
        {
            return null;
        }
    }

    public string GetValue(long row, int column)
    {
        try { return _storage.GetCellDataAsString(row, column + 1); }
        catch { return null; }
    }

    public void Prefetch(long firstRow, long lastRow)
    {
        try { _storage.EnsureRowsInBuf(firstRow, lastRow); }
        catch { }
    }

    /// <summary>The column's header text, for naming a match in the status line. Falls back the same way the
    /// aggregates reader does — the header, then a positional name.</summary>
    public string ColumnName(int dataColumn)
    {
        if (_columnNames != null && dataColumn >= 0 && dataColumn < _columnNames.Count)
        {
            string name = _columnNames[dataColumn];
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        try
        {
            Grid.GetHeaderInfo(dataColumn + 1, out string header, out Bitmap _);
            if (!string.IsNullOrEmpty(header))
                return header;
        }
        catch { }

        return $"Column {dataColumn + 1}";
    }

    /// <summary>Whether this source still describes a live grid. A source outlives its grid whenever a query
    /// is re-executed while the search window is open.</summary>
    public bool IsAlive => Grid != null && !Grid.IsDisposed;
}
