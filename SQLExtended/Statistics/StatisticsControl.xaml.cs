using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SQLExtended.Statistics.Capture;
using Microsoft.VisualStudio.Shell;
using StatisticsParser.Core.Formatting;
using StatisticsParser.Core.Models;
using StatisticsParser.Core.Parsing;

namespace SQLExtended.Statistics;

/// <summary>
/// Renders a <see cref="ParseResult"/> from the vendored StatisticsParser core: one IO grid per statement, the time
/// rows that followed it, then a grand-total section. The layout mirrors <see cref="TextReportBuilder"/> so the
/// "Copy as text" output matches what is on screen.
///
/// Grids are built in code rather than XAML because the column set varies per statement — the parser only emits the IO
/// columns SQL Server actually reported (and, with <see cref="StatisticsOptions.SuppressZeroColumns"/>, drops those
/// that are zero throughout).
/// </summary>
public partial class StatisticsControl : UserControl
{
    private AsyncPackage _package;
    private string _rawText;
    private ParseResult _parsed;

    public StatisticsControl()
    {
        InitializeComponent();
        UpdateButtonState();
    }

    /// <summary>Supplies the package the "Re-parse" button needs to run another capture.</summary>
    internal void SetPackage(AsyncPackage package) => _package = package;

    /// <summary>Replaces the report with a freshly parsed capture.</summary>
    internal void Render(string rawText, ParseResult parsed)
    {
        _rawText = rawText;
        _parsed = parsed;
        BlocksPanel.Children.Clear();

        var lang = parsed?.Language ?? ParserLanguage.English;
        LanguageText.Text = string.IsNullOrEmpty(lang.LangName) ? "" : lang.LangName;

        if (parsed == null || parsed.Data.Count == 0)
        {
            StatusText.Text = "Captured the Messages pane but found no STATISTICS output in it. "
                            + "Run the query with SET STATISTICS IO, TIME ON.";
            UpdateButtonState();
            return;
        }

        var nfi = NumberFormatFor(lang);

        // Time rows arrive after the statement they describe, so they are buffered and flushed as a single
        // block — same batching TextReportBuilder uses.
        var pendingTime = new List<TimeRow>();
        int statementNumber = 0;

        foreach (var row in parsed.Data)
        {
            if (row is TimeRow t)
            {
                if (t.Summary)
                {
                    FlushTime(pendingTime, lang);
                    AddTimeBlock(new[] { t }, lang, TextReportBuilder.SummaryNoticeText);
                }
                else
                {
                    pendingTime.Add(t);
                }
                continue;
            }

            FlushTime(pendingTime, lang);

            switch (row)
            {
                case IoGroup g:
                    AddIoGroupBlock(g, ++statementNumber, lang, nfi);
                    break;
                case RowsAffectedRow ra:
                    AddPlainLine(ra.Count.ToString("N0", nfi) + " " + ra.Label, "#9CDCFE");
                    break;
                case ErrorRow er:
                    AddPlainLine(er.Text, "#F14C4C");
                    break;
                case CompletionTimeRow ct:
                    AddPlainLine(CompletionTimeFormatter.Format(ct, convertToLocalTime: false), "#808080");
                    break;
                case InfoRow ir:
                    AddPlainLine(ir.Text, "#808080");
                    break;
            }
        }

        FlushTime(pendingTime, lang);
        AddTotalsSection(parsed, lang, nfi);

        StatusText.Text = statementNumber == 1
            ? "1 statement parsed."
            : statementNumber + " statements parsed.";
        UpdateButtonState();

        void FlushTime(List<TimeRow> pending, ParserLanguage l)
        {
            if (pending.Count == 0) return;
            AddTimeBlock(pending.ToList(), l, note: null);
            pending.Clear();
        }
    }

    /// <summary>Shows why a capture produced no text. Any existing report is left on screen.</summary>
    internal void ShowCaptureStatus(MessagesCaptureResult result)
    {
        StatusText.Text = result.Status switch
        {
            MessagesCaptureStatus.NoActiveWindow =>
                "No active SQL query window. Open a query window, run a query, then try again.",
            MessagesCaptureStatus.EmptyMessages =>
                "The Messages pane is empty. Run the query with SET STATISTICS IO, TIME ON.",
            MessagesCaptureStatus.ContractsAssemblyMissing =>
                "SSMS's query-editor brokered contracts assembly could not be found, so the Messages pane can't be read. "
                + "Details are in the SSMS ActivityLog.",
            MessagesCaptureStatus.ProxyUnavailable =>
                "SSMS did not hand out its query-editor brokered service — this usually means the SSMS version moved the "
                + "contract. Details are in the SSMS ActivityLog: " + (result.Error?.Message ?? ""),
            _ =>
                "Reading the Messages pane failed: " + (result.Error?.Message ?? "unknown error")
                + " Details are in the SSMS ActivityLog."
        };
    }

    private void UpdateButtonState()
    {
        bool hasReport = _parsed != null && _parsed.Data.Count > 0;
        CopyButton.IsEnabled = hasReport;
        CopyRawButton.IsEnabled = !string.IsNullOrEmpty(_rawText);
    }

    // --- Block builders ---

    private void AddIoGroupBlock(IoGroup group, int statementNumber, ParserLanguage lang, NumberFormatInfo nfi)
    {
        AddHeading("Statement " + statementNumber);

        var rows = new List<IoDisplayRow>();
        int rowNum = 0;
        foreach (var r in group.Data)
            rows.Add(IoDisplayRow.FromRow(r, ++rowNum, nfi));
        if (group.Total != null)
            rows.Add(IoDisplayRow.FromTotal(group.Total, nfi, lang.TotalLabel));

        BlocksPanel.Children.Add(BuildIoGrid(group.Columns, rows, lang, includeRowNum: true));
    }

    private void AddTotalsSection(ParseResult parsed, ParserLanguage lang, NumberFormatInfo nfi)
    {
        AddHeading(string.IsNullOrEmpty(lang.TotalsLabel) ? "Totals" : lang.TotalsLabel);

        var grand = parsed.Total.IoTotal;
        if (grand != null && grand.Data.Count > 0)
        {
            var rows = grand.Data.Select(t => IoDisplayRow.FromTotal(t, nfi, tableNameOverride: null)).ToList();
            if (grand.Total != null)
                rows.Add(IoDisplayRow.FromTotal(grand.Total, nfi, lang.TotalLabel));
            BlocksPanel.Children.Add(BuildIoGrid(grand.Columns, rows, lang, includeRowNum: false));
        }

        var timeRows = new List<TimeDisplayRow>
        {
            TimeDisplayRow.FromTotal(parsed.Total.CompileTotal, LabelFor(RowType.CompileTimeTotal, lang)),
            TimeDisplayRow.FromTotal(parsed.Total.ExecutionTotal, LabelFor(RowType.ExecutionTimeTotal, lang))
        };
        BlocksPanel.Children.Add(BuildTimeGrid(timeRows, lang));
    }

    private void AddTimeBlock(IList<TimeRow> times, ParserLanguage lang, string note)
    {
        var rows = times.Select(t => TimeDisplayRow.FromRow(t, LabelFor(t.RowType, lang))).ToList();
        BlocksPanel.Children.Add(BuildTimeGrid(rows, lang));
        if (!string.IsNullOrEmpty(note))
            AddPlainLine(note, "#808080");
    }

    private static string LabelFor(RowType rowType, ParserLanguage lang) => rowType switch
    {
        RowType.CompileTime or RowType.CompileTimeTotal => lang.CompileSectionLabel,
        _ => lang.ExecutionSectionLabel
    };

    private void AddHeading(string text) =>
        BlocksPanel.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("BlockHeading") });

    private void AddPlainLine(string text, string hexColor)
    {
        var block = new TextBlock { Text = text, Style = (Style)FindResource("PlainLine") };
        block.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(hexColor);
        BlocksPanel.Children.Add(block);
    }

    // --- Grid construction ---

    /// <summary>
    /// Builds the shared dark-themed grid chrome. Each block gets its own grid (rather than one grid for the whole
    /// report) because the statements don't share a column set.
    /// </summary>
    private DataGrid NewGrid()
    {
        return new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            CanUserAddRows = false,
            CanUserResizeRows = false,
            CanUserSortColumns = true,
            SelectionMode = DataGridSelectionMode.Extended,
            ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#2D2D30"),
            Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#1E1E1E"),
            BorderBrush = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#333337"),
            BorderThickness = new Thickness(1),
            RowBackground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#1E1E1E"),
            AlternationCount = 2,
            Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#D4D4D4"),
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Code, Consolas, Courier New"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 4),
            ColumnHeaderStyle = (Style)FindResource("DarkGridHeader"),
            RowStyle = (Style)FindResource("DarkGridRow"),
            CellStyle = (Style)FindResource("DarkGridCell")
        };
    }

    private DataGrid BuildIoGrid(IList<IoColumn> columns, IList<IoDisplayRow> rows, ParserLanguage lang, bool includeRowNum)
    {
        var grid = NewGrid();
        var numeric = (Style)FindResource("NumericCellText");
        var name = (Style)FindResource("NameCellText");

        if (includeRowNum)
            grid.Columns.Add(TextColumn(lang.HeaderRowNum, nameof(IoDisplayRow.RowNum), numeric));

        var tableColumn = TextColumn(lang.HeaderTable, nameof(IoDisplayRow.Table), name);
        tableColumn.MinWidth = 160;
        grid.Columns.Add(tableColumn);

        // Table is already rendered above and PercentRead always goes last, so both are skipped here.
        foreach (var col in columns)
        {
            if (col == IoColumn.Table || col == IoColumn.PercentRead) continue;
            var spec = SpecFor(col, lang);
            if (spec.Path == null) continue;
            grid.Columns.Add(TextColumn(spec.Header, spec.Path, numeric));
        }

        if (columns.Contains(IoColumn.PercentRead))
            grid.Columns.Add(TextColumn(lang.HeaderPercentRead, nameof(IoDisplayRow.PercentRead), numeric));

        grid.ItemsSource = rows;
        return grid;
    }

    private DataGrid BuildTimeGrid(IList<TimeDisplayRow> rows, ParserLanguage lang)
    {
        var grid = NewGrid();
        var numeric = (Style)FindResource("NumericCellText");
        var name = (Style)FindResource("NameCellText");

        var labelColumn = TextColumn(string.Empty, nameof(TimeDisplayRow.Label), name);
        labelColumn.MinWidth = 240;
        grid.Columns.Add(labelColumn);
        grid.Columns.Add(TextColumn(lang.CpuLabel, nameof(TimeDisplayRow.Cpu), numeric));
        grid.Columns.Add(TextColumn(lang.ElapsedLabel, nameof(TimeDisplayRow.Elapsed), numeric));

        grid.ItemsSource = rows;
        return grid;
    }

    private static DataGridTextColumn TextColumn(string header, string path, Style elementStyle) =>
        new()
        {
            Header = header ?? string.Empty,
            Binding = new System.Windows.Data.Binding(path),
            ElementStyle = elementStyle,
            Width = DataGridLength.Auto
        };

    /// <summary>Header text and <see cref="IoDisplayRow"/> property path for each IO column the parser can emit.</summary>
    private static (string Header, string Path) SpecFor(IoColumn col, ParserLanguage lang) => col switch
    {
        IoColumn.Scan => (lang.HeaderScan, nameof(IoDisplayRow.Scan)),
        IoColumn.Logical => (lang.HeaderLogical, nameof(IoDisplayRow.Logical)),
        IoColumn.Physical => (lang.HeaderPhysical, nameof(IoDisplayRow.Physical)),
        IoColumn.PageServer => (lang.HeaderPageServer, nameof(IoDisplayRow.PageServer)),
        IoColumn.ReadAhead => (lang.HeaderReadAhead, nameof(IoDisplayRow.ReadAhead)),
        IoColumn.PageServerReadAhead => (lang.HeaderPageServerReadAhead, nameof(IoDisplayRow.PageServerReadAhead)),
        IoColumn.LobLogical => (lang.HeaderLobLogical, nameof(IoDisplayRow.LobLogical)),
        IoColumn.LobPhysical => (lang.HeaderLobPhysical, nameof(IoDisplayRow.LobPhysical)),
        IoColumn.LobPageServer => (lang.HeaderLobPageServer, nameof(IoDisplayRow.LobPageServer)),
        IoColumn.LobReadAhead => (lang.HeaderLobReadAhead, nameof(IoDisplayRow.LobReadAhead)),
        IoColumn.LobPageServerReadAhead => (lang.HeaderLobPageServerReadAhead, nameof(IoDisplayRow.LobPageServerReadAhead)),
        IoColumn.SegmentReads => (lang.HeaderSegmentReads, nameof(IoDisplayRow.SegmentReads)),
        IoColumn.SegmentSkipped => (lang.HeaderSegmentSkipped, nameof(IoDisplayRow.SegmentSkipped)),
        _ => (null, null)
    };

    /// <summary>
    /// Grouping/decimal separators follow the parsed language, not the machine's regional settings — the numbers came
    /// from a SQL Server session in that language, so they should read back the same way.
    /// </summary>
    private static NumberFormatInfo NumberFormatFor(ParserLanguage lang)
    {
        var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        nfi.NumberGroupSeparator = lang.NumberFormat.ThousandSeparator;
        nfi.NumberDecimalSeparator = lang.NumberFormat.DecimalSeparator;
        return nfi;
    }

    // --- Event handlers ---

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_package == null)
        {
            StatusText.Text = "Re-parse is unavailable until the command has run once.";
            return;
        }
        StatisticsPresenter.Show(_package, activate: false);
    }

    private void CopyAsText_Click(object sender, RoutedEventArgs e)
    {
        if (_parsed == null) return;
        SetClipboard(TextReportBuilder.Build(_parsed), "Report copied as tab-separated text.");
    }

    private void CopyRaw_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_rawText)) return;
        SetClipboard(_rawText, "Raw Messages text copied.");
    }

    private void SetClipboard(string text, string okMessage)
    {
        try
        {
            Clipboard.SetText(text ?? string.Empty);
            StatusText.Text = okMessage;
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process; that must not take the tool window down.
            StatusText.Text = "Could not copy to the clipboard: " + ex.Message;
        }
    }

    // --- Display rows ---

    /// <summary>
    /// One grid row's values, pre-formatted as strings. Pre-formatting (rather than binding ints through a converter)
    /// keeps the grand-total and per-statement grids on the same number format and keeps the XAML converter-free.
    /// </summary>
    private sealed class IoDisplayRow
    {
        public string RowNum { get; set; } = "";
        public string Table { get; set; } = "";
        public string Scan { get; set; } = "";
        public string Logical { get; set; } = "";
        public string Physical { get; set; } = "";
        public string PageServer { get; set; } = "";
        public string ReadAhead { get; set; } = "";
        public string PageServerReadAhead { get; set; } = "";
        public string LobLogical { get; set; } = "";
        public string LobPhysical { get; set; } = "";
        public string LobPageServer { get; set; } = "";
        public string LobReadAhead { get; set; } = "";
        public string LobPageServerReadAhead { get; set; } = "";
        public string SegmentReads { get; set; } = "";
        public string SegmentSkipped { get; set; } = "";
        public string PercentRead { get; set; } = "";
        public bool IsTotal { get; set; }

        public static IoDisplayRow FromRow(IoRow r, int rowNum, NumberFormatInfo nfi) => new()
        {
            RowNum = rowNum.ToString("N0", nfi),
            Table = StatisticsOptions.FormatTableName(r.TableName),
            Scan = r.Scan.ToString("N0", nfi),
            Logical = r.Logical.ToString("N0", nfi),
            Physical = r.Physical.ToString("N0", nfi),
            PageServer = r.PageServer.ToString("N0", nfi),
            ReadAhead = r.ReadAhead.ToString("N0", nfi),
            PageServerReadAhead = r.PageServerReadAhead.ToString("N0", nfi),
            LobLogical = r.LobLogical.ToString("N0", nfi),
            LobPhysical = r.LobPhysical.ToString("N0", nfi),
            LobPageServer = r.LobPageServer.ToString("N0", nfi),
            LobReadAhead = r.LobReadAhead.ToString("N0", nfi),
            LobPageServerReadAhead = r.LobPageServerReadAhead.ToString("N0", nfi),
            SegmentReads = r.SegmentReads.ToString("N0", nfi),
            SegmentSkipped = r.SegmentSkipped.ToString("N0", nfi),
            PercentRead = r.PercentRead.ToString("F3", nfi) + "%",
            IsTotal = false
        };

        /// <summary>
        /// A total line. <paramref name="tableNameOverride"/> supplies the localized "Total" label for the bottom line
        /// of a grid; the grand-total table's per-table rows pass null and keep their own table name.
        /// </summary>
        public static IoDisplayRow FromTotal(IoGroupTotal t, NumberFormatInfo nfi, string tableNameOverride) => new()
        {
            RowNum = "",
            Table = tableNameOverride ?? StatisticsOptions.FormatTableName(t.TableName),
            Scan = t.Scan.ToString("N0", nfi),
            Logical = t.Logical.ToString("N0", nfi),
            Physical = t.Physical.ToString("N0", nfi),
            PageServer = t.PageServer.ToString("N0", nfi),
            ReadAhead = t.ReadAhead.ToString("N0", nfi),
            PageServerReadAhead = t.PageServerReadAhead.ToString("N0", nfi),
            LobLogical = t.LobLogical.ToString("N0", nfi),
            LobPhysical = t.LobPhysical.ToString("N0", nfi),
            LobPageServer = t.LobPageServer.ToString("N0", nfi),
            LobReadAhead = t.LobReadAhead.ToString("N0", nfi),
            LobPageServerReadAhead = t.LobPageServerReadAhead.ToString("N0", nfi),
            SegmentReads = t.SegmentReads.ToString("N0", nfi),
            SegmentSkipped = t.SegmentSkipped.ToString("N0", nfi),
            PercentRead = t.PercentRead.ToString("F3", nfi) + "%",
            IsTotal = tableNameOverride != null
        };
    }

    private sealed class TimeDisplayRow
    {
        public string Label { get; set; } = "";
        public string Cpu { get; set; } = "";
        public string Elapsed { get; set; } = "";
        public bool IsTotal { get; set; }

        public static TimeDisplayRow FromRow(TimeRow t, string label) => new()
        {
            Label = label,
            Cpu = TimeFormatter.FormatMs(t.CpuMs),
            Elapsed = TimeFormatter.FormatMs(t.ElapsedMs),
            IsTotal = false
        };

        public static TimeDisplayRow FromTotal(TimeTotal t, string label) => new()
        {
            Label = label,
            Cpu = TimeFormatter.FormatMs(t.CpuMs),
            Elapsed = TimeFormatter.FormatMs(t.ElapsedMs),
            IsTotal = true
        };
    }
}
