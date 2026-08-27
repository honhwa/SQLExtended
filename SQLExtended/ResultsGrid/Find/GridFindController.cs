using EnvDTE;
using Microsoft.SqlServer.Management.UI.Grid;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQLExtended.Settings;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Threading;

namespace SQLExtended.ResultsGrid.Find;

/// <summary>What the window shows about the search. Everything the status line needs, already decided.</summary>
internal sealed class GridFindStatus
{
    public string Text { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public bool IsSearching { get; set; }

    /// <summary>1-based position of the current match, 0 when none.</summary>
    public int Ordinal { get; set; }

    public int Count { get; set; }

    /// <summary>The count is a floor rather than a total — the scan is still running, or a cap stopped it.</summary>
    public bool CountIsPartial { get; set; }

    public bool CanStep { get; set; }
}

internal interface IGridFindHost
{
    void ReportStatus(GridFindStatus status);
}

/// <summary>
/// Runs a find across the results grids of the active query window: scans them, tints the matches, and
/// steps the grid's own selection from one to the next.
///
/// <para><b>Matches are always collected, and "Highlight all" only decides what is painted.</b> The
/// alternative — an incremental find that stops at the first hit — cannot say "3 of 47", makes every
/// subsequent step re-scan, and has to re-read everything again when the user steps backwards. Since the
/// scan is sliced and capped anyway, collecting is both cheaper in aggregate and the only way the count is
/// honest. It also means toggling a checkbox that cannot change what matches (see
/// <see cref="GridFindOptions.MatchingEquals"/>) repaints instead of re-reading a million cells.</para>
///
/// <para><b>Scanning is sliced across dispatcher ticks, never awaited.</b> Grid storage is readable only on
/// the UI thread, so the work is broken into short slices and the thread handed back between them: the
/// window keeps painting, the term stays editable, and stepping works against the matches found so far
/// while the rest is still being read.</para>
/// </summary>
internal sealed class GridFindController : IDisposable
{
    /// <summary>Cells per inner chunk, and how long a slice may run before the UI thread gets it back. Small
    /// enough that a slice cannot be felt, large enough that the per-slice overhead disappears.</summary>
    private const long CellsPerChunk = 2_000;
    private const int SliceMs = 12;

    private readonly IGridFindHost _host;
    private readonly Dictionary<GridControl, GridFindHighlighter> _highlighters = new();
    private readonly List<GridStorageCellSource> _sources = new();
    private readonly List<Hit> _matches = new();

    private DispatcherTimer _scanTimer;
    private GridFindScan _scan;
    private int _scanSourceIndex = -1;
    private int _harvested;

    private GridFindMatcher _matcher;
    private string _term = string.Empty;
    private GridFindOptions _options = new();

    private int _current = -1;
    private bool _jumpedToFirst;

    private GridControl _preferredGrid;
    private long _cellBudgetRemaining;
    private long _cellsExaminedInFinishedScans;
    private long _cellCap;
    private int _matchCap;
    private bool _cellCapHit;
    private bool _matchCapHit;
    private bool _suspended;

    public GridFindController(IGridFindHost host) => _host = host;

    private readonly struct Hit
    {
        public Hit(int sourceIndex, GridCellPosition position)
        {
            SourceIndex = sourceIndex;
            Position = position;
        }

        public int SourceIndex { get; }
        public GridCellPosition Position { get; }
    }

    private long TotalCellsExamined => _cellsExaminedInFinishedScans + (_scan?.CellsExamined ?? 0);

    /// <summary>
    /// The entry point the window calls for everything. Re-scans only when the options could change which
    /// cells match; otherwise it repaints what it already has.
    /// </summary>
    public void ApplyOptions(string term, GridFindOptions options)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        options ??= new GridFindOptions();

        if (_matcher != null && string.Equals(term, _term, StringComparison.Ordinal) && _options.MatchingEquals(options))
        {
            _options.HighlightAll = options.HighlightAll;
            RefreshHighlights();
            ReportProgress();
            return;
        }

        Search(term, options);
    }

    /// <summary>Starts a fresh search, discarding whatever the last one found.</summary>
    public void Search(string term, GridFindOptions options)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        StopScan();
        _term = term ?? string.Empty;
        _options = (options ?? new GridFindOptions()).Clone();
        _matches.Clear();
        _matcher = null;
        _current = -1;
        _jumpedToFirst = false;
        _cellCapHit = false;
        _matchCapHit = false;
        _cellsExaminedInFinishedScans = 0;

        if (_term.Length == 0)
        {
            ClearHighlights();
            ReportProgress();
            return;
        }

        _matcher = GridFindMatcher.Create(_term, _options, out string error);
        if (_matcher == null)
        {
            ClearHighlights();
            _host?.ReportStatus(new GridFindStatus { Text = error, IsError = true });
            return;
        }

        if (!BuildSources())
        {
            ClearHighlights();
            _host?.ReportStatus(new GridFindStatus { Text = "No results grid to search — run a query first.", IsError = true });
            return;
        }

        var settings = SQLExtendedSettings.Current;
        _cellCap = Math.Max(1L, settings.GridFindMaxCells);
        _matchCap = Math.Max(1, settings.GridFindMaxMatches);
        _cellBudgetRemaining = _cellCap;

        RefreshHighlights();
        _scanSourceIndex = -1;
        StartNextSourceScan();
    }

    /// <summary>Moves to the next or previous match, wrapping. Works while a scan is still running — it just
    /// wraps within what has been found so far.</summary>
    public void Step(bool forward)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (_matches.Count == 0)
        {
            ReportProgress();
            return;
        }

        if (_current < 0)
            _current = forward ? 0 : _matches.Count - 1;
        else
            _current = forward ? (_current + 1) % _matches.Count : (_current - 1 + _matches.Count) % _matches.Count;

        SelectCurrent();
        ReportProgress();
    }

    /// <summary>
    /// Called on a timer while the window is open. Two jobs: remember which grid the user was last in (the
    /// search box has the focus by the time they type, so it cannot be asked for then), and notice when the
    /// grids have been replaced — re-executing the query builds new ones, and a search still pointing at the
    /// old ones silently finds nothing and highlights nothing.
    /// </summary>
    public void SyncGrids()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_suspended)
            return;

        GridControl focused = ResultsGridReader.GetFocusedGrid();
        if (focused != null)
            _preferredGrid = focused;

        if (_term.Length == 0 || _matcher == null || _scan != null)
            return;

        var grids = FindGrids();
        if (grids.Count == 0)
            return;

        bool stale = _sources.Count == 0 || _sources.Any(s => !s.IsAlive);
        bool scopeChanged = _options.AllResultSets
            ? grids.Count != _sources.Count || !grids.All(g => _sources.Any(s => ReferenceEquals(s.Grid, g)))
            : _preferredGrid != null && _sources.Count == 1 && !ReferenceEquals(_sources[0].Grid, _preferredGrid) && grids.Contains(_preferredGrid);

        if (stale || scopeChanged)
            Search(_term, _options);
    }

    /// <summary>The window was hidden or closed. Highlights come off the grids: a tinted results grid with no
    /// window on screen to explain it is not something the user can undo.</summary>
    public void Suspend()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _suspended = true;
        StopScan();
        ClearHighlights();
    }

    /// <summary>The window is visible again — re-run whatever is still in the box, against whatever grids are
    /// there now.</summary>
    public void Resume()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _suspended = false;
        if (_term.Length > 0)
            Search(_term, _options);
    }

    public void Dispose()
    {
        StopScan();
        ClearHighlights();
    }

    // ---- grids -------------------------------------------------------------------------------------

    private List<GridControl> FindGrids()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (Package.GetGlobalService(typeof(SDTE)) is not DTE dte)
                return new List<GridControl>();

            // ActiveWindow is *this* tool window the moment the user clicks into the search box, so the query
            // window has to be reached through ActiveDocument, which keeps naming the last active document
            // whatever tool window holds the focus. Reading ActiveWindow alone would look for grids inside
            // the search window and always find none — the feature would work only while the grid had focus,
            // which is never, because typing is how a search starts.
            IntPtr hwnd = IntPtr.Zero;
            try { hwnd = dte.ActiveDocument?.ActiveWindow?.HWnd ?? IntPtr.Zero; } catch { }
            if (hwnd == IntPtr.Zero)
            {
                try { hwnd = dte.ActiveWindow?.HWnd ?? IntPtr.Zero; } catch { }
            }

            return hwnd == IntPtr.Zero ? new List<GridControl>() : ResultsGridReader.FindGridsUnder(hwnd);
        }
        catch
        {
            return new List<GridControl>();
        }
    }

    private bool BuildSources()
    {
        var grids = FindGrids();
        if (grids.Count == 0)
            return false;

        if (!_options.AllResultSets)
        {
            GridControl chosen = _preferredGrid != null && grids.Contains(_preferredGrid) ? _preferredGrid : grids[0];
            grids = new List<GridControl> { chosen };
        }

        _sources.Clear();
        foreach (GridControl grid in grids)
        {
            GridStorageCellSource source = GridStorageCellSource.Create(grid);
            if (source != null)
                _sources.Add(source);
        }

        // Grids that have dropped out of scope keep their tint otherwise — unticking "All result sets" would
        // leave every other grid looking like it still has live matches in it.
        foreach (var pair in _highlighters.Where(p => !_sources.Any(s => ReferenceEquals(s.Grid, p.Key))).ToList())
        {
            pair.Value.Dispose();
            _highlighters.Remove(pair.Key);
        }

        return _sources.Count > 0;
    }

    // ---- scanning ----------------------------------------------------------------------------------

    private void StartNextSourceScan()
    {
        _scanSourceIndex++;
        while (_scanSourceIndex < _sources.Count && !_sources[_scanSourceIndex].IsAlive)
            _scanSourceIndex++;

        if (_scanSourceIndex >= _sources.Count || _matches.Count >= _matchCap || _cellBudgetRemaining <= 0)
        {
            FinishScan();
            return;
        }

        _harvested = 0;
        _scan = new GridFindScan(_sources[_scanSourceIndex], _matcher, startRow: 0, startColumn: 0, forward: true, wrap: false,
                                 maxMatches: _matchCap - _matches.Count, maxCells: _cellBudgetRemaining);

        EnsureTimer();
        _scanTimer.Start();
        ReportProgress();
    }

    private void EnsureTimer()
    {
        if (_scanTimer != null)
            return;

        // Background priority so painting, input and the caret all outrank the scan: the window must stay
        // usable while a large grid is being read, not merely finish quickly.
        _scanTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1) };
        _scanTimer.Tick += OnScanTick;
    }

    private void OnScanTick(object sender, EventArgs e)
    {
        if (_scan == null)
        {
            _scanTimer?.Stop();
            return;
        }

        bool more;
        try
        {
            var slice = Stopwatch.StartNew();
            do
            {
                more = _scan.Step(CellsPerChunk);
            }
            while (more && slice.ElapsedMilliseconds < SliceMs);
        }
        catch (Exception ex)
        {
            // The grid was torn down underneath the scan — a re-execution while we were reading. Stop, and
            // let the next SyncGrids notice the new grids and start again.
            System.Diagnostics.Debug.WriteLine($"[SQLExtended] Grid find scan failed: {ex}");
            StopScan();
            ReportProgress();
            return;
        }

        Harvest();

        if (!_jumpedToFirst && _matches.Count > 0)
        {
            _jumpedToFirst = true;
            _current = 0;
            SelectCurrent();
        }

        if (!more)
        {
            _cellsExaminedInFinishedScans += _scan.CellsExamined;
            _cellBudgetRemaining -= _scan.CellsExamined;
            _cellCapHit |= _scan.StoppedAtCellCap;
            _matchCapHit |= _scan.StoppedAtMatchCap;

            _scanTimer.Stop();
            _scan = null;
            StartNextSourceScan();
            return;
        }

        ReportProgress();
    }

    /// <summary>Copies whatever the running scan has found since the last tick into the shared match list and
    /// repaints that grid, so tints and the count appear as the scan goes rather than at the end.</summary>
    private void Harvest()
    {
        IReadOnlyList<GridCellPosition> found = _scan.Matches;
        if (found.Count == _harvested)
            return;

        for (int i = _harvested; i < found.Count; i++)
            _matches.Add(new Hit(_scanSourceIndex, found[i]));
        _harvested = found.Count;

        UpdateHighlight(_scanSourceIndex);
    }

    private void FinishScan()
    {
        _scanTimer?.Stop();
        _scan = null;
        RefreshHighlights();
        ReportProgress();
    }

    private void StopScan()
    {
        _scanTimer?.Stop();
        _scan = null;
        _harvested = 0;
    }

    // ---- grid interaction --------------------------------------------------------------------------

    private void SelectCurrent()
    {
        if (_current < 0 || _current >= _matches.Count)
            return;

        Hit hit = _matches[_current];
        if (hit.SourceIndex < 0 || hit.SourceIndex >= _sources.Count)
            return;

        GridStorageCellSource source = _sources[hit.SourceIndex];
        if (!source.IsAlive)
            return;

        GridControl grid = source.Grid;
        int storageColumn = hit.Position.Column + 1;

        try
        {
            // Keeps the match visible while the search box holds the focus (the highlighter sets this too;
            // it is set here as well because a grid can be selected into before it has a highlighter, when
            // "Highlight all" is off).
            grid.AlwaysHighlightSelection = true;

            var blocks = new BlockOfCellsCollection();
            blocks.Add(new BlockOfCells(hit.Position.Row, SelectionColumn(grid, storageColumn)));
            grid.SelectedCells = blocks;

            // EnsureCellIsVisible takes a storage index and converts it itself — unlike the selection setter
            // above, which does so only for two of the six selection types.
            grid.EnsureCellIsVisible(hit.Position.Row, storageColumn);
        }
        catch (Exception ex)
        {
            // The grid validates the block against its current size and throws if the result set has shrunk
            // underneath us. Not worth a banner: the next sync re-runs the search.
            System.Diagnostics.Debug.WriteLine($"[SQLExtended] Grid find select failed: {ex}");
        }
    }

    /// <summary>
    /// Which column space <see cref="GridControl.SelectedCells"/>' setter wants, which is not the same for
    /// every grid: it converts storage indexes to UI ones for <c>SingleCell</c>/<c>SingleColumn</c> and passes
    /// the other four selection types through untouched. That mirrors the getter exactly — the same asymmetry
    /// the aggregates reader documents — and getting it backwards selects a neighbouring column as soon as
    /// anyone drags a column header.
    /// </summary>
    private static int SelectionColumn(GridControl grid, int storageColumn)
    {
        try
        {
            GridSelectionType type = grid.SelectionType;
            if (type == GridSelectionType.SingleCell || type == GridSelectionType.SingleColumn)
                return storageColumn;
            return grid.GetUIColumnIndexByStorageIndex(storageColumn);
        }
        catch
        {
            return storageColumn;
        }
    }

    private GridFindHighlighter GetHighlighter(GridControl grid)
    {
        if (grid == null || grid.IsDisposed)
            return null;

        if (_highlighters.TryGetValue(grid, out GridFindHighlighter existing))
            return existing;

        try
        {
            var highlighter = new GridFindHighlighter(grid);
            _highlighters[grid] = highlighter;
            return highlighter;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateHighlight(int sourceIndex)
    {
        if (sourceIndex < 0 || sourceIndex >= _sources.Count)
            return;

        GridStorageCellSource source = _sources[sourceIndex];
        if (!source.IsAlive)
            return;

        GridFindHighlighter highlighter = GetHighlighter(source.Grid);
        if (highlighter == null)
            return;

        if (!_options.HighlightAll)
        {
            highlighter.Clear();
            return;
        }

        var positions = new List<GridCellPosition>();
        foreach (Hit hit in _matches)
        {
            if (hit.SourceIndex == sourceIndex)
                positions.Add(hit.Position);
        }
        highlighter.SetMatches(positions);
    }

    private void RefreshHighlights()
    {
        for (int i = 0; i < _sources.Count; i++)
            UpdateHighlight(i);
    }

    private void ClearHighlights()
    {
        foreach (GridFindHighlighter highlighter in _highlighters.Values)
            highlighter.Dispose();
        _highlighters.Clear();
    }

    // ---- status ------------------------------------------------------------------------------------

    private void ReportProgress()
    {
        var status = new GridFindStatus
        {
            IsSearching = _scan != null,
            Ordinal = _current + 1,
            Count = _matches.Count,
            CountIsPartial = _scan != null || _matchCapHit || _cellCapHit,
            CanStep = _matches.Count > 0
        };
        status.Text = BuildStatusText(status);
        _host?.ReportStatus(status);
    }

    private string BuildStatusText(GridFindStatus status)
    {
        if (_term.Length == 0)
            return "Type something to find in the results grid.";

        if (_matches.Count == 0)
        {
            if (status.IsSearching)
                return $"Searching… {TotalCellsExamined:N0} cells so far.";
            if (_cellCapHit)
                return $"No matches in the first {TotalCellsExamined:N0} cells — the search stopped at the cell limit. Raise it in SQLExtended settings.";
            return $"No matches ({TotalCellsExamined:N0} cells searched{ScopeSuffix()}).";
        }

        var text = new StringBuilder();
        string count = status.CountIsPartial ? $"{_matches.Count:N0}+" : $"{_matches.Count:N0}";

        if (_current >= 0 && _current < _matches.Count)
        {
            Hit hit = _matches[_current];
            text.Append($"Match {_current + 1:N0} of {count}");
            if (hit.SourceIndex >= 0 && hit.SourceIndex < _sources.Count)
            {
                // Row is shown 1-based to line up with the grid's own row-number column.
                text.Append($" · {_sources[hit.SourceIndex].ColumnName(hit.Position.Column)}, row {hit.Position.Row + 1:N0}");
                if (_sources.Count > 1)
                    text.Append($", result set {hit.SourceIndex + 1}");
            }
        }
        else
        {
            text.Append($"{count} matches");
        }

        if (status.IsSearching)
            text.Append(" · still searching…");
        else if (_matchCapHit)
            text.Append($" · stopped at the {_matchCap:N0}-match limit");
        else if (_cellCapHit)
            text.Append($" · stopped at the {_cellCap:N0}-cell limit");

        if (_matcher != null && _matcher.TimedOut)
            text.Append(" · a regular expression timed out on some cells, so this is not the whole answer");

        return text.ToString();
    }

    private string ScopeSuffix() => _sources.Count > 1 ? $" across {_sources.Count} result sets" : string.Empty;
}
