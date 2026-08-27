using EnvDTE;
using EnvDTE80;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.VisualStudio.Shell;
using SQLExtended.History.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;

namespace SQLExtended.History;

public partial class SqlHistoryControl : UserControl
{
    private System.Windows.Threading.DispatcherTimer _debounceTimer;
    private List<HistoryItemViewModel> _items = new();

    public SqlHistoryControl()
    {
        InitializeComponent();
        InitializeSyntaxHighlighting();

        _debounceTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _debounceTimer.Tick += (s, e) => { _debounceTimer.Stop(); ReloadResults(); };

        Loaded += (s, e) =>
        {
            SearchTextBox.Focus();
            ReloadResults();
        };

        HistoryService.Instance.SnapshotAdded += OnSnapshotAdded;
    }

    private void OnSnapshotAdded(object sender, HistorySnapshot e)
    {
        // Auto-refresh the list when new snapshots arrive.
        Dispatcher.BeginInvoke(new Action(ReloadResults));
    }

    private void InitializeSyntaxHighlighting()
    {
        try
        {
            // Reuse the syntax file already embedded by the Search feature.
            var assembly = typeof(SqlHistoryControl).Assembly;
            using var stream = assembly.GetManifestResourceStream("SQLExtended.Search.TsqlDarkHighlighting.xshd");
            if (stream == null) return;
            using var reader = new XmlTextReader(stream);
            var highlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            PreviewEditor.SyntaxHighlighting = highlighting;
        }
        catch { }
    }

    // --- Loading + filtering ---

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _debounceTimer.Stop();
            ReloadResults();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            SearchTextBox.Clear();
            ReloadResults();
            e.Handled = true;
        }
    }

    private void DateFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Fires once during XAML parse (IsSelected="True" on a ComboBoxItem) before
        // other named controls exist. The Loaded handler will do the initial reload.
        if (!IsLoaded) return;
        ReloadResults();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => ReloadResults();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Clear();
        ReloadResults();
        SearchTextBox.Focus();
    }

    private DateTime? GetDateFilterCutoff()
    {
        int idx = DateFilterCombo.SelectedIndex;
        return idx switch
        {
            0 => DateTime.UtcNow.Date,
            1 => DateTime.UtcNow.AddDays(-7),
            2 => DateTime.UtcNow.AddDays(-30),
            _ => (DateTime?)null
        };
    }

    private void ReloadResults()
    {
        try
        {
            string term = SearchTextBox.Text?.Trim();
            var since = GetDateFilterCutoff();
            var snaps = HistoryService.Instance.Query(term, since, 500);
            _items = snaps.Select(s => new HistoryItemViewModel(s)).ToList();
            ResultsList.ItemsSource = _items;
            CountText.Text = $"{_items.Count} snapshot(s)";
            StatusText.Text = HistoryService.Instance.IsInitialized
                ? $"DB: {HistoryService.Instance.DatabasePath}"
                : "History disabled or not initialized.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Load error: {ex.Message}";
        }
    }

    private HistoryItemViewModel SelectedItem => ResultsList.SelectedItem as HistoryItemViewModel;

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = SelectedItem;
        if (vm == null) { PreviewEditor.Text = ""; PreviewHeader.Text = "Select a snapshot to view"; return; }

        try
        {
            var full = HistoryService.Instance.GetById(vm.Id);
            PreviewEditor.Text = full?.Text ?? "";
            PreviewHeader.Text = $"{vm.DocumentTitle} — {vm.FullTimeDisplay}";
        }
        catch (Exception ex)
        {
            PreviewEditor.Text = $"-- Error: {ex.Message}";
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OpenInNewWindow_Click(sender, null);
    }

    // --- Actions ---

    private void OpenInNewWindow_Click(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var vm = SelectedItem;
        if (vm == null) return;

        try
        {
            var full = HistoryService.Instance.GetById(vm.Id);
            if (full == null) return;

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                OpenTextInNewQueryWindow(full.Text);
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Open error: {ex.Message}";
        }
    }

    private static void OpenTextInNewQueryWindow(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
        if (dte == null) return;

        // Write the snapshot to a temp .sql file and open it — this makes SSMS treat it as a SQL
        // query window (with T-SQL syntax highlighting and execute support) instead of a plain text doc.
        string tempPath = null;
        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SQLExtended", "History");
            System.IO.Directory.CreateDirectory(dir);
            tempPath = System.IO.Path.Combine(dir, $"History_{DateTime.Now:yyyyMMdd_HHmmss_fff}.sql");
            System.IO.File.WriteAllText(tempPath, text ?? "", new System.Text.UTF8Encoding(false));

            dte.ItemOperations.OpenFile(tempPath, EnvDTE.Constants.vsViewKindTextView);
        }
        catch
        {
            try { System.Windows.Clipboard.SetText(text); } catch { }
        }
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedItem;
        if (vm == null) return;
        try
        {
            var full = HistoryService.Instance.GetById(vm.Id);
            if (full != null) System.Windows.Clipboard.SetText(full.Text ?? "");
            StatusText.Text = "Copied to clipboard.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Copy error: {ex.Message}";
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var vm = SelectedItem;
        if (vm == null) return;
        try
        {
            HistoryService.Instance.DeleteById(vm.Id);
            ReloadResults();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Delete error: {ex.Message}";
        }
    }
}

/// <summary>Row VM for the history list. Holds enough to render without re-querying.</summary>
public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(HistorySnapshot snap)
    {
        Id = snap.Id;
        DocumentTitle = snap.DocumentTitle;
        var local = snap.CapturedAtUtc.ToLocalTime();
        TimeDisplay = local.ToString(IsToday(local) ? "HH:mm:ss" : "MMM d HH:mm");
        FullTimeDisplay = local.ToString("yyyy-MM-dd HH:mm:ss");
        LengthDisplay = FormatLength(snap.TextLength);
        PreviewLine = snap.Preview;
    }

    public long Id { get; }
    public string DocumentTitle { get; }
    public string TimeDisplay { get; }
    public string FullTimeDisplay { get; }
    public string LengthDisplay { get; }
    public string PreviewLine { get; }

    private static bool IsToday(DateTime local) => local.Date == DateTime.Now.Date;

    private static string FormatLength(int chars)
    {
        if (chars < 1024) return $"{chars} ch";
        return $"{chars / 1024.0:0.#} KB";
    }
}
