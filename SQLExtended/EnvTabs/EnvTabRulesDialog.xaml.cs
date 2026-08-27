using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQLExtended.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;

namespace SQLExtended.EnvTabs;

/// <summary>
/// A rule as the grid edits it. The grid binds to this rather than to <see cref="EnvTabRule"/> directly so
/// the colour swatch and name can be projected off the index, and so Cancel really cancels — nothing here
/// touches the saved settings until Save copies it back.
/// </summary>
public sealed class EnvTabRuleRow : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _label = "";
    private string _serverPattern = "";
    private string _databasePattern = "";
    private EnvTabMatchMode _matchMode = EnvTabMatchMode.Wildcard;
    private int _colorIndex = EnvTabPalette.NoColor;

    public EnvTabRuleRow() { }

    public EnvTabRuleRow(EnvTabRule rule)
    {
        _enabled = rule.Enabled;
        _label = rule.Label ?? "";
        _serverPattern = rule.ServerPattern ?? "";
        _databasePattern = rule.DatabasePattern ?? "";
        _matchMode = rule.MatchMode;
        _colorIndex = EnvTabPalette.Sanitize(rule.ColorIndex);
        AutoCreated = rule.AutoCreated;
    }

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string Label { get => _label; set => Set(ref _label, value); }
    public string ServerPattern { get => _serverPattern; set => Set(ref _serverPattern, value); }
    public string DatabasePattern { get => _databasePattern; set => Set(ref _databasePattern, value); }
    public EnvTabMatchMode MatchMode { get => _matchMode; set => Set(ref _matchMode, value); }
    public bool AutoCreated { get; set; }

    public int ColorIndex
    {
        get => _colorIndex;
        set
        {
            if (!Set(ref _colorIndex, value)) return;
            OnPropertyChanged(nameof(ColorHex));
            OnPropertyChanged(nameof(ColorName));
        }
    }

    /// <summary>Swatch fill. Null for "no colour", which leaves the border empty rather than black.</summary>
    public string ColorHex => EnvTabPalette.HexOf(_colorIndex) ?? "Transparent";

    public string ColorName => EnvTabPalette.NameOf(_colorIndex);

    public EnvTabRule ToRule() => new()
    {
        Enabled = Enabled,
        Label = Label?.Trim() ?? "",
        ServerPattern = ServerPattern?.Trim() ?? "",
        DatabasePattern = DatabasePattern?.Trim() ?? "",
        MatchMode = MatchMode,
        ColorIndex = EnvTabPalette.Sanitize(ColorIndex),
        AutoCreated = AutoCreated,
    };

    public event PropertyChangedEventHandler PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// The rule editor. Also the place the subsystem's diagnostics surface — everything in EnvTabs fails soft,
/// so without somewhere to read the notes "no colours appeared" and "no rules matched" look identical.
/// </summary>
public sealed partial class EnvTabRulesDialog : Window
{
    private readonly ObservableCollection<EnvTabRuleRow> _rows = new();

    public EnvTabRulesDialog()
    {
        InitializeComponent();

        ModeColumn.ItemsSource = Enum.GetValues(typeof(EnvTabMatchMode));
        RulesGrid.ItemsSource = _rows;

        Load();
        ShowDiagnostics();
    }

    private void Load()
    {
        var settings = SQLExtendedSettings.Current;

        EnabledCheck.IsChecked = settings.EnvTabsEnabled;
        ColorCheck.IsChecked = settings.EnvTabsColorTabs;
        RenameCheck.IsChecked = settings.EnvTabsRenameTabs;
        PromptCheck.IsChecked = settings.EnvTabsAutoPrompt;
        TemplateBox.Text = settings.EnvTabsCaptionTemplate ?? TabCaptionFormatter.DefaultTemplate;
        GroupingCombo.SelectedIndex = settings.EnvTabsGrouping == EnvTabGrouping.ServerAndDatabase ? 1 : 0;

        _rows.Clear();
        foreach (var rule in settings.EnvTabsRules ?? new List<EnvTabRule>())
            _rows.Add(new EnvTabRuleRow(rule));
    }

    private void ShowDiagnostics()
    {
        var notes = EnvTabsDiagnostics.Recent();
        DiagnosticsList.ItemsSource = notes;

        bool colouringOn = ThreadHelper.CheckAccess() && FileColorServiceProxy.IsRegexTabColoringOn();

        StatusText.Text = colouringOn
            ? "The shell is colouring document tabs from the regex provider, which is what this feature drives."
            : "The shell is not currently colouring document tabs from the regex provider. Saving with the feature enabled turns it on; if it stays off, set Tools > Options > Environment > Tabs and Windows > Colorize document tabs to \"by regex\" by hand.";

        if (notes.Count == 0)
            StatusText.Text += "  No problems recorded this session.";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var row = new EnvTabRuleRow { Label = "New rule", ServerPattern = "*", ColorIndex = NextFreeColor() };
        _rows.Add(row);
        RulesGrid.SelectedItem = row;
        RulesGrid.ScrollIntoView(row);
    }

    private int NextFreeColor()
    {
        var used = new HashSet<int>(_rows.Select(r => r.ColorIndex));
        for (int i = 0; i < EnvTabPalette.Count; i++)
            if (!used.Contains(i)) return i;
        return 0;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is EnvTabRuleRow row) _rows.Remove(row);
    }

    private void Colour_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not EnvTabRuleRow row) return;

        // Cycles through the palette and then "no colour". A modal picker for a 16-item list that is
        // already shown as a swatch in the grid would be more clicks for less feedback.
        row.ColorIndex = row.ColorIndex >= EnvTabPalette.Count - 1 ? EnvTabPalette.NoColor : row.ColorIndex + 1;
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        if (RulesGrid.SelectedItem is not EnvTabRuleRow row) return;

        int from = _rows.IndexOf(row);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= _rows.Count) return;

        _rows.Move(from, to);
        RulesGrid.SelectedItem = row;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Commit any cell still in edit mode, or the row the user is typing in is silently discarded.
        RulesGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);

        var settings = SQLExtendedSettings.Current;

        settings.EnvTabsEnabled = EnabledCheck.IsChecked == true;
        settings.EnvTabsColorTabs = ColorCheck.IsChecked == true;
        settings.EnvTabsRenameTabs = RenameCheck.IsChecked == true;
        settings.EnvTabsAutoPrompt = PromptCheck.IsChecked == true;
        settings.EnvTabsCaptionTemplate = string.IsNullOrWhiteSpace(TemplateBox.Text) ? TabCaptionFormatter.DefaultTemplate : TemplateBox.Text.Trim();
        settings.EnvTabsGrouping = GroupingCombo.SelectedIndex == 1 ? EnvTabGrouping.ServerAndDatabase : EnvTabGrouping.Server;
        settings.EnvTabsRules = _rows.Select(r => r.ToRule()).ToList();

        settings.Save();
        EnvTabsService.Instance?.RulesChanged();

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Owns the dialog to the shell — see the note on <see cref="NewEnvTabRuleDialog"/>.</summary>
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
        catch
        {
            // An unowned dialog is still usable; it just may not stay in front.
        }
    }
}
