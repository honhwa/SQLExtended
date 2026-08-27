using System.Linq;
using System.Windows;

namespace SQLExtended.Validation;

/// <summary>
/// Small management dialog for the <see cref="ValidationIgnoreList"/>: lists ignored databases and
/// objects and lets the user remove individual entries or clear them all. Mutates and saves the
/// list passed in; <see cref="Changed"/> reports whether anything was removed.
/// </summary>
public partial class ValidationIgnoreDialog : Window
{
    private readonly ValidationIgnoreList _ignores;

    /// <summary>True when at least one entry was removed (so the caller can re-filter results).</summary>
    public bool Changed { get; private set; }

    internal ValidationIgnoreDialog(ValidationIgnoreList ignores)
    {
        InitializeComponent();
        _ignores = ignores;
        Populate();
    }

    private void Populate()
    {
        EntryList.Items.Clear();
        foreach (string db in _ignores.Databases.OrderBy(d => d, System.StringComparer.OrdinalIgnoreCase))
            EntryList.Items.Add($"Database: {db}");
        foreach (string obj in _ignores.Objects.OrderBy(o => o, System.StringComparer.OrdinalIgnoreCase))
            EntryList.Items.Add($"Object: {obj}");

        EmptyLabel.Visibility = _ignores.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (EntryList.SelectedItem is not string display) return;

        // Strip the "Database: " / "Object: " prefix to get the stored value.
        int colon = display.IndexOf(':');
        string value = colon >= 0 ? display.Substring(colon + 1).Trim() : display;

        _ignores.Remove(value);
        _ignores.Save();
        Changed = true;
        Populate();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (_ignores.Count == 0) return;
        _ignores.Clear();
        _ignores.Save();
        Changed = true;
        Populate();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
