using EnvDTE;
using Microsoft.SqlServer.Management.UI.Grid;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQLExtended.Settings;
using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace SQLExtended.ResultsGrid.Aggregates;

/// <summary>
/// Keeps the Aggregates window fed with whatever is selected in a results grid.
///
/// <para><b>Everything here runs only while the window is visible.</b> The control switches the watcher on
/// and off from its own <c>IsVisibleChanged</c>, so a user who never opens the pane pays nothing — no
/// polling, no event handlers on SSMS's grids. That also covers the pane being tabbed behind another,
/// which is the common case for a docked window and is indistinguishable from closed as far as this is
/// concerned.</para>
///
/// <para><b>Grids are found by polling, because there is no event for them.</b> SSMS builds a fresh
/// <see cref="GridControl"/> per result set on every execution and exposes nothing to subscribe to, so
/// each tick enumerates the active query window and attaches to any grid not already seen. Attaching is
/// idempotent and handlers are dropped when a grid is disposed, so a session's worth of executions does
/// not accumulate subscriptions.</para>
///
/// <para><b>Recomputes are debounced.</b> <see cref="GridControl.SelectionChanged"/> fires continuously
/// while a drag is in progress; recomputing on each one would re-read the whole selection per mouse-move
/// and make the drag itself stutter.</para>
/// </summary>
internal static class GridAggregatesWatcher
{
    private static readonly HashSet<GridControl> _attached = new();
    private static DispatcherTimer _pollTimer;
    private static DispatcherTimer _debounceTimer;
    private static IGridAggregatesTarget _target;
    private static GridControl _pendingGrid;

    /// <summary>Non-null when "bring the window to the front when cells are selected" is on. This is the one
    /// thing that makes the watcher run with the window closed, which is why the setting is opt-in.</summary>
    private static AsyncPackage _autoShowPackage;

    /// <summary>The window the results are pushed to. Implemented by the control so the watcher does not
    /// need to resolve the tool window on every keystroke of a drag.</summary>
    public interface IGridAggregatesTarget
    {
        void ShowRead(GridSelectionRead read, long maxCells);
    }

    /// <summary>Called by the control when it becomes visible. Starts polling and computes once for the
    /// grid that already has focus, so the pane opens showing the current selection rather than blank.</summary>
    public static void Start(IGridAggregatesTarget target)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _target = target;
        EnsureTimers();
        _pollTimer.Start();

        AttachToVisibleGrids();
        Compute(ResultsGridReader.GetFocusedGrid() ?? _pendingGrid);
    }

    /// <summary>Creates the timers once and re-reads their intervals from settings, so a changed interval
    /// takes effect the next time the window is shown rather than needing an SSMS restart.</summary>
    private static void EnsureTimers()
    {
        var settings = SQLExtendedSettings.Current;

        if (_pollTimer == null)
        {
            _pollTimer = new DispatcherTimer();
            _pollTimer.Tick += (_, __) => AttachToVisibleGrids();
        }
        _pollTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, settings.GridAggregatesPollSeconds));

        if (_debounceTimer == null)
        {
            _debounceTimer = new DispatcherTimer();
            _debounceTimer.Tick += (_, __) =>
            {
                _debounceTimer.Stop();
                Compute(_pendingGrid);
            };
        }
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, settings.GridAggregatesDebounceMs));
    }

    /// <summary>Called by the control when it is hidden or closed. Stops the timers but leaves the grid
    /// handlers in place — they are cheap, and re-attaching on every show would race the poll.</summary>
    public static void Stop()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _target = null;
        _debounceTimer?.Stop();

        // Auto-show is the one thing that still needs new grids found while the window is closed.
        if (_autoShowPackage == null)
            _pollTimer?.Stop();
    }

    /// <summary>
    /// Turns on "bring the window to the front when cells are selected". Called at package load and
    /// whenever the setting changes.
    ///
    /// This is the only path that attaches to SSMS's grids with the window closed, which is exactly why
    /// the setting defaults to off: with it on, the extension is listening to every results grid for the
    /// whole session whether the feature is ever used or not.
    /// </summary>
    public static void ArmAutoShow(AsyncPackage package)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!SQLExtendedSettings.Current.GridAggregatesAutoShow)
        {
            _autoShowPackage = null;
            if (_target == null)
                _pollTimer?.Stop();
            return;
        }

        _autoShowPackage = package;
        EnsureTimers();
        _pollTimer.Start();
        AttachToVisibleGrids();
    }

    /// <summary>Recomputes now, skipping the debounce. Used by the window's own Refresh.</summary>
    public static void RefreshNow()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        AttachToVisibleGrids();
        Compute(ResultsGridReader.GetFocusedGrid() ?? _pendingGrid);
    }

    private static void AttachToVisibleGrids()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var dte = Package.GetGlobalService(typeof(SDTE)) as DTE;
            IntPtr hwnd = dte?.ActiveWindow?.HWnd ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
                return;

            foreach (var grid in ResultsGridReader.FindGridsUnder(hwnd))
            {
                if (grid == null || grid.IsDisposed || !_attached.Add(grid))
                    continue;

                grid.SelectionChanged += OnGridSelectionChanged;
                grid.Disposed += OnGridDisposed;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SQLExtended] Aggregates attach failed: {ex}");
        }
    }

    private static void OnGridDisposed(object sender, EventArgs e)
    {
        if (sender is not GridControl grid)
            return;
        grid.SelectionChanged -= OnGridSelectionChanged;
        grid.Disposed -= OnGridDisposed;
        _attached.Remove(grid);
        if (ReferenceEquals(_pendingGrid, grid))
            _pendingGrid = null;
    }

    private static void OnGridSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not GridControl grid)
            return;

        if (_target == null)
        {
            // Window closed. Only auto-show opens it, and only for an actual range — otherwise the window
            // would appear on every single click in a results grid, which is not what was asked for.
            if (_autoShowPackage == null || !IsRangeSelection(e?.SelectedBlocks))
                return;
            AggregatesCommand.Show(_autoShowPackage);
        }

        _pendingGrid = grid;
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    /// <summary>More than one cell — the gesture the aggregates window is for. A whole-column or whole-row
    /// selection reports a single block whose width and height are 1, so the selection type has to count too.</summary>
    private static bool IsRangeSelection(BlockOfCellsCollection blocks)
    {
        if (blocks == null || blocks.Count == 0)
            return false;
        if (blocks.Count > 1)
            return true;

        foreach (BlockOfCells block in blocks)
        {
            if (block != null && !block.IsEmpty && (block.Width > 1 || block.Height > 1))
                return true;
        }
        return false;
    }

    private static void Compute(GridControl grid)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var target = _target;
        if (target == null)
            return;

        _pendingGrid = grid;

        long maxCells = Math.Max(1L, SQLExtendedSettings.Current.GridAggregatesMaxCells);
        GridSelectionRead read = GridSelectionReader.Read(grid, maxCells);
        try { target.ShowRead(read, maxCells); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SQLExtended] Aggregates render failed: {ex}"); }
    }
}
