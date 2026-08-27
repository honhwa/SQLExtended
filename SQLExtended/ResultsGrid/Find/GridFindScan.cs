using System;
using System.Collections.Generic;

namespace SQLExtended.ResultsGrid.Find;

/// <summary>
/// Walks a grid's cells in row-major order looking for matches, <b>a bounded slice at a time</b>.
///
/// <para><b>The slicing is the point.</b> <c>IGridStorage</c> is a WinForms control's storage and can only
/// be read on the UI thread, so a straight loop over a million-row result set is a frozen SSMS for as long
/// as it takes. <see cref="Step"/> does only as much work as it is given and returns, letting the caller
/// hand the thread back between slices — the window stays responsive, the caret keeps blinking, and Cancel
/// is clickable while the search is still running.</para>
///
/// <para><b>It also stops rather than lie.</b> Two caps bound a scan: cells examined and matches collected.
/// Either one being hit is recorded (<see cref="StoppedAtCellCap"/>, <see cref="StoppedAtMatchCap"/>) so the
/// caller can say the count is a floor rather than a total. Reporting "47 matches" when the scan gave up at
/// cell 2,000,000 is the one failure nobody would catch by looking at it.</para>
///
/// <para>Free of the grid assembly — it walks an <see cref="IGridCellSource"/> — so the walk order, the
/// wrap-around and the caps are unit-tested against a fake.</para>
/// </summary>
internal sealed class GridFindScan
{
    /// <summary>Rows asked for at a time. The grid fetches a block per call, so this trades a little memory
    /// for not making a round trip per row.</summary>
    private const int PrefetchRows = 512;

    private readonly IGridCellSource _source;
    private readonly GridFindMatcher _matcher;
    private readonly List<GridCellPosition> _matches = new();
    private readonly long _rowCount;
    private readonly int _columnCount;
    private readonly long _totalCells;
    private readonly bool _forward;
    private readonly int _maxMatches;
    private readonly long _maxCells;

    /// <summary>Flat index of the next cell to examine: <c>row * columns + column</c>.</summary>
    private long _index;

    /// <summary>Cells still to examine before the walk is complete. Counting down rather than comparing
    /// against the start position is what makes a wrapped scan stop exactly one cell short of where it
    /// began, instead of either re-examining that cell or missing it.</summary>
    private long _remaining;

    private long _prefetchedFrom = -1;
    private long _prefetchedTo = -1;

    /// <param name="startRow">First row to examine.</param>
    /// <param name="startColumn">First data column to examine, within <paramref name="startRow"/>.</param>
    /// <param name="forward">Walk forwards (down and right) or backwards.</param>
    /// <param name="wrap">Continue past the end of the grid and back to the start position.</param>
    /// <param name="maxMatches">Stop once this many matches are collected.</param>
    /// <param name="maxCells">Stop once this many cells have been examined.</param>
    public GridFindScan(IGridCellSource source, GridFindMatcher matcher, long startRow, int startColumn, bool forward, bool wrap, int maxMatches, long maxCells)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
        _forward = forward;
        _maxMatches = Math.Max(1, maxMatches);
        _maxCells = Math.Max(1L, maxCells);

        // Captured once: the grid can be replaced underneath a running scan (a re-execution), and a source
        // that shrinks mid-walk would otherwise be read out of bounds.
        _rowCount = Math.Max(0, source.RowCount);
        _columnCount = Math.Max(0, source.ColumnCount);
        _totalCells = _rowCount * _columnCount;

        if (_totalCells == 0)
        {
            Finished = true;
            return;
        }

        long row = Math.Min(Math.Max(0, startRow), _rowCount - 1);
        int column = Math.Min(Math.Max(0, startColumn), _columnCount - 1);
        _index = row * _columnCount + column;

        _remaining = wrap ? _totalCells : (forward ? _totalCells - _index : _index + 1);
    }

    public IReadOnlyList<GridCellPosition> Matches => _matches;

    public long CellsExamined { get; private set; }

    /// <summary>Nothing further will be examined — the walk completed, or a cap stopped it.</summary>
    public bool Finished { get; private set; }

    public bool StoppedAtCellCap { get; private set; }

    public bool StoppedAtMatchCap { get; private set; }

    /// <summary>True if a regular expression timed out on some cell, so the results are known-incomplete.</summary>
    public bool TimedOut => _matcher.TimedOut;

    /// <summary>
    /// Examines up to <paramref name="budget"/> cells and returns whether there is more to do. Exceptions
    /// from the source (a grid disposed mid-scan is the ordinary case) propagate to the caller, which knows
    /// whether that is worth reporting.
    /// </summary>
    public bool Step(long budget)
    {
        long examinedThisSlice = 0;

        while (!Finished && examinedThisSlice < budget)
        {
            if (_remaining <= 0)
            {
                Finished = true;
                break;
            }

            if (CellsExamined >= _maxCells)
            {
                StoppedAtCellCap = true;
                Finished = true;
                break;
            }

            long row = _index / _columnCount;
            int column = (int)(_index % _columnCount);

            EnsurePrefetched(row);

            string text = _source.GetValue(row, column);
            CellsExamined++;
            examinedThisSlice++;
            _remaining--;

            if (_matcher.IsMatch(text))
            {
                _matches.Add(new GridCellPosition(row, column));
                if (_matches.Count >= _maxMatches)
                {
                    StoppedAtMatchCap = true;
                    Finished = true;
                    break;
                }
            }

            // Finish on the cell that completes the walk rather than on the following call. Otherwise the
            // last slice reports work remaining when there is none, and the caller pays a whole extra tick
            // before it can say the search is over.
            if (_remaining <= 0)
            {
                Finished = true;
                break;
            }

            Advance();
        }

        return !Finished;
    }

    private void Advance()
    {
        if (_forward)
        {
            _index++;
            if (_index >= _totalCells)
                _index = 0;
        }
        else
        {
            _index--;
            if (_index < 0)
                _index = _totalCells - 1;
        }
    }

    private void EnsurePrefetched(long row)
    {
        if (row >= _prefetchedFrom && row <= _prefetchedTo)
            return;

        long from, to;
        if (_forward)
        {
            from = row;
            to = Math.Min(_rowCount - 1, row + PrefetchRows - 1);
        }
        else
        {
            to = row;
            from = Math.Max(0, row - PrefetchRows + 1);
        }

        _source.Prefetch(from, to);
        _prefetchedFrom = from;
        _prefetchedTo = to;
    }
}
