using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SQLExtended.Monitoring;

/// <summary>
/// Read-only viewer for a statement from one of the dashboard grids, which only have room for a single collapsed
/// line. Select-and-copy works in the editor; Copy (or Ctrl+C with nothing selected) takes the whole text.
/// </summary>
public partial class SqlTextDialog : Window
{
    /// <param name="select">Text within <paramref name="sql"/> to select and scroll to — the running statement when a whole batch is shown.</param>
    public SqlTextDialog(string title, string sql, string select = null)
    {
        InitializeComponent();

        Title = title;
        HeaderText.Text = title;
        Theme.TsqlHighlighting.Attach(SqlEditor);
        SqlEditor.Text = sql ?? "";

        Loaded += (s, e) =>
        {
            SqlEditor.Focus();
            SelectText(select);
        };
    }

    private void SelectText(string select)
    {
        if (string.IsNullOrWhiteSpace(select)) return;

        int idx = SqlEditor.Text.IndexOf(select.Trim(), StringComparison.Ordinal);
        if (idx < 0) return;

        SqlEditor.Select(idx, select.Trim().Length);
        var loc = SqlEditor.Document.GetLocation(idx);
        SqlEditor.ScrollTo(loc.Line, loc.Column);
        StatusText.Text = "The running statement is selected.";
    }

    /// <summary>
    /// Owned by the SSMS main window, as <see cref="SchemaDialog"/> is: <c>Window.GetWindow</c> returns null for a control
    /// hosted in a tool window, and an unowned window can open behind the shell.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            if (Owner != null) return;
            if (!ThreadHelper.CheckAccess()) return;
            if (Package.GetGlobalService(typeof(SVsUIShell)) is not IVsUIShell shell) return;
            if (shell.GetDialogOwnerHwnd(out IntPtr ownerHwnd) != 0 || ownerHwnd == IntPtr.Zero) return;

            new WindowInteropHelper(this).Owner = ownerHwnd;
        }
        catch { /* an unowned window is still usable; it just may not stay in front */ }
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyAll();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && SqlEditor.SelectionLength == 0)
        {
            CopyAll();
            e.Handled = true;
        }
    }

    private void CopyAll()
    {
        try
        {
            Clipboard.SetText(SqlEditor.Text);
            StatusText.Text = "Copied to clipboard.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Copy failed: " + ex.Message;
        }
    }
}
