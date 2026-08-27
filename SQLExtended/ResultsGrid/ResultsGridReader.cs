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
