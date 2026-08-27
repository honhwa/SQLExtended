using EnvDTE;
using EnvDTE80;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.VisualStudio.Shell;
using SQLExtended.ScriptLibrary.Models;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Xml;

namespace SQLExtended.ScriptLibrary;

public partial class ScriptLibraryControl : UserControl
{
    private System.Windows.Threading.DispatcherTimer _debounceTimer;

    public ScriptLibraryControl()
    {
        InitializeComponent();
        InitializeSyntaxHighlighting();

        _debounceTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _debounceTimer.Tick += (s, e) => { _debounceTimer.Stop(); Reload(); };

        Loaded += (s, e) =>
        {
            SearchTextBox.Focus();
            Reload();
        };

        ScriptLibraryService.Instance.Changed += OnLibraryChanged;
        UpdateButtonStates(null);
    }

    private void OnLibraryChanged(object sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(Reload));

    private void InitializeSyntaxHighlighting()
    {
        try
        {
            // Reuse the T-SQL highlighting file embedded by the Search feature.
            var assembly = typeof(ScriptLibraryControl).Assembly;
            using var stream = assembly.GetManifestResourceStream("SQLExtended.Search.TsqlDarkHighlighting.xshd");
            if (stream == null) return;
            using var reader = new XmlTextReader(stream);
            PreviewEditor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
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
        if (e.Key == Key.Enter) { _debounceTimer.Stop(); Reload(); e.Handled = true; }
        else if (e.Key == Key.Escape) { SearchTextBox.Clear(); Reload(); e.Handled = true; }
    }

    private void Reload()
    {
        try
        {
            string priorId = SelectedScript?.Id;

            var rows = ScriptLibraryService.Instance.Query(SearchTextBox.Text?.Trim())
                .Select(s => new ScriptRowViewModel(s)).ToList();

            var view = new ListCollectionView(rows);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ScriptRowViewModel.Category)));
            ScriptList.ItemsSource = view;

            // Restore prior selection where possible.
            if (priorId != null)
            {
                var match = rows.FirstOrDefault(r => r.Script.Id == priorId);
                if (match != null) ScriptList.SelectedItem = match;
            }

            StatusText.Text = $"{rows.Count} script(s)";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Load error: {ex.Message}";
        }
    }

    private LibraryScript SelectedScript => (ScriptList.SelectedItem as ScriptRowViewModel)?.Script;

    private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var script = SelectedScript;
        if (script == null)
        {
            PreviewEditor.Text = "";
            PreviewHeader.Text = "Select a script";
            PreviewDescription.Text = "";
            UpdateButtonStates(null);
            return;
        }

        PreviewEditor.Text = script.Body ?? "";
        PreviewHeader.Text = script.IsBuiltIn ? $"{script.Name}  (built-in)" : script.Name;
        PreviewDescription.Text = script.Description ?? "";
        UpdateButtonStates(script);
    }

    private void UpdateButtonStates(LibraryScript script)
    {
        bool has = script != null;
        bool editable = has && !script.IsBuiltIn;
        OpenButton.IsEnabled = has;
        RunButton.IsEnabled = has;
        CopyButton.IsEnabled = has;
        EditButton.IsEnabled = editable;
        DeleteButton.IsEnabled = editable;
    }

    private void ScriptList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Open_Click(sender, null);
    }

    // --- Actions ---

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var script = SelectedScript;
        if (script == null) return;
        RunOnUiThread(() => OpenTextInNewQueryWindow(script.Body, execute: false));
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var script = SelectedScript;
        if (script == null) return;
        RunOnUiThread(() => OpenTextInNewQueryWindow(script.Body, execute: true));
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var script = SelectedScript;
        if (script == null) return;
        try { System.Windows.Clipboard.SetText(script.Body ?? ""); StatusText.Text = "Copied to clipboard."; }
        catch (Exception ex) { StatusText.Text = $"Copy error: {ex.Message}"; }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        var seed = new LibraryScript { Category = (SelectedScript?.Category) ?? "General" };
        var dialog = new ScriptEditDialog(seed) { Owner = System.Windows.Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            ScriptLibraryService.Instance.AddOrUpdateUser(dialog.Result);
            StatusText.Text = $"Saved '{dialog.Result.Name}'.";
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var script = SelectedScript;
        if (script == null || script.IsBuiltIn) return;

        // Edit a copy so a cancelled dialog leaves the stored script untouched.
        var working = Clone(script);
        var dialog = new ScriptEditDialog(working) { Owner = System.Windows.Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            ScriptLibraryService.Instance.AddOrUpdateUser(dialog.Result);
            StatusText.Text = $"Saved '{dialog.Result.Name}'.";
        }
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        var script = SelectedScript;
        if (script == null) return;

        // Create an editable user copy (works for built-in scripts too).
        var copy = Clone(script);
        copy.Id = "";
        copy.IsBuiltIn = false;
        copy.Name = $"{script.Name} (copy)";
        var dialog = new ScriptEditDialog(copy) { Owner = System.Windows.Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            ScriptLibraryService.Instance.AddOrUpdateUser(dialog.Result);
            StatusText.Text = $"Saved '{dialog.Result.Name}'.";
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var script = SelectedScript;
        if (script == null || script.IsBuiltIn) return;

        var result = MessageBox.Show($"Delete user script '{script.Name}'?", "SQLExtended Script Library",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        ScriptLibraryService.Instance.DeleteUser(script.Id);
        StatusText.Text = $"Deleted '{script.Name}'.";
    }

    // --- Editor integration ---

    private void RunOnUiThread(Action action)
    {
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try { action(); }
            catch (Exception ex) { StatusText.Text = $"Error: {ex.Message}"; }
        });
    }

    /// <summary>
    /// Writes the script to a temp .sql file and opens it as a query window (T-SQL highlighting + execute support).
    /// When <paramref name="execute"/> is true, runs the SSMS execute command against the new window.
    /// Mirrors the approach used by the SQL History tool window.
    /// </summary>
    private void OpenTextInNewQueryWindow(string text, bool execute)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
        if (dte == null) { StatusText.Text = "No DTE available."; return; }

        try
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SQLExtended", "ScriptLibrary");
            System.IO.Directory.CreateDirectory(dir);
            var tempPath = System.IO.Path.Combine(dir, $"Script_{DateTime.Now:yyyyMMdd_HHmmss_fff}.sql");
            System.IO.File.WriteAllText(tempPath, text ?? "", new System.Text.UTF8Encoding(false));

            dte.ItemOperations.OpenFile(tempPath, EnvDTE.Constants.vsViewKindTextView);

            if (execute)
            {
                // The freshly opened query window is the active document; F5-equivalent command.
                try { dte.ExecuteCommand("Query.Execute"); StatusText.Text = "Opened and executed."; }
                catch { StatusText.Text = "Opened. (Press F5 to run — execute command unavailable.)"; }
            }
            else
            {
                StatusText.Text = "Opened in new query window.";
            }
        }
        catch (Exception ex)
        {
            try { System.Windows.Clipboard.SetText(text ?? ""); } catch { }
            StatusText.Text = $"Could not open window ({ex.Message}). Copied to clipboard instead.";
        }
    }

    private static LibraryScript Clone(LibraryScript s) => new LibraryScript
    {
        Id = s.Id,
        Name = s.Name,
        Category = s.Category,
        Description = s.Description,
        Body = s.Body,
        IsBuiltIn = s.IsBuiltIn
    };
}

/// <summary>Row VM for the script list. Exposes display-friendly members for binding.</summary>
public sealed class ScriptRowViewModel
{
    public ScriptRowViewModel(LibraryScript script) => Script = script;

    public LibraryScript Script { get; }
    public string Name => Script.Name;
    public string Category => string.IsNullOrWhiteSpace(Script.Category) ? "General" : Script.Category;
    public Visibility BuiltInBadgeVisibility => Script.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;
}
