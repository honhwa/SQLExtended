using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SQLExtended.ResultsGrid.Columns;

/// <summary>
/// The Go to Column picker: a box that filters the results grid's column names as you type, and a list to pick from.
/// A transient popup rather than a tool window — it is opened, used once and gone, the way Ctrl+, / Go To All are in
/// Visual Studio — so it closes on Enter, on Escape, and on losing activation.
///
/// <para><b>Shown with <c>ShowDialog</c>, never <c>Show</c>.</b> Shown modelessly from a command it is never activated:
/// the shell hands the focus back to the editor as the command returns, so every key typed into the "box" went into the
/// query window instead and the list could not be used. A modal window owns the keyboard until it closes. The choice is
/// handed back through <see cref="Selected"/> and acted on by the caller once the dialog has gone.</para>
///
/// <para>The keyboard stays in the box throughout: Up/Down/PageUp/PageDown move the list's selection from there, so
/// typing, narrowing and picking never needs a Tab into the list.</para>
/// </summary>
public partial class GoToColumnWindow : Window
{
    private readonly IReadOnlyList<GridColumnEntry> _all;
    private bool _closing;

    internal GoToColumnWindow(IReadOnlyList<GridColumnEntry> entries)
    {
        InitializeComponent();
        _all = entries;
        Apply("");
        Loaded += (s, e) => { Activate(); QueryBox.Focus(); Keyboard.Focus(QueryBox); };
    }

    /// <summary>The column picked, once the dialog has returned true.</summary>
    internal GridColumnEntry Selected { get; private set; }

    /// <summary>
    /// Owned by the SSMS main window, as the extension's other dialogs are: an unowned window can open behind the
    /// shell, and this one closes on deactivation, so opening behind it would close it before it was seen.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            if (!ThreadHelper.CheckAccess()) return;
            if (Package.GetGlobalService(typeof(SVsUIShell)) is not IVsUIShell shell) return;
            if (shell.GetDialogOwnerHwnd(out IntPtr ownerHwnd) != 0 || ownerHwnd == IntPtr.Zero) return;

            new WindowInteropHelper(this).Owner = ownerHwnd;
        }
        catch { /* an unowned window is still usable */ }
    }

    private void Apply(string query)
    {
        var matches = GridColumnMatcher.Filter(_all, query);
        ColumnList.ItemsSource = matches;
        if (matches.Count > 0)
        {
            ColumnList.SelectedIndex = 0;
            ColumnList.ScrollIntoView(matches[0]);
        }

        int grids = _all.Select(e => e.GridOrder).Distinct().Count();
        string scope = grids > 1 ? $" across {grids} result sets" : "";
        StatusText.Text = string.IsNullOrWhiteSpace(query)
            ? $"{_all.Count:N0} column{(_all.Count == 1 ? "" : "s")}{scope}"
            : matches.Count == 0
                ? "No column matches"
                : $"{matches.Count:N0} of {_all.Count:N0} column{(_all.Count == 1 ? "" : "s")}{scope}";
    }

    private void QueryBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Apply(QueryBox.Text);

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Move(1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.PageDown: Move(10); e.Handled = true; break;
            case Key.PageUp: Move(-10); e.Handled = true; break;
            case Key.Enter: Commit(ColumnList.SelectedItem as GridColumnEntry); e.Handled = true; break;
            case Key.Escape: CloseOnce(); e.Handled = true; break;
        }
    }

    // Reached only if the list itself was clicked into; the same keys mean the same things there.
    private void ColumnList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Commit(ColumnList.SelectedItem as GridColumnEntry); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseOnce(); e.Handled = true; }
    }

    private void ColumnList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Commit(ColumnList.SelectedItem as GridColumnEntry);

    private void Move(int delta)
    {
        int count = ColumnList.Items.Count;
        if (count == 0)
            return;
        int index = Math.Max(0, Math.Min(count - 1, ColumnList.SelectedIndex + delta));
        ColumnList.SelectedIndex = index;
        ColumnList.ScrollIntoView(ColumnList.SelectedItem);
    }

    private void Commit(GridColumnEntry entry)
    {
        if (entry == null || _closing)
            return;
        Selected = entry;
        _closing = true;
        DialogResult = true;   // closes the dialog
    }

    private void Window_Deactivated(object sender, EventArgs e) => CloseOnce();

    // Closing raises Deactivated, which would close again a window that is already closing.
    private void CloseOnce()
    {
        if (_closing)
            return;
        _closing = true;
        Close();
    }
}
