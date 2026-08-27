using System;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SQLExtended.EnvTabs;

/// <summary>What the user chose when offered a rule for an unmapped connection.</summary>
public enum NewRuleOutcome
{
    Create,
    NotNow,
    Never,
}

public readonly struct NewRuleResult
{
    public NewRuleResult(NewRuleOutcome outcome, EnvTabRule rule)
    {
        Outcome = outcome;
        Rule = rule;
    }

    public NewRuleOutcome Outcome { get; }
    public EnvTabRule Rule { get; }
}

/// <summary>
/// The on-connect prompt: "you've connected somewhere with no rule — want one?".
///
/// Three answers, not two. "Not now" is per session and "Never for this" is remembered across restarts,
/// because a single Cancel cannot express the difference between "I'm busy" and "this server should never
/// be coloured" — and getting that wrong means either a dialog every session forever, or silently never
/// offering again on a server the user did want mapped.
/// </summary>
public sealed partial class NewEnvTabRuleDialog : Window
{
    private sealed class ColorChoice
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Hex { get; set; }
    }

    private NewRuleOutcome _outcome = NewRuleOutcome.NotNow;

    private NewEnvTabRuleDialog(EnvTabRule proposed, string server, string database)
    {
        InitializeComponent();

        HeadingText.Text = "No tab colour rule for this connection";
        SubheadingText.Text = string.IsNullOrWhiteSpace(database)
            ? $"Connected to {server}."
            : $"Connected to {server}, database {database}.";

        LabelBox.Text = proposed.Label ?? "";
        ServerBox.Text = proposed.ServerPattern ?? "";
        DatabaseBox.Text = proposed.DatabasePattern ?? "";

        ColorCombo.ItemsSource = EnvTabPalette.All().Select(c => new ColorChoice { Index = c.Index, Name = c.Name, Hex = c.Hex }).ToList();
        ColorCombo.SelectedIndex = proposed.ColorIndex >= 0 ? proposed.ColorIndex : 0;

        Loaded += (_, _) => LabelBox.SelectAll();
    }

    /// <summary>
    /// Shows the prompt modally and returns what the user chose. Must be called on the UI thread.
    /// </summary>
    public static NewRuleResult Prompt(EnvTabRule proposed, string server, string database)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dialog = new NewEnvTabRuleDialog(proposed, server, database);
        dialog.ShowDialog();

        return new NewRuleResult(dialog._outcome, dialog._outcome == NewRuleOutcome.Create ? dialog.BuildRule(proposed) : null);
    }

    private EnvTabRule BuildRule(EnvTabRule proposed)
    {
        var rule = proposed.Clone();
        rule.Label = LabelBox.Text?.Trim() ?? "";
        rule.ServerPattern = ServerBox.Text?.Trim() ?? "";
        rule.DatabasePattern = DatabaseBox.Text?.Trim() ?? "";
        rule.ColorIndex = ColorCombo.SelectedItem is ColorChoice choice ? choice.Index : EnvTabPalette.NoColor;
        rule.AutoCreated = true;

        // A name containing * or ? cannot be expressed as a wildcard pattern without matching more than
        // the user was asked about, so such a rule is stored as an escaped regex instead.
        if (EnvTabRuleSet.NeedsRegexMode(rule.ServerPattern) || EnvTabRuleSet.NeedsRegexMode(rule.DatabasePattern))
        {
            rule.MatchMode = EnvTabMatchMode.Regex;
            rule.ServerPattern = System.Text.RegularExpressions.Regex.Escape(rule.ServerPattern);
            rule.DatabasePattern = System.Text.RegularExpressions.Regex.Escape(rule.DatabasePattern);
        }

        return rule;
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerBox.Text))
        {
            MessageBox.Show(this, "A rule needs a server pattern. Use * to match every server.", "Colour this connection?", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _outcome = NewRuleOutcome.Create;
        DialogResult = true;
    }

    private void NotNow_Click(object sender, RoutedEventArgs e)
    {
        _outcome = NewRuleOutcome.NotNow;
        DialogResult = false;
    }

    private void Never_Click(object sender, RoutedEventArgs e)
    {
        _outcome = NewRuleOutcome.Never;
        DialogResult = false;
    }

    /// <summary>
    /// Gives the dialog the shell as its owner. Without one, a modal WPF window shown from the shell can
    /// be placed <i>behind</i> the main window — still modal, so SSMS stops responding with nothing on
    /// screen to explain why. That is reported as a hang, and it is the same fix
    /// <see cref="SchemaDialog"/> carries.
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
        catch
        {
            // An unowned dialog is still usable; it just may not stay in front.
        }
    }
}
