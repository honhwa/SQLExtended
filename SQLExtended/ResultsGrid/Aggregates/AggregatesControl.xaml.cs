using SQLExtended.Settings;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SQLExtended.ResultsGrid.Aggregates;

/// <summary>
/// The Grid Aggregates window: one row per selected results-grid column, plus a combined row when more
/// than one column is selected.
///
/// <para><b>Per-column rather than one figure for the whole selection.</b> Selecting three columns and
/// being shown a single Sum answers a question nobody asked — an order id added to a unit price. The
/// combined row is still offered for the Excel-status-bar case, but it is the extra, not the headline.</para>
///
/// <para>The watcher is started and stopped from <see cref="UIElement.IsVisible"/>, so a docked window
/// tabbed behind another costs nothing — and a user who never opens this window never has a handler
/// attached to an SSMS grid at all.</para>
/// </summary>
public partial class AggregatesControl : UserControl, GridAggregatesWatcher.IGridAggregatesTarget
{
    private readonly List<AggregateRow> _rows = new();
    private bool _loadingSettings;

    public AggregatesControl()
    {
        InitializeComponent();
        LoadColumnChoices();
        AggregatesGrid.ItemsSource = _rows;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>Row shape bound by the XAML. Everything is pre-rendered to text: the grid shows numbers
    /// from several different sources and one formatting rule per column beats a converter per type.</summary>
    private sealed class AggregateRow
    {
        public string ColumnName { get; set; }
        public string TypeText { get; set; }
        public string Cells { get; set; }
        public string NonNull { get; set; }
        public string Nulls { get; set; }
        public string Blanks { get; set; }
        public string Distinct { get; set; }
        public string Sum { get; set; }
        public string Average { get; set; }
        public string Min { get; set; }
        public string Max { get; set; }
        public string TotalChars { get; set; }
        public string MaxChars { get; set; }
        public bool IsTotal { get; set; }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            GridAggregatesWatcher.Start(this);
        else
            GridAggregatesWatcher.Stop();
    }

    /// <summary>Receives a read from the watcher. Always on the UI thread — the watcher's timers and the
    /// grid's own events both run there, and the grid could not be read from anywhere else.
    ///
    /// Implemented explicitly: the control is public (XAML generates it so) while the read types are
    /// internal, and a public method cannot take them.</summary>
    void GridAggregatesWatcher.IGridAggregatesTarget.ShowRead(GridSelectionRead read, long maxCells)
    {
        _rows.Clear();

        if (read == null || read.Status == GridSelectionStatus.NoSelection)
        {
            AggregatesGrid.Items.Refresh();
            HideNotice();
            SetStatus("Select cells in a results grid.");
            return;
        }

        if (read.Status == GridSelectionStatus.TooLarge)
        {
            AggregatesGrid.Items.Refresh();
            // Deliberately no partial total: see GridSelectionReader's remarks. A number here would be
            // read as the answer, and there is no way to show "but only for part of it" convincingly
            // enough in a cell.
            ShowNotice($"Selection is {GridAggregateFormat.Count(read.SelectedCells)} cells; the limit is "
                       + $"{GridAggregateFormat.Count(maxCells)}. Nothing was totalled — a partial total would look like the real one. "
                       + "Select less, or raise \"Maximum cells\" under SQLExtended Settings → Grid Aggregates.");
            SetStatus("Selection too large.");
            return;
        }

        GridAggregateResult result = read.Result;
        foreach (var column in result.Columns)
            _rows.Add(ToRow(column, isTotal: false));
        if (result.Combined != null)
            _rows.Add(ToRow(result.Combined, isTotal: true));

        AggregatesGrid.Items.Refresh();

        bool approximate = false;
        foreach (var column in result.Columns)
            approximate |= column.Approximate;

        if (approximate)
            ShowNotice("Some totals were computed in floating point because a value did not fit an exact decimal "
                       + "(a float column, or a total beyond decimal's range). Their last digits may be off.");
        else
            HideNotice();

        string columnWord = result.Columns.Count == 1 ? "column" : "columns";
        SetStatus($"{GridAggregateFormat.Count(result.TotalCells)} cells selected across {result.Columns.Count} {columnWord}.");
    }

    private static AggregateRow ToRow(GridColumnAggregate column, bool isTotal) => new()
    {
        ColumnName = column.ColumnName,
        TypeText = GridAggregateFormat.Kind(column.Kind),
        Cells = GridAggregateFormat.Count(column.Cells),
        NonNull = GridAggregateFormat.Count(column.NonNull),
        Nulls = GridAggregateFormat.Count(column.Nulls),
        Blanks = GridAggregateFormat.Count(column.Blanks),
        Distinct = GridAggregateFormat.Count(column.DistinctCount),
        Sum = GridAggregateFormat.Sum(column),
        Average = GridAggregateFormat.Average(column),
        Min = column.MinText,
        Max = column.MaxText,
        TotalChars = GridAggregateFormat.Count(column.TotalChars),
        MaxChars = GridAggregateFormat.Count(column.MaxChars),
        IsTotal = isTotal
    };

    private void SetStatus(string text) => StatusText.Text = text;

    private void ShowNotice(string text)
    {
        NoticeText.Text = text;
        NoticeBorder.Visibility = Visibility.Visible;
    }

    private void HideNotice() => NoticeBorder.Visibility = Visibility.Collapsed;

    private void Refresh_Click(object sender, RoutedEventArgs e) => GridAggregatesWatcher.RefreshNow();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var text = new StringBuilder();
        var columns = VisibleColumns();

        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0) text.Append('\t');
            text.Append(columns[i].Header);
        }
        text.AppendLine();

        foreach (var row in _rows)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0) text.Append('\t');
                text.Append(columns[i].Value(row));
            }
            text.AppendLine();
        }

        try { Clipboard.SetText(text.ToString()); SetStatus("Copied to the clipboard."); }
        catch (Exception ex) { SetStatus($"Copy failed: {ex.Message}"); }
    }

    /// <summary>The columns currently on screen, so Copy produces what is being looked at rather than
    /// every aggregate including the ones deliberately switched off.</summary>
    private List<(string Header, Func<AggregateRow, string> Value)> VisibleColumns()
    {
        var columns = new List<(string, Func<AggregateRow, string>)>
        {
            ("Column", r => r.ColumnName),
            ("Type", r => r.TypeText),
            ("Cells", r => r.Cells)
        };

        if (ShowNonNull.IsChecked == true) columns.Add(("Non-null", r => r.NonNull));
        if (ShowNulls.IsChecked == true) columns.Add(("Nulls", r => r.Nulls));
        if (ShowBlanks.IsChecked == true) columns.Add(("Blank", r => r.Blanks));
        if (ShowDistinct.IsChecked == true) columns.Add(("Distinct", r => r.Distinct));
        if (ShowSum.IsChecked == true) columns.Add(("Sum", r => r.Sum));
        if (ShowAverage.IsChecked == true) columns.Add(("Average", r => r.Average));
        if (ShowMin.IsChecked == true) columns.Add(("Min", r => r.Min));
        if (ShowMax.IsChecked == true) columns.Add(("Max", r => r.Max));
        if (ShowChars.IsChecked == true)
        {
            columns.Add(("Chars", r => r.TotalChars));
            columns.Add(("Longest", r => r.MaxChars));
        }

        return columns.ConvertAll(c => (c.Item1, c.Item2));
    }

    /// <summary>
    /// Applies the checkboxes to the grid's columns.
    ///
    /// A <see cref="DataGridColumn"/> is not in the visual tree, so binding its Visibility to a checkbox
    /// by ElementName silently does nothing — hence the explicit wiring here.
    /// </summary>
    private void LoadColumnChoices()
    {
        var settings = SQLExtendedSettings.Current;
        _loadingSettings = true;
        ShowNonNull.IsChecked = settings.GridAggregatesShowNonNull;
        ShowNulls.IsChecked = settings.GridAggregatesShowNulls;
        ShowBlanks.IsChecked = settings.GridAggregatesShowBlanks;
        ShowDistinct.IsChecked = settings.GridAggregatesShowDistinct;
        ShowSum.IsChecked = settings.GridAggregatesShowSum;
        ShowAverage.IsChecked = settings.GridAggregatesShowAverage;
        ShowMin.IsChecked = settings.GridAggregatesShowMin;
        ShowMax.IsChecked = settings.GridAggregatesShowMax;
        ShowChars.IsChecked = settings.GridAggregatesShowChars;
        _loadingSettings = false;

        foreach (var box in new[] { ShowNonNull, ShowNulls, ShowBlanks, ShowDistinct, ShowSum, ShowAverage, ShowMin, ShowMax, ShowChars })
        {
            box.Checked += OnColumnChoiceChanged;
            box.Unchecked += OnColumnChoiceChanged;
        }

        ApplyColumnChoices();
    }

    private void OnColumnChoiceChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
            return;
        ApplyColumnChoices();
        SaveColumnChoices();
    }

    private void ApplyColumnChoices()
    {
        NonNullColumn.Visibility = Show(ShowNonNull);
        NullsColumn.Visibility = Show(ShowNulls);
        BlanksColumn.Visibility = Show(ShowBlanks);
        DistinctColumn.Visibility = Show(ShowDistinct);
        SumColumn.Visibility = Show(ShowSum);
        AverageColumn.Visibility = Show(ShowAverage);
        MinColumn.Visibility = Show(ShowMin);
        MaxColumn.Visibility = Show(ShowMax);
        CharsColumn.Visibility = Show(ShowChars);
        MaxCharsColumn.Visibility = Show(ShowChars);
    }

    private static Visibility Show(CheckBox box) => box.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void SaveColumnChoices()
    {
        try
        {
            var settings = SQLExtendedSettings.Current;
            settings.GridAggregatesShowNonNull = ShowNonNull.IsChecked == true;
            settings.GridAggregatesShowNulls = ShowNulls.IsChecked == true;
            settings.GridAggregatesShowBlanks = ShowBlanks.IsChecked == true;
            settings.GridAggregatesShowDistinct = ShowDistinct.IsChecked == true;
            settings.GridAggregatesShowSum = ShowSum.IsChecked == true;
            settings.GridAggregatesShowAverage = ShowAverage.IsChecked == true;
            settings.GridAggregatesShowMin = ShowMin.IsChecked == true;
            settings.GridAggregatesShowMax = ShowMax.IsChecked == true;
            settings.GridAggregatesShowChars = ShowChars.IsChecked == true;
            settings.Save();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SQLExtended] Aggregates settings save failed: {ex}");
        }
    }
}
