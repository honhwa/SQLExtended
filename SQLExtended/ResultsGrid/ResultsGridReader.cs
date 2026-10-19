using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.SqlServer.Management.UI.Grid;

namespace SQLExtended.ResultsGrid;

/// <summary>
/// Locates SSMS results-grid controls and extracts their data as <see cref="ResultGridData"/>.
///
/// The results pane hosts one GridResultsGrid (: GridControl) per result set. The grid's
/// IGridStorage is a QueryExecution.QEResultSet at runtime; its column-name/type/null members are
/// internal to SQLEditors, so they're read via reflection with graceful fallbacks (header text +
/// type inference) if SSMS internals change. Storage cell indexes are grid-based — column 0 is the
/// row-number column and data starts at 1 — while schema/type lookups use the 0-based data index
/// (verified against SSMS 22's QEResultSet IL).
/// </summary>
internal static class ResultsGridReader
{
    /// <summary>Row cap so a huge grid can't freeze the UI thread while we read it.</summary>
    public const int MaxRows = 50000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>The grid under the keyboard focus, or null. This is the grid the user right-clicked
    /// when our command is invoked from the results context menu.</summary>
    public static GridControl GetFocusedGrid()
    {
        IntPtr h = GetFocus();
        return h == IntPtr.Zero ? null : Control.FromHandle(h) as GridControl;
    }

    /// <summary>All results grids inside the given window (e.g. the active query window's frame),
    /// in creation order — which matches result-set order.</summary>
    public static List<GridControl> FindGridsUnder(IntPtr hwnd)
    {
        var grids = new List<GridControl>();
        if (hwnd == IntPtr.Zero)
            return grids;
        EnumChildWindows(hwnd, (child, _) =>
        {
            if (Control.FromHandle(child) is GridControl grid)
                grids.Add(grid);
            return true;
        }, IntPtr.Zero);
        return grids;
    }

    /// <summary>
    /// The results grids of the active query window, in result-set order. Must run on the UI thread.
    ///
    /// <para>Reached through <c>DTE.ActiveDocument</c>, not <c>ActiveWindow</c>: the moment the user clicks into
    /// one of our own windows (the Find box, the Go to Column picker) <c>ActiveWindow</c> is that window, and grids
    /// would be looked for inside it and never found. <c>ActiveDocument</c> keeps naming the last active document
    /// whatever tool window holds the focus.</para>
    /// </summary>
    public static List<GridControl> FindActiveDocumentGrids()
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SDTE)) is not EnvDTE.DTE dte)
                return new List<GridControl>();

            IntPtr hwnd = IntPtr.Zero;
            try { hwnd = dte.ActiveDocument?.ActiveWindow?.HWnd ?? IntPtr.Zero; } catch { }
            if (hwnd == IntPtr.Zero)
            {
                try { hwnd = dte.ActiveWindow?.HWnd ?? IntPtr.Zero; } catch { }
            }

            return hwnd == IntPtr.Zero ? new List<GridControl>() : FindGridsUnder(hwnd);
        }
        catch
        {
            return new List<GridControl>();
        }
    }

    /// <summary>
    /// Which column space <see cref="GridControl.SelectedCells"/>' setter wants, which is not the same for
    /// every grid: it converts storage indexes to UI ones for <c>SingleCell</c>/<c>SingleColumn</c> and passes
    /// the other four selection types through untouched. That mirrors the getter exactly — the same asymmetry
    /// the aggregates reader documents — and getting it backwards selects a neighbouring column as soon as
    /// anyone drags a column header. Find and Go to Column both select through this.
    /// </summary>
    public static int SelectionColumn(GridControl grid, int storageColumn)
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

    /// <summary>Reads column names, SQL types, and (up to <see cref="MaxRows"/>) row values from a grid.
    /// Must run on the UI thread. <paramref name="totalRows"/> reports the untruncated count.</summary>
    public static ResultGridData Read(GridControl grid, out long totalRows)
    {
        IGridStorage storage = grid.GridStorage;
        totalRows = storage?.NumRows() ?? 0;

        // QEResultSet internals (best effort — nulls fall back to header text / inference)
        StringCollection colNames = null;
        MethodInfo formattedTypeName = null, isCellNull = null;
        if (storage != null)
        {
            Type st = storage.GetType();
            try { colNames = st.GetProperty("ColumnNames", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(storage) as StringCollection; } catch { }
            try { formattedTypeName = st.GetMethod("GetFormattedDataTypeName", new[] { typeof(int) }); } catch { }
            try { isCellNull = st.GetMethod("IsCellDataNull", new[] { typeof(long), typeof(int) }); } catch { }
        }

        int dataCols = colNames?.Count ?? Math.Max(0, grid.ColumnsNumber - 1);
        var data = new ResultGridData
        {
            ColumnNames = new string[dataCols],
            SqlTypes = new string[dataCols]
        };

        for (int c = 0; c < dataCols; c++)
        {
            if (colNames != null)
                data.ColumnNames[c] = colNames[c];
            else
            {
                try { grid.GetHeaderInfo(c + 1, out string header, out Bitmap _); data.ColumnNames[c] = header; } catch { }
            }
            if (formattedTypeName != null)
            {
                try { data.SqlTypes[c] = formattedTypeName.Invoke(storage, new object[] { c }) as string; } catch { formattedTypeName = null; }
            }
        }

        if (storage == null)
            return data;

        long rows = Math.Min(totalRows, MaxRows);
        for (long r = 0; r < rows; r++)
        {
            var row = new string[dataCols];
            for (int c = 0; c < dataCols; c++)
            {
                bool isNull = false;
                if (isCellNull != null)
                {
                    try { isNull = (bool)isCellNull.Invoke(storage, new object[] { r, c + 1 }); } catch { isCellNull = null; }
                }
                string value = isNull ? null : storage.GetCellDataAsString(r, c + 1);
                // Without QEResultSet null info, fall back to the grid's display convention.
                if (isCellNull == null && value == "NULL")
                    value = null;
                row[c] = value;
            }
            data.Rows.Add(row);
        }
        return data;
    }
}
