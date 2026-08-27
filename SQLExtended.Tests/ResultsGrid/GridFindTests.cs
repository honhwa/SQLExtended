using System;
using System.Collections.Generic;
using System.Linq;
using SQLExtended.ResultsGrid.Find;
using Xunit;

namespace SQLExtended.Tests.ResultsGrid;

/// <summary>
/// The matching rules and the scan walk behind Find in Results. Both are pinned here because every mistake
/// either can make is invisible on screen: a wrong walk order shows the "next" match somewhere behind you, a
/// wrap that stops a cell early hides exactly one match out of a grid full of them, and a cap that goes
/// unrecorded turns a partial count into a total nobody thinks to question.
/// </summary>
public class GridFindTests
{
    /// <summary>A grid of known text, and a record of the row ranges the scan asked for.</summary>
    private sealed class FakeSource : IGridCellSource
    {
        private readonly string[,] _cells;

        public FakeSource(string[,] cells)
        {
            _cells = cells;
            RowCount = cells.GetLength(0);
            ColumnCount = cells.GetLength(1);
        }

        public long RowCount { get; }
        public int ColumnCount { get; }
        public List<(long From, long To)> Prefetches { get; } = new();
        public List<(long Row, int Column)> Reads { get; } = new();

        public string GetValue(long row, int column)
        {
            Reads.Add((row, column));
            return _cells[row, column];
        }

        public void Prefetch(long firstRow, long lastRow) => Prefetches.Add((firstRow, lastRow));
    }

    /// <summary>A 3x2 grid whose contents make position obvious: "r0c0", "r0c1", "r1c0"…</summary>
    private static FakeSource Grid(int rows, int columns)
    {
        var cells = new string[rows, columns];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
                cells[r, c] = $"r{r}c{c}";
        }
        return new FakeSource(cells);
    }

    private static GridFindMatcher Matcher(string term, GridFindOptions options = null)
    {
        GridFindMatcher matcher = GridFindMatcher.Create(term, options ?? new GridFindOptions(), out string error);
        Assert.Null(error);
        Assert.NotNull(matcher);
        return matcher;
    }

    /// <summary>Runs a scan to completion in small slices — which is also the only way the controller ever
    /// runs one, so this is the real code path rather than a convenience.</summary>
    private static void RunToCompletion(GridFindScan scan, long budget = 3)
    {
        int guard = 0;
        while (scan.Step(budget))
        {
            if (++guard > 10_000)
                throw new InvalidOperationException("Scan did not finish — it is not making progress.");
        }
        Assert.True(scan.Finished);
    }

    // ---- matching ----------------------------------------------------------------------------------

    [Fact]
    public void Contains_is_the_default_and_ignores_case()
    {
        GridFindMatcher matcher = Matcher("acme");

        Assert.True(matcher.IsMatch("ACME Ltd"));
        Assert.True(matcher.IsMatch("see acme contract"));
        Assert.False(matcher.IsMatch("Acne Ltd"));
    }

    [Fact]
    public void MatchCase_distinguishes_spelling()
    {
        GridFindMatcher matcher = Matcher("ACME", new GridFindOptions { MatchCase = true });

        Assert.True(matcher.IsMatch("ACME Ltd"));
        Assert.False(matcher.IsMatch("acme Ltd"));
    }

    [Fact]
    public void WholeCell_requires_the_entire_cell()
    {
        GridFindMatcher matcher = Matcher("ACME", new GridFindOptions { WholeCell = true });

        Assert.True(matcher.IsMatch("ACME"));
        Assert.True(matcher.IsMatch("acme"));           // still case-insensitive by default
        Assert.False(matcher.IsMatch("ACME Ltd"));
        Assert.False(matcher.IsMatch(" ACME"));
    }

    [Fact]
    public void A_null_cell_matches_nothing_rather_than_everything()
    {
        // The source hands over displayed text, so a real NULL arrives as "NULL" and is found by searching
        // for it. A null reference here means the cell could not be read at all, and must not match.
        Assert.True(Matcher("null").IsMatch("NULL"));
        Assert.False(Matcher("null").IsMatch(null));
    }

    [Fact]
    public void Regex_matches_and_reports_its_own_syntax_errors()
    {
        GridFindMatcher matcher = Matcher(@"^\d{3}-\d{4}$", new GridFindOptions { UseRegex = true });
        Assert.True(matcher.IsMatch("555-1234"));
        Assert.False(matcher.IsMatch("5551234"));

        GridFindMatcher broken = GridFindMatcher.Create("(unclosed", new GridFindOptions { UseRegex = true }, out string error);
        Assert.Null(broken);
        Assert.Contains("Invalid regular expression", error);
    }

    [Fact]
    public void An_empty_term_is_refused_rather_than_matching_every_cell()
    {
        Assert.Null(GridFindMatcher.Create("", new GridFindOptions(), out string error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void WholeCell_regex_is_anchored_to_the_cell_not_to_a_line()
    {
        // \A…\z rather than ^…$: in .NET the latter also match at line boundaries, so a multi-line cell
        // would satisfy "whole cell" on the strength of one of its lines.
        GridFindMatcher matcher = Matcher("ACME", new GridFindOptions { UseRegex = true, WholeCell = true });

        Assert.True(matcher.IsMatch("ACME"));
        Assert.False(matcher.IsMatch("ACME\nLtd"));
    }

    // ---- walk order --------------------------------------------------------------------------------

    [Fact]
    public void Forward_walks_row_major()
    {
        FakeSource source = Grid(rows: 3, columns: 2);
        var scan = new GridFindScan(source, Matcher("r"), 0, 0, forward: true, wrap: false, maxMatches: 100, maxCells: 1000);
        RunToCompletion(scan);

        Assert.Equal(
            new[] { (0L, 0), (0L, 1), (1L, 0), (1L, 1), (2L, 0), (2L, 1) },
            scan.Matches.Select(m => (m.Row, m.Column)).ToArray());
    }

    [Fact]
    public void Backward_walks_row_major_in_reverse()
    {
        FakeSource source = Grid(rows: 3, columns: 2);
        var scan = new GridFindScan(source, Matcher("r"), 2, 1, forward: false, wrap: false, maxMatches: 100, maxCells: 1000);
        RunToCompletion(scan);

        Assert.Equal(
            new[] { (2L, 1), (2L, 0), (1L, 1), (1L, 0), (0L, 1), (0L, 0) },
            scan.Matches.Select(m => (m.Row, m.Column)).ToArray());
    }

    [Fact]
    public void Without_wrap_a_scan_stops_at_the_end_of_the_grid()
    {
        FakeSource source = Grid(rows: 3, columns: 2);
        var scan = new GridFindScan(source, Matcher("r"), startRow: 2, startColumn: 0, forward: true, wrap: false, maxMatches: 100, maxCells: 1000);
        RunToCompletion(scan);

        Assert.Equal(new[] { (2L, 0), (2L, 1) }, scan.Matches.Select(m => (m.Row, m.Column)).ToArray());
        Assert.Equal(2, scan.CellsExamined);
    }

    [Fact]
    public void With_wrap_every_cell_is_examined_exactly_once()
    {
        // The one that matters: off by one in either direction and a wrapped search either re-reports the
        // cell it started on or silently skips it.
        FakeSource source = Grid(rows: 3, columns: 2);
        var scan = new GridFindScan(source, Matcher("r"), startRow: 1, startColumn: 1, forward: true, wrap: true, maxMatches: 100, maxCells: 1000);
        RunToCompletion(scan);

        Assert.Equal(6, scan.CellsExamined);
        Assert.Equal(6, source.Reads.Count);
        Assert.Equal(6, source.Reads.Distinct().Count());
        Assert.Equal(
            new[] { (1L, 1), (2L, 0), (2L, 1), (0L, 0), (0L, 1), (1L, 0) },
            scan.Matches.Select(m => (m.Row, m.Column)).ToArray());
    }

    [Fact]
    public void With_wrap_backwards_every_cell_is_examined_exactly_once()
    {
        FakeSource source = Grid(rows: 3, columns: 2);
        var scan = new GridFindScan(source, Matcher("r"), startRow: 1, startColumn: 0, forward: false, wrap: true, maxMatches: 100, maxCells: 1000);
        RunToCompletion(scan);

        Assert.Equal(6, scan.CellsExamined);
        Assert.Equal(
            new[] { (1L, 0), (0L, 1), (0L, 0), (2L, 1), (2L, 0), (1L, 1) },
            scan.Matches.Select(m => (m.Row, m.Column)).ToArray());
    }

    // ---- slicing -----------------------------------------------------------------------------------

    [Fact]
    public void A_scan_run_in_slices_finds_the_same_cells_as_one_run_whole()
    {
        FakeSource sliced = Grid(rows: 5, columns: 4);
        var slicedScan = new GridFindScan(sliced, Matcher("c2"), 0, 0, forward: true, wrap: false, maxMatches: 100, maxCells: 1000);
        RunToCompletion(slicedScan, budget: 1);

        FakeSource whole = Grid(rows: 5, columns: 4);
        var wholeScan = new GridFindScan(whole, Matcher("c2"), 0, 0, forward: true, wrap: false, maxMatches: 100, maxCells: 1000);
        RunToCompletion(wholeScan, budget: 10_000);

        Assert.Equal(wholeScan.Matches.ToArray(), slicedScan.Matches.ToArray());
        Assert.Equal(5, slicedScan.Matches.Count);
    }

    [Fact]
    public void Step_reports_more_work_until_the_walk_is_done()
    {
        FakeSource source = Grid(rows: 2, columns: 2);
        var scan = new GridFindScan(source, Matcher("nothing"), 0, 0, forward: true, wrap: false, maxMatches: 100, maxCells: 1000);

        Assert.True(scan.Step(1));
        Assert.False(scan.Finished);
        Assert.True(scan.Step(2));
        Assert.False(scan.Step(1));
        Assert.True(scan.Finished);
        Assert.Equal(4, scan.CellsExamined);
    }

    // ---- caps --------------------------------------------------------------------------------------

    [Fact]
    public void The_match_cap_stops_the_scan_and_says_so()
    {
        FakeSource source = Grid(rows: 10, columns: 4);
        var scan = new GridFindScan(source, Matcher("r"), 0, 0, forward: true, wrap: false, maxMatches: 3, maxCells: 1000);
        RunToCompletion(scan);

        Assert.Equal(3, scan.Matches.Count);
        Assert.True(scan.StoppedAtMatchCap);
        Assert.False(scan.StoppedAtCellCap);
    }

    [Fact]
    public void The_cell_cap_stops_the_scan_and_says_so()
    {
        FakeSource source = Grid(rows: 10, columns: 4);
        var scan = new GridFindScan(source, Matcher("nothing"), 0, 0, forward: true, wrap: false, maxMatches: 100, maxCells: 7);
        RunToCompletion(scan);

        Assert.Equal(7, scan.CellsExamined);
        Assert.True(scan.StoppedAtCellCap);
        Assert.False(scan.StoppedAtMatchCap);
    }

    [Fact]
    public void A_scan_that_covers_everything_reports_neither_cap()
    {
        // The negative case is the one that matters: these two flags are what stop a partial count being
        // presented as a total, so they must not be set on a scan that genuinely saw every cell.
        FakeSource source = Grid(rows: 4, columns: 3);
        var scan = new GridFindScan(source, Matcher("r"), 0, 0, forward: true, wrap: false, maxMatches: 100, maxCells: 1000);
        RunToCompletion(scan);

        Assert.Equal(12, scan.Matches.Count);
        Assert.False(scan.StoppedAtCellCap);
        Assert.False(scan.StoppedAtMatchCap);
    }

    // ---- degenerate grids --------------------------------------------------------------------------

    [Fact]
    public void An_empty_grid_finishes_without_reading_anything()
    {
        var source = new FakeSource(new string[0, 0]);
        var scan = new GridFindScan(source, Matcher("x"), 0, 0, forward: true, wrap: true, maxMatches: 100, maxCells: 1000);

        Assert.True(scan.Finished);
        Assert.False(scan.Step(100));
        Assert.Empty(source.Reads);
        Assert.Equal(0, scan.CellsExamined);
    }

    [Fact]
    public void A_start_position_outside_the_grid_is_clamped_rather_than_thrown()
    {
        FakeSource source = Grid(rows: 2, columns: 2);
        var scan = new GridFindScan(source, Matcher("r"), startRow: 99, startColumn: 99, forward: true, wrap: true, maxMatches: 100, maxCells: 1000);
        RunToCompletion(scan);

        Assert.Equal(4, scan.CellsExamined);
        Assert.Equal((1L, 1), (scan.Matches[0].Row, scan.Matches[0].Column));
    }

    // ---- prefetch ----------------------------------------------------------------------------------

    [Fact]
    public void Rows_are_prefetched_in_blocks_rather_than_one_call_per_row()
    {
        FakeSource source = Grid(rows: 40, columns: 3);
        var scan = new GridFindScan(source, Matcher("nothing"), 0, 0, forward: true, wrap: false, maxMatches: 100, maxCells: 10_000);
        RunToCompletion(scan);

        // One block covering everything read — not 40 calls, which on a real grid is 40 round trips.
        Assert.Single(source.Prefetches);
        Assert.Equal(0L, source.Prefetches[0].From);
        Assert.Equal(39L, source.Prefetches[0].To);
    }

    // ---- options -----------------------------------------------------------------------------------

    [Fact]
    public void HighlightAll_alone_does_not_count_as_a_different_search()
    {
        // What this buys: ticking "Highlight all" repaints instead of re-reading the whole result set.
        var a = new GridFindOptions { MatchCase = true, HighlightAll = true };
        var b = new GridFindOptions { MatchCase = true, HighlightAll = false };
        Assert.True(a.MatchingEquals(b));

        Assert.False(a.MatchingEquals(new GridFindOptions { MatchCase = false }));
        Assert.False(a.MatchingEquals(new GridFindOptions { MatchCase = true, WholeCell = true }));
        Assert.False(a.MatchingEquals(new GridFindOptions { MatchCase = true, UseRegex = true }));
        Assert.False(a.MatchingEquals(new GridFindOptions { MatchCase = true, AllResultSets = true }));
    }
}
