using System;

namespace SQLExtended.ResultsGrid.Find;

/// <summary>A cell, addressed the way the rest of this namespace addresses cells.</summary>
internal readonly struct GridCellPosition : IEquatable<GridCellPosition>
{
    public GridCellPosition(long row, int column)
    {
        Row = row;
        Column = column;
    }

    public long Row { get; }

    /// <summary>
    /// <b>0-based data column.</b> Storage indexes — where column 0 is the grid's row-number column and data
    /// starts at 1 — are converted at the two edges that touch the grid (<see cref="GridStorageCellSource"/>
    /// on the way in, <see cref="GridFindHighlighter"/> and the controller's selection call on the way out).
    /// Nothing between them knows the offset exists, which is what keeps it from being applied twice or not
    /// at all — a mistake that reads as the search quietly matching the neighbouring column.
    /// </summary>
    public int Column { get; }

    public bool Equals(GridCellPosition other) => Row == other.Row && Column == other.Column;

    public override bool Equals(object obj) => obj is GridCellPosition other && Equals(other);

    public override int GetHashCode() => unchecked((Row.GetHashCode() * 397) ^ Column);

    public override string ToString() => $"r{Row}c{Column}";
}

/// <summary>
/// The cells a scan walks. An interface rather than the grid itself so <see cref="GridFindScan"/> can be
/// unit-tested against a fake — the scan's walk order, wrap-around and cap handling are all things that
/// fail silently on screen (a wrong order shows the "next" match somewhere behind you, a wrap that stops a
/// cell early hides exactly one match, and a cap that reports a total it did not verify is worse still).
/// </summary>
internal interface IGridCellSource
{
    long RowCount { get; }

    /// <summary>Data columns, so 0..<see cref="ColumnCount"/>-1 addresses real data.</summary>
    int ColumnCount { get; }

    /// <summary>
    /// The cell's text <b>as the grid displays it</b> — which is the only thing SSMS exposes, and means a
    /// NULL arrives here as the literal text "NULL", exactly as the user sees it. Searching for NULL
    /// therefore finds NULLs, and also finds a varchar cell containing the word.
    /// </summary>
    string GetValue(long row, int column);

    /// <summary>Hint that a row range is about to be read. Lets the grid fetch a block at a time instead of
    /// a round trip per cell; a no-op is a valid implementation.</summary>
    void Prefetch(long firstRow, long lastRow);
}
