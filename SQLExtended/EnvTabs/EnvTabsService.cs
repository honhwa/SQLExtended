using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.Shell;
using SQLExtended.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SQLExtended.EnvTabs;

/// <summary>
/// Drives the whole feature: works out which connection each open tab belongs to, matches it to a rule,
/// and applies the rule's colour and caption.
///
/// <b>Why a poll rather than events.</b> The obvious design is to hook the running document table and
/// react to documents opening. That gets you the tab but not the thing that matters — its connection.
/// <see cref="ConnectionHelper"/> can only report the connection of the <i>active</i> query window (SSMS
/// exposes no per-document connection we can read), and a tab's connection changes without any document
/// event at all: the user picks a different database from the toolbar dropdown, or reconnects the window
/// to another server. Both are invisible to the RDT. So the connection is sampled from whichever tab is
/// active, each tick, and remembered per document path.
///
/// <b>Connections are sticky per path.</b> Once a tab's server and database are known they are kept until
/// the tab closes or is seen connected elsewhere. A tab that is not active cannot be re-read, and dropping
/// to "unknown" would strip the colour off every background tab — which is precisely when the colour is
/// doing its job.
/// </summary>
internal sealed class EnvTabsService : IDisposable
{
    private static EnvTabsService _instance;
    public static EnvTabsService Instance => _instance;

    private readonly IServiceProvider _serviceProvider;
    private Timer _timer;
    private bool _running;

    /// <summary>Document path → the connection it was last seen using.</summary>
    private readonly Dictionary<string, (string Server, string Database)> _connectionByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Document path → its number within its group, kept stable while the tab stays open.</summary>
    private readonly Dictionary<string, int> _sequenceByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Connections already offered to the user this session, so the prompt appears once.</summary>
    private readonly HashSet<string> _promptedThisSession = new(StringComparer.OrdinalIgnoreCase);

    private string _configPath;
    private bool _configPathProbed;
    private bool _shellPreferenceSet;
    private bool _blockWritten;
    private bool _promptOpen;

    /// <summary>
    /// How many more ticks to keep re-pinning colours. The shell reloads the config file on its own
    /// schedule through a file watcher, so a colour pinned in the same tick the pattern was written finds
    /// no group yet and is silently dropped. Retrying for a few ticks after any change is what makes this
    /// self-healing without knowing when the reload lands.
    /// </summary>
    private int _pinTicksRemaining;

    private EnvTabsService(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public static void Start(IServiceProvider serviceProvider)
    {
        _instance ??= new EnvTabsService(serviceProvider);
        _instance.Restart();
    }

    /// <summary>(Re)starts the poll. Called at startup and whenever the settings dialog saves.</summary>
    public void Restart()
    {
        _timer?.Dispose();
        _timer = null;

        var settings = SQLExtendedSettings.Current;
        if (!settings.EnvTabsEnabled)
        {
            TurnOff();
            return;
        }

        _pinTicksRemaining = PinRetryTicks;
        int seconds = Math.Max(1, settings.EnvTabsPollSeconds);
        _timer = new Timer(OnTick, null, TimeSpan.FromSeconds(Math.Min(3, seconds)), TimeSpan.FromSeconds(seconds));
    }

    private const int PinRetryTicks = 4;

    /// <summary>
    /// Called when the rules change. Forces a config rewrite and restarts colour pinning, so an edit in
    /// the rules dialog shows up immediately rather than at the next natural change.
    /// </summary>
    public void RulesChanged()
    {
        EnvTabRule.ClearCache();
        _sequenceByPath.Clear();
        _pinTicksRemaining = PinRetryTicks;
        Restart();
    }

    private void TurnOff()
    {
        if (!_blockWritten && !_shellPreferenceSet) return;

        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Take our patterns back out of the shell's file and restore the captions we changed, so
            // switching the feature off actually undoes it rather than freezing the last state.
            string path = ResolveConfigPath();
            if (path != null) ColorByRegexConfigStore.RemoveManagedBlock(path);
            RestoreAllCaptions();

            if (_shellPreferenceSet)
            {
                FileColorServiceProxy.EnableRegexTabColoring(false);
                _shellPreferenceSet = false;
            }
        });

        _blockWritten = false;
    }

    private void OnTick(object state)
    {
        if (_running) return;
        _running = true;

        try
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await RefreshAsync();
            });
        }
        catch (Exception ex)
        {
            EnvTabsDiagnostics.Note("Tab refresh failed: " + ex.Message);
        }
        finally
        {
            _running = false;
        }
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        // Everything below reads window frames and SSMS connection state, both of which are main-thread
        // only. The caller has already switched, but an async method must not merely assert it.
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var settings = SQLExtendedSettings.Current;
        if (!settings.EnvTabsEnabled) return;

        if (settings.EnvTabsColorTabs && !_shellPreferenceSet)
            _shellPreferenceSet = FileColorServiceProxy.EnableRegexTabColoring(true);

        var tabs = DocumentTabs.Enumerate(_serviceProvider);
        if (tabs.Count == 0) return;

        SampleActiveConnection(tabs);
        PruneClosedTabs(tabs);

        var ruleSet = new EnvTabRuleSet { Rules = settings.EnvTabsRules ?? new List<EnvTabRule>() };
        var groups = BuildGroups(tabs, ruleSet);

        if (settings.EnvTabsColorTabs) await ApplyColorsAsync(groups, tabs);
        if (settings.EnvTabsRenameTabs) ApplyCaptions(tabs, ruleSet, settings);

        MaybePrompt(tabs, ruleSet, settings);
    }

    /// <summary>
    /// Reads the connection of the active tab and files it against that tab's path. This is the only point
    /// at which any connection information enters the subsystem.
    /// </summary>
    private void SampleActiveConnection(List<DocumentTab> tabs)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var active = tabs.FirstOrDefault(t => t.IsActive);
        if (active == null) return;

        try
        {
            string connectionString = ConnectionHelper.GetActiveConnectionString();
            if (string.IsNullOrEmpty(connectionString)) return;

            var builder = new SqlConnectionStringBuilder(connectionString);
            string server = ConnectionHelper.NormalizeHarvestedDataSource(builder.DataSource);
            string database = builder.InitialCatalog;

            if (string.IsNullOrWhiteSpace(server)) return;

            if (_connectionByPath.TryGetValue(active.Path, out var previous) &&
                (!string.Equals(previous.Server, server, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(previous.Database, database, StringComparison.OrdinalIgnoreCase)))
            {
                // The window was pointed somewhere else. Its number within the old group means nothing now.
                _sequenceByPath.Remove(active.Path);
            }

            _connectionByPath[active.Path] = (server, database);
        }
        catch (Exception ex)
        {
            EnvTabsDiagnostics.Note("Could not read the active connection: " + ex.Message);
        }
    }

    private void PruneClosedTabs(List<DocumentTab> tabs)
    {
        var open = new HashSet<string>(tabs.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);

        foreach (var stale in _connectionByPath.Keys.Where(p => !open.Contains(p)).ToList())
            _connectionByPath.Remove(stale);

        foreach (var stale in _sequenceByPath.Keys.Where(p => !open.Contains(p)).ToList())
            _sequenceByPath.Remove(stale);
    }

    /// <summary>
    /// Groups the open tabs by the rule they match, in rule order, and rewrites the config file if the
    /// result differs from what is already on disk.
    /// </summary>
    private List<EnvTabGroup> BuildGroups(List<DocumentTab> tabs, EnvTabRuleSet ruleSet)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var byRule = new Dictionary<string, EnvTabGroup>(StringComparer.Ordinal);
        var ordered = new List<EnvTabGroup>();

        foreach (var tab in tabs)
        {
            if (!_connectionByPath.TryGetValue(tab.Path, out var connection)) continue;

            var rule = ruleSet.Match(connection.Server, connection.Database);
            if (rule == null) continue;

            if (!byRule.TryGetValue(rule.Key, out var group))
            {
                group = new EnvTabGroup { RuleKey = rule.Key, Label = rule.Label, ColorIndex = EnvTabPalette.Sanitize(rule.ColorIndex) };
                byRule[rule.Key] = group;
                ordered.Add(group);
            }

            group.Paths.Add(tab.Path);
            AssignSequence(tab.Path, group);
        }

        string configPath = ResolveConfigPath();
        if (configPath != null && ColorByRegexConfigStore.Write(configPath, ordered))
        {
            _blockWritten = true;
            _pinTicksRemaining = PinRetryTicks;
        }

        return ordered;
    }

    /// <summary>
    /// Gives a tab the lowest number not already used within its group, and never changes it afterwards.
    /// Renumbering live tabs when an unrelated one closes would make the numbers useless as a way to refer
    /// to a window.
    /// </summary>
    private void AssignSequence(string path, EnvTabGroup group)
    {
        if (_sequenceByPath.ContainsKey(path)) return;

        var taken = new HashSet<int>(group.Paths.Where(p => _sequenceByPath.ContainsKey(p)).Select(p => _sequenceByPath[p]));
        int next = 1;
        while (taken.Contains(next)) next++;
        _sequenceByPath[path] = next;
    }

    private async System.Threading.Tasks.Task ApplyColorsAsync(List<EnvTabGroup> groups, List<DocumentTab> tabs)
    {
        if (_pinTicksRemaining <= 0) return;
        _pinTicksRemaining--;

        foreach (var group in groups)
        {
            if (group.ColorIndex == EnvTabPalette.NoColor) continue;

            // One pin per group: the shell keys the colour by group id, and every path in the group
            // resolves to the same one.
            var representative = tabs.FirstOrDefault(t => t.Hierarchy != null && group.Paths.Contains(t.Path, StringComparer.OrdinalIgnoreCase));
            if (representative == null) continue;

            await FileColorServiceProxy.SetFileColorAsync(
                _serviceProvider, representative.Path, representative.Hierarchy, representative.ItemId, group.ColorIndex, CancellationToken.None);
        }
    }

    private void ApplyCaptions(List<DocumentTab> tabs, EnvTabRuleSet ruleSet, SQLExtendedSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (var tab in tabs)
        {
            if (!_connectionByPath.TryGetValue(tab.Path, out var connection))
            {
                // Unknown connection: leave whatever is there. Stripping a prefix off a tab we simply
                // haven't sampled yet would make captions flicker on every SSMS start.
                continue;
            }

            var rule = ruleSet.Match(connection.Server, connection.Database);

            string desired = rule == null
                ? TabCaptionFormatter.Strip(tab.Caption)
                : TabCaptionFormatter.Format(
                    settings.EnvTabsCaptionTemplate, rule.Label, connection.Server, connection.Database,
                    _sequenceByPath.TryGetValue(tab.Path, out int n) ? n : 0, tab.Caption);

            if (!string.Equals(desired, tab.Caption, StringComparison.Ordinal))
                DocumentTabs.TrySetCaption(tab.Frame, desired);
        }
    }

    private void RestoreAllCaptions()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        foreach (var tab in DocumentTabs.Enumerate(_serviceProvider))
        {
            if (!TabCaptionFormatter.HasPrefix(tab.Caption)) continue;
            DocumentTabs.TrySetCaption(tab.Frame, TabCaptionFormatter.Strip(tab.Caption));
        }
    }

    /// <summary>
    /// Offers to create a rule for the active connection when nothing covers it.
    ///
    /// Only the <i>active</i> tab is ever offered, and only once per connection per session. A prompt that
    /// fired for every unmapped background tab would open a stack of dialogs on startup — and this dialog
    /// interrupts, so it has to be rare enough to be welcome.
    /// </summary>
    private void MaybePrompt(List<DocumentTab> tabs, EnvTabRuleSet ruleSet, SQLExtendedSettings settings)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!settings.EnvTabsAutoPrompt || _promptOpen) return;

        var active = tabs.FirstOrDefault(t => t.IsActive);
        if (active == null || !_connectionByPath.TryGetValue(active.Path, out var connection)) return;
        if (!ruleSet.IsUnmapped(connection.Server, connection.Database)) return;

        bool byDatabase = settings.EnvTabsGrouping == EnvTabGrouping.ServerAndDatabase;
        string key = byDatabase ? $"{connection.Server}|{connection.Database}" : connection.Server;

        if (!_promptedThisSession.Add(key)) return;
        if (settings.EnvTabsDeclined?.Contains(key, StringComparer.OrdinalIgnoreCase) == true) return;

        _promptOpen = true;
        try
        {
            var proposed = EnvTabRuleSet.ProposeRule(connection.Server, connection.Database, settings.EnvTabsGrouping, ruleSet.NextFreeColor());
            var result = NewEnvTabRuleDialog.Prompt(proposed, connection.Server, connection.Database);

            switch (result.Outcome)
            {
                case NewRuleOutcome.Create:
                    ruleSet.AddFromPrompt(result.Rule);
                    settings.EnvTabsRules = ruleSet.Rules;
                    settings.Save();
                    RulesChanged();
                    break;

                case NewRuleOutcome.Never:
                    settings.EnvTabsDeclined ??= new List<string>();
                    settings.EnvTabsDeclined.Add(key);
                    settings.Save();
                    break;

                case NewRuleOutcome.NotNow:
                    // Already recorded in _promptedThisSession, so it won't reappear until next session.
                    break;
            }
        }
        catch (Exception ex)
        {
            EnvTabsDiagnostics.Note("The new-rule prompt failed: " + ex.Message);
        }
        finally
        {
            _promptOpen = false;
        }
    }

    private string ResolveConfigPath()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_configPathProbed) return _configPath;
        _configPathProbed = true;
        _configPath = ColorByRegexConfigStore.ResolvePath(_serviceProvider);

        if (_configPath == null)
            EnvTabsDiagnostics.Note("The shell would not report its working folder, so tab colours cannot be written. Captions still work.");

        return _configPath;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
