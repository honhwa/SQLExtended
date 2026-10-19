using Microsoft.SqlServer.Management.UI.Grid;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel.Design;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.ResultsGrid.Columns;

/// <summary>
/// "Go to Column" (Ctrl+Alt+K) — a picker over the results grid's column names that filters as you type and, on
/// Enter, moves the grid's selection to that column in the current row and scrolls it into view. Also on the grid's
/// right-click menu and the SQLExtended menu, beside Find in Results.
///
/// <para>Everything the grid is asked here was established by <see cref="ResultsGridReader"/> and Find in Results:
/// column names come from <c>QEResultSet.ColumnNames</c> by reflection, falling back to the header text; storage
/// column 0 is the row-number column; and the selection is set through <see cref="ResultsGridReader.SelectionColumn"/>,
/// which is the one place that knows which column space the setter wants.</para>
/// </summary>
internal sealed class GoToColumnCommand
{
    private const int CommandId = 0x0c30;

    private GoToColumnCommand(OleMenuCommandService commandService) =>
        commandService.AddCommand(new MenuCommand(Execute, new CommandID(Find.GridFindCommand.CommandSet, CommandId)));

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            _ = new GoToColumnCommand(commandService);
    }

    private static void Execute(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            // Read before anything takes the focus: the grid holding it is the one the user means, and is the one the
            // shortcut and the right-click menu are both invoked from.
            GridControl focused = ResultsGridReader.GetFocusedGrid();
            List<GridControl> grids = ResultsGridReader.FindActiveDocumentGrids();
            if (focused != null && !grids.Contains(focused))
                grids.Insert(0, focused);
            grids.RemoveAll(g => g == null || g.IsDisposed);

            var entries = ReadColumns(grids, focused);
            if (entries.Count == 0)
            {
                SetStatusBar("Go to Column: no results grid with columns — run a query first.");
                return;
            }

            var window = new GoToColumnWindow(entries);
            Place(window, focused ?? grids[0]);
            if (window.ShowDialog() != true || window.Selected is not { } picked)
                return;

            // Once the dialog has handed activation back to the shell: focusing the grid any earlier loses the focus
            // to the main window as it reactivates, and the grid paints no selection while unfocused.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Go(picked)), System.Windows.Threading.DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            Diagnostics.SQLExtendedLog.Warning("GoToColumn", "Go to Column failed", ex);
        }
    }

    // --- Reading ---

    /// <summary>
    /// One entry per data column of every grid, the focused grid's first. Names and types come from the result set
    /// itself where SSMS exposes them, as <see cref="ResultsGridReader.Read"/> does.
    /// </summary>
    private static List<GridColumnEntry> ReadColumns(List<GridControl> grids, GridControl focused)
    {
        var entries = new List<GridColumnEntry>();
        var inResultOrder = ResultsGridReader.FindActiveDocumentGrids();

        for (int g = 0; g < grids.Count; g++)
        {
            GridControl grid = grids[g];
            IGridStorage storage;
            try { storage = grid.GridStorage; } catch { continue; }

            StringCollection names = null;
            MethodInfo typeName = null;
            if (storage != null)
            {
                Type st = storage.GetType();
                try { names = st.GetProperty("ColumnNames", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(storage) as StringCollection; } catch { }
                try { typeName = st.GetMethod("GetFormattedDataTypeName", new[] { typeof(int) }); } catch { }
            }

            int columns;
            try { columns = names?.Count ?? Math.Max(0, grid.ColumnsNumber - 1); } catch { continue; }

            int resultIndex = inResultOrder.IndexOf(grid);
            string label = grids.Count > 1 ? $"Result {(resultIndex >= 0 ? resultIndex + 1 : g + 1)}" : null;

            for (int c = 0; c < columns; c++)
            {
                int storageColumn = c + 1;
                string name = names != null && c < names.Count ? names[c] : null;
                if (string.IsNullOrEmpty(name))
                {
                    try { grid.GetHeaderInfo(storageColumn, out string header, out Bitmap _); name = header; } catch { }
                }

                string type = null;
                if (typeName != null)
                {
                    try { type = typeName.Invoke(storage, new object[] { c }) as string; } catch { typeName = null; }
                }

                // The row-number column is UI 0 as well as storage 0, so a UI index is already the 1-based position.
                int position = storageColumn;
                try { position = grid.GetUIColumnIndexByStorageIndex(storageColumn); } catch { }

                entries.Add(new GridColumnEntry
                {
                    GridOrder = ReferenceEquals(grid, focused) ? 0 : g + 1,
                    GridLabel = label,
                    StorageColumn = storageColumn,
                    Position = position,
                    Name = string.IsNullOrEmpty(name) ? $"Column {storageColumn}" : name,
                    DataType = type,
                    Grid = grid
                });
            }
        }

        return entries;
    }

    // --- Going there ---

    /// <summary>
    /// Selects the chosen column in the row the grid's selection is already on (row 0 if it has none), scrolls it into
    /// view and gives the grid the focus, so the arrow keys carry on from there.
    /// </summary>
    private static void Go(GridColumnEntry entry)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (entry?.Grid is not GridControl grid || grid.IsDisposed)
            return;

        try
        {
            long rows = grid.GridStorage?.NumRows() ?? 0;
            long row = CurrentRow(grid, rows);

            grid.Focus();
            if (rows > 0)
            {
                var blocks = new BlockOfCellsCollection();
                blocks.Add(new BlockOfCells(row, ResultsGridReader.SelectionColumn(grid, entry.StorageColumn)));
                grid.SelectedCells = blocks;

                // EnsureCellIsVisible takes a storage index and converts it itself — unlike the selection setter.
                grid.EnsureCellIsVisible(row, entry.StorageColumn);
            }
        }
        catch (Exception ex)
        {
            // The grid validates the block against its current size; a result set re-run underneath the picker throws here.
            Diagnostics.SQLExtendedLog.Info("GoToColumn", $"Could not select column {entry.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// The row of the grid's current selection. A block's Y is a real row for every selection type: for a whole-column
    /// selection it is where the click landed, which is still a row the user put the cursor on.
    /// </summary>
    private static long CurrentRow(GridControl grid, long rows)
    {
        try
        {
            BlockOfCellsCollection blocks = grid.SelectedCells;
            if (blocks != null && blocks.Count > 0)
            {
                long y = blocks[0].Y;
                if (y >= 0 && y < rows)
                    return y;
            }
        }
        catch { }
        return 0;
    }

    // --- Placement ---

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// Over the top of the grid, centred, where the eye already is. The grid reports device pixels and WPF wants device
    /// independent ones, so the grid's own DPI converts between them; a grid with no size (a hidden Results tab) gets
    /// the picker centred on screen instead.
    /// </summary>
    private static void Place(GoToColumnWindow window, GridControl anchor)
    {
        try
        {
            if (anchor != null && anchor.Visible && anchor.Width > 0 && anchor.Height > 0)
            {
                double scale = 1.0;
                try { uint dpi = GetDpiForWindow(anchor.Handle); if (dpi > 0) scale = dpi / 96.0; } catch { }

                Point origin = anchor.PointToScreen(new Point(0, 0));
                double gridLeft = origin.X / scale, gridTop = origin.Y / scale, gridWidth = anchor.Width / scale;

                window.Left = gridLeft + Math.Max(0, (gridWidth - window.Width) / 2);
                window.Top = gridTop + 24;
                return;
            }
        }
        catch { }

        window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
    }

    private static void SetStatusBar(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar bar)
                bar.SetText(text);
        }
        catch { }
    }
}
