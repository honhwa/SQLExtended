using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using SQLExtended.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace SQLExtended.Monitoring.AlwaysOn;

/// <summary>
/// The Always On monitor. Pinned to the connection it was opened from, polls the HADR DMVs on a timer, and keeps a
/// short in-memory window of queue sizes so the Databases tab can show trend as well as level.
///
/// <para><b>The connection is captured once, when the window is opened, and kept until it closes</b>; the window
/// does not follow the active query window. Half of what this monitor shows is vantage-point dependent — several
/// state columns are populated only for replicas local to the queried instance — so a window that changed
/// connection would change what the same grids mean. The tool window is registered <c>MultiInstances</c>, which is
/// also how you watch a whole group: one window per replica, side by side. See <see cref="MonitorWindows"/> for the
/// matching and reuse rules, shared with the other three dashboards.</para>
///
/// Threading: collection runs entirely on a background thread and only the merge into the bound collections
/// happens on the UI thread. Polls never overlap — a slow server makes the interval effectively longer rather
/// than queueing work up behind itself.
/// </summary>
public partial class AgMonitorControl : UserControl
{
    private readonly ObservableCollection<AgGroupRow> _groups = new ObservableCollection<AgGroupRow>();
    private readonly ObservableCollection<AgReplicaRow> _replicas = new ObservableCollection<AgReplicaRow>();
    private readonly ObservableCollection<AgDatabaseRow> _databases = new ObservableCollection<AgDatabaseRow>();
    private readonly ObservableCollection<AgDatabaseRow> _attention = new ObservableCollection<AgDatabaseRow>();
    private readonly ObservableCollection<AgSeedingRow> _seeding = new ObservableCollection<AgSeedingRow>();
    private readonly ObservableCollection<AgAutoSeedRow> _autoSeeding = new ObservableCollection<AgAutoSeedRow>();
    private readonly ObservableCollection<AgIssueRow> _issues = new ObservableCollection<AgIssueRow>();
    private readonly ObservableCollection<AgClusterMemberRow> _clusterMembers = new ObservableCollection<AgClusterMemberRow>();
    private readonly ObservableCollection<AgClusterNetworkRow> _clusterNetworks = new ObservableCollection<AgClusterNetworkRow>();
    private readonly ObservableCollection<AgClusterNodeRow> _clusterNodes = new ObservableCollection<AgClusterNodeRow>();
    private readonly ObservableCollection<AgListenerRow> _listeners = new ObservableCollection<AgListenerRow>();
    private readonly ObservableCollection<AgRoutingRow> _routing = new ObservableCollection<AgRoutingRow>();
    private readonly ObservableCollection<AgThroughputRow> _throughput = new ObservableCollection<AgThroughputRow>();
    private readonly ObservableCollection<AgTransportRow> _transport = new ObservableCollection<AgTransportRow>();

    private readonly AgHistory _history = new AgHistory();

    // Cumulative AG counters need the previous reading to become a rate; reset when the server changes.
    private readonly AgCounterTracker _counters = new AgCounterTracker();

    private readonly DispatcherTimer _timer;
    private ICollectionView _issuesView;

    private AsyncPackage _package;
    private CancellationTokenSource _inFlight;
    private bool _polling;

    // Capabilities are per-server; cached against the connection string that produced them.
    private AgCapabilities _caps;
    private string _capsConnection;

    // Tab indices. Named because both "Copy tab" and "Open as query" switch on them, and a tab inserted in the
    // middle silently repointed both when they were bare numbers.
    private const int TabOverview = 0;
    private const int TabDiagnostics = 1;
    private const int TabReplicas = 2;
    private const int TabDatabases = 3;
    private const int TabThroughput = 4;
    private const int TabCluster = 5;
    private const int TabListeners = 6;
    private const int TabSeeding = 7;
    private const int TabErrors = 8;

    // The connection actually used for the last successful poll, reused by "Open as query" and the events load.
    private string _monitorConnection;

    // The connection this window is pinned to, captured when it was opened — see the class remarks.
    private readonly MonitorPin _pin = new MonitorPin();

    // SERVERPROPERTY('ServerName') from the last poll, for the caption and the header.
    private string _serverName;

    // SUSER_SNAME() from the last poll, for the header. Until one arrives the connection string's own answer stands
    // in — see MonitorPin.LoginFor.
    private string _loginName;

    // A poll finished against a connection this window is no longer pinned to; its replacement runs once the
    // in-flight one has unwound (BeginRefresh refuses to overlap polls).
    private bool _restartAfterPoll;

    /// <summary>
    /// The server this window is pinned to, normalised for matching (see <see cref="MonitorWindows.ServerKey"/>).
    /// Null until pinned; <see cref="AgMonitorCommand"/> reads it to decide whether an open window already covers
    /// the server being asked for.
    /// </summary>
    internal string PinnedServerKey => _pin.ServerKey;

    /// <summary>
    /// Set by the hosting tool window so the pane's caption can name the server. With several windows open the
    /// caption is the only thing distinguishing their tabs, so it is not optional decoration.
    /// </summary>
    internal Action<string> CaptionChanged;

    private List<AgEventRow> _allEvents = new List<AgEventRow>();

    private static readonly int[] IntervalSeconds = { 2, 5, 10, 30, 60 };
    private static readonly int[] EventCounts = { 200, 500, 2000 };

    public AgMonitorControl()
    {
        InitializeComponent();

        GroupCards.ItemsSource = _groups;
        ReplicaGrid.ItemsSource = _replicas;
        DatabaseGrid.ItemsSource = _databases;
        AttentionGrid.ItemsSource = _attention;
        SeedingCards.ItemsSource = _seeding;
        AutoSeedGrid.ItemsSource = _autoSeeding;
        ClusterMemberGrid.ItemsSource = _clusterMembers;
        ClusterNetworkGrid.ItemsSource = _clusterNetworks;
        ClusterNodeGrid.ItemsSource = _clusterNodes;
        ListenerGrid.ItemsSource = _listeners;
        RoutingGrid.ItemsSource = _routing;
        ThroughputGrid.ItemsSource = _throughput;
        TransportGrid.ItemsSource = _transport;

        // The issues grid filters through its collection view rather than by rebinding, so ticking "Problems
        // only" keeps the scroll position on a long list.
        IssueGrid.ItemsSource = _issues;
        _issuesView = CollectionViewSource.GetDefaultView(_issues);
        _issuesView.Filter = IssueFilter;

        foreach (int seconds in IntervalSeconds)
            IntervalCombo.Items.Add(seconds + "s");
        IntervalCombo.SelectedIndex = 1; // 5s — fast enough to watch a queue move, slow enough to be polite

        foreach (int count in EventCounts)
            EventCountCombo.Items.Add("Top " + count);
        EventCountCombo.SelectedIndex = 1;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(IntervalSeconds[IntervalCombo.SelectedIndex]) };
        _timer.Tick += (s, e) => BeginRefresh(userInitiated: false);

        Unloaded += OnUnloaded;
    }

    internal void SetPackage(AsyncPackage package) => _package = package;

    // -------------------------------------------------------------------------------------------------
    // Toolbar
    // -------------------------------------------------------------------------------------------------

    private void Refresh_Click(object sender, RoutedEventArgs e) => BeginRefresh(userInitiated: true);

    private void AutoRefresh_Changed(object sender, RoutedEventArgs e)
    {
        if (AutoRefreshCheck.IsChecked == true)
        {
            _timer.Start();
            BeginRefresh(userInitiated: true);
        }
        else
        {
            _timer.Stop();
        }
    }

    private void Interval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_timer == null) return;
        int index = Math.Max(0, Math.Min(IntervalSeconds.Length - 1, IntervalCombo.SelectedIndex));
        _timer.Interval = TimeSpan.FromSeconds(IntervalSeconds[index]);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        try { _inFlight?.Cancel(); } catch { }
        _history.Clear();
        _counters.Clear();
    }

    private void IssuesOnly_Changed(object sender, RoutedEventArgs e)
    {
        _issuesView?.Refresh();
        UpdateIssuesHint();
    }

    /// <summary>
    /// Hides the informational rows when "Problems only" is ticked. The all-clear row is informational, so
    /// ticking this on a healthy server correctly leaves an empty grid — the hint line below says so.
    /// </summary>
    private bool IssueFilter(object item)
    {
        if (!(item is AgIssueRow issue)) return false;
        return IssuesOnlyCheck?.IsChecked != true || issue.Severity != AgIssueSeverity.Information;
    }

    private void GoToDiagnostics_Click(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = TabDiagnostics;

    /// <summary>Reads the diagnostic thresholds out of settings. Loaded per poll so an edit takes effect at once.</summary>
    private static AgThresholds CurrentThresholds()
    {
        var settings = SQLExtendedSettings.Current;
        return new AgThresholds
        {
            RpoWarningSeconds = settings.AgRpoWarningSeconds,
            RpoCriticalSeconds = settings.AgRpoCriticalSeconds,
            SecondaryLagWarningSeconds = settings.AgSecondaryLagWarningSeconds,
            SendQueueWarningKb = settings.AgSendQueueWarningKb,
            RedoQueueWarningKb = settings.AgRedoQueueWarningKb,
            CommitDelayWarningMs = settings.AgCommitDelayWarningMs
        };
    }

    // -------------------------------------------------------------------------------------------------
    // Pinning
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Binds this window to one replica for the rest of its life and starts a collection. UI thread only.
    /// </summary>
    internal void PinTo(string connectionString, string serverLabel)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // Nothing to pin to. A window already watching a server keeps it — losing a working pin because the caller
        // could not resolve a connection this time would be a worse outcome than doing nothing.
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (!_pin.IsPinned) ShowNoConnection(serverLabel);
            return;
        }

        // Re-pinning to a different instance: the capability probe, the queue history and the counter baseline all
        // belong to the old replica. Only reachable when the window cap is hit, never silently.
        if (_pin.Set(connectionString))
        {
            _groups.Clear(); _replicas.Clear(); _databases.Clear(); _attention.Clear(); _seeding.Clear(); _autoSeeding.Clear();
            _clusterMembers.Clear(); _clusterNetworks.Clear(); _clusterNodes.Clear(); _listeners.Clear(); _routing.Clear();
            _throughput.Clear(); _transport.Clear(); _issues.Clear();
            _allEvents = new List<AgEventRow>();
            _history.Clear();
            _counters.Clear();
            _caps = null;
            _capsConnection = null;
            _serverName = null;
            _loginName = null;
            TimingText.Text = "";
        }

        ApplyPinnedChrome(serverLabel);
        ShowNotice(null);
        BeginRefresh(userInitiated: true);
    }

    /// <summary>Names the pinned replica in the caption, the header and the header's tooltip.</summary>
    private void ApplyPinnedChrome(string serverLabel)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string display = FirstNonEmpty(_serverName, serverLabel, _pin.Target) ?? "(not connected)";
        string login = _pin.LoginFor(_loginName);

        ServerText.Text = display;
        LoginText.Text = string.IsNullOrEmpty(login) ? "" : "as " + login;
        ServerText.ToolTip = LoginText.ToolTip = _pin.Describe(display, login);

        // The login is deliberately not in the caption: with several windows docked the tab strip has room for the
        // server and nothing else, and the server is what distinguishes them.
        CaptionChanged?.Invoke(display);
    }

    /// <summary>
    /// Nothing to pin to. Left unpinned deliberately: the next Refresh falls back to the active query window and
    /// pins to that, which is what the message tells the user to do.
    /// </summary>
    internal void ShowNoConnection(string requestedServer)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        ShowNotice(string.IsNullOrEmpty(requestedServer)
            ? "No SQL connection to pin this window to. Open or focus a query window connected to an availability-group replica, then press Refresh."
            : $"Could not get a connection for {requestedServer}. Open a query window against it, then press Refresh.");

        StatusText.Text = "Not connected.";
        CaptionChanged?.Invoke(null);
    }

    /// <summary>
    /// Says that this window was re-pointed at a different replica because the window cap was reached, rather than a
    /// new one being opened — the grids alone give no hint that anything was displaced.
    /// </summary>
    internal void ShowRepinnedNotice(int maxWindows)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        ShowNotice($"This window was re-pinned to {_pin.Target} — {maxWindows} Always On windows were already open, "
                 + "which is the maximum. Close one to open a server in its own window.");
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value;
        return null;
    }

    // -------------------------------------------------------------------------------------------------
    // Refresh pipeline
    // -------------------------------------------------------------------------------------------------

    /// <summary>Fire-and-forget refresh. Silently no-ops if a poll is already running.</summary>
    internal void BeginRefresh(bool userInitiated)
    {
        if (_polling)
        {
            if (userInitiated) StatusText.Text = "A refresh is already running.";
            return;
        }

        var package = _package;
        if (package == null) { StatusText.Text = "Package not initialised."; return; }

        _ = package.JoinableTaskFactory.RunAsync(async () => await RefreshAsync(package, userInitiated));
    }

    private async Task RefreshAsync(AsyncPackage package, bool userInitiated)
    {
        _polling = true;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(package.DisposalToken);
        _inFlight = cts;

        try
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cts.Token);
            RefreshButton.IsEnabled = false;

            // Progress and the early paint are for the polls a person is waiting on — the first one and an explicit
            // Refresh. On the five-second timer they would replace a settled summary with a flicker of step text
            // and re-merge the Overview twice per tick, which is cost and noise for a window already populated.
            var progress = userInitiated
                ? new MonitorStatusReporter(package.JoinableTaskFactory, cts.Token, text => StatusText.Text = text)
                : null;

            if (userInitiated) StatusText.Text = "Connecting…";

            if (!_pin.IsPinned)
            {
                // Unpinned: the window was opened with nothing to pin to, so Refresh means "use the active query
                // window now, and keep it" — which is what ShowNoConnection told the user to do. Connection
                // discovery must happen on the UI thread; it reflects into SSMS's editor internals.
                string baseConnection = ConnectionHelper.GetActiveConnectionString();
                if (string.IsNullOrEmpty(baseConnection))
                {
                    ShowNoConnection(null);
                    return;
                }

                _pin.Set(baseConnection);
                ShowNotice(null);
                ApplyPinnedChrome(null);
            }

            // The pinned connection, not whatever the active editor happens to be on. Captured here on the UI
            // thread so the poll can be compared against it afterwards and discarded if the pin moved underneath.
            string connection = AgQueryService.BuildMonitorConnectionString(_pin.Connection);

            // Read on the UI thread — SQLExtendedSettings.Current is not thread-safe to fault in from a worker.
            var thresholds = CurrentThresholds();

            // Shows the Overview as soon as the three sections behind it are in, rather than after the cluster,
            // listener, counter and seeding reads it does not display. The collection awaits this, so nothing is
            // writing to the snapshot while the grids read it — see MonitorPlan.
            Func<AgSnapshot, Task> overviewReady = !userInitiated ? (Func<AgSnapshot, Task>)null : async partial =>
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(cts.Token);

                if (string.Equals(AgQueryService.BuildMonitorConnectionString(_pin.Connection), connection, StringComparison.Ordinal))
                    ApplyOverview(partial, stillCollecting: true);

                await TaskScheduler.Default;
            };

            // Everything from here is off the UI thread.
            await TaskScheduler.Default;

            if (_caps == null || !string.Equals(_capsConnection, connection, StringComparison.Ordinal))
            {
                _caps = await AgCapabilities.ProbeAsync(connection, cts.Token).ConfigureAwait(false);
                _capsConnection = connection;

                // A different server means the previous window's samples describe something else entirely, and
                // the counter baseline belongs to a different instance's uptime.
                _history.Clear();
                _counters.Clear();
            }

            var snapshot = await AgQueryService.CollectAsync(connection, _caps, _counters, thresholds, progress, overviewReady, cts.Token).ConfigureAwait(false);
            _history.Record(snapshot.Databases);
            _history.Prune(new HashSet<string>(snapshot.Databases.Select(d => d.Key), StringComparer.OrdinalIgnoreCase));

            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cts.Token);

            // The window was re-pinned while this poll was in flight, so the snapshot describes a replica this
            // window no longer shows. The re-pin's own BeginRefresh was refused as "already running", so the
            // replacement is queued here — otherwise the new server's grids stay empty until the next tick.
            if (!string.Equals(AgQueryService.BuildMonitorConnectionString(_pin.Connection), connection, StringComparison.Ordinal))
            {
                _restartAfterPoll = true;
                return;
            }

            _monitorConnection = connection;
            Apply(snapshot);
        }
        catch (OperationCanceledException)
        {
            // Tool window closed or SSMS shutting down — nothing to report.
        }
        catch (Exception ex)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            StatusText.Text = "Refresh failed: " + ex.Message;
            ShowNotice(ex.Message);

            // A failing server on a timer would otherwise log and retry forever.
            if (AutoRefreshCheck.IsChecked == true)
            {
                AutoRefreshCheck.IsChecked = false;
                StatusText.Text = "Refresh failed, auto-refresh stopped: " + ex.Message;
            }

            ActivityLogHelper.LogError(package, "SQLExtended AG Monitor", "Refresh failed: " + ex);
        }
        finally
        {
            _polling = false;
            if (_inFlight == cts) _inFlight = null;
            try { cts.Dispose(); } catch { }

            try
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                RefreshButton.IsEnabled = true;

                if (_restartAfterPoll)
                {
                    _restartAfterPoll = false;
                    BeginRefresh(userInitiated: false);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Merges a completed snapshot into every bound collection and updates the chrome. UI thread only.
    ///
    /// Split in two so the Overview can be drawn part-way through a collection — see <see cref="ApplyOverview"/>.
    /// Both halves are called here in order, so a poll that had no early paint (every timer tick) ends up in
    /// exactly the state it did before the split.
    /// </summary>
    private void Apply(AgSnapshot snapshot)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        ApplyOverview(snapshot, stillCollecting: false);
        if (!snapshot.IsAvailable) return;

        ApplyRemainingTabs(snapshot);
    }

    /// <summary>
    /// The Overview tab: the group cards, the replica and database grids the attention list projects from, and the
    /// inventory line in the status bar.
    ///
    /// <para>Called twice on a user-initiated poll — once the moment its three sections are in (with
    /// <paramref name="stillCollecting"/> set, from <c>AgQueryService</c>'s hook) and once at the end. It is
    /// therefore written to be idempotent: merges, projections and text, nothing that accumulates. Anything with a
    /// running total — <c>AgHistory.Record</c>, notably — belongs in the caller or in
    /// <see cref="ApplyRemainingTabs"/>, or the sparklines would gain two samples per poll.</para>
    /// </summary>
    private void ApplyOverview(AgSnapshot snapshot, bool stillCollecting)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // The instance's own name for itself, which the caption and header prefer over the connect target — behind
        // a listener or a CNAME they differ, and with several windows open the real name is what tells them apart.
        _serverName = snapshot.ServerName;
        _loginName = snapshot.LoginName;
        ApplyPinnedChrome(null);

        if (!snapshot.IsAvailable)
        {
            _groups.Clear(); _replicas.Clear(); _databases.Clear(); _attention.Clear(); _seeding.Clear(); _autoSeeding.Clear();
            _issues.Clear(); _clusterMembers.Clear(); _clusterNetworks.Clear(); _clusterNodes.Clear();
            _listeners.Clear(); _routing.Clear(); _throughput.Clear(); _transport.Clear();
            ApplyClusterSummary(null);
            SetVerdict("Nothing to monitor", snapshot.UnavailableReason, AgIssueSeverity.Information);
            AttentionGrid.Visibility = Visibility.Collapsed;
            AttentionEmpty.Visibility = Visibility.Visible;
            AttentionEmpty.Text = snapshot.UnavailableReason;
            ShowNotice(snapshot.UnavailableReason);
            StatusText.Text = "Nothing to monitor.";
            TimingText.Text = "";

            if (AutoRefreshCheck.IsChecked == true)
                AutoRefreshCheck.IsChecked = false;
            return;
        }

        RowMerge.Apply(_groups, snapshot.Groups, r => r.Name ?? "", CopyGroup);
        RowMerge.Apply(_replicas, snapshot.Replicas, r => r.Key, CopyReplica);
        RowMerge.Apply(_databases, snapshot.Databases, r => r.Key, CopyDatabase);

        // The overview's attention list is a projection of the same rows, so reuse the merged instances —
        // that way both grids show identical values and share one set of history buffers. Unhealthy rows sort
        // above merely-degraded ones, so the worst thing is always at the top.
        var attention = _databases.Where(d => d.IsUnhealthy).Concat(_databases.Where(d => d.IsWarning)).ToList();
        RowMerge.Apply(_attention, attention, r => r.Key, (existing, updated) => { });
        AttentionGrid.Visibility = attention.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AttentionEmpty.Visibility = attention.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        AttentionEmpty.Text = _databases.Count == 0
            ? "No availability databases reported on this replica."
            : "All " + _databases.Count + " availability database replicas are healthy.";

        StatusText.Text = BuildStatus(stillCollecting);
    }

    /// <summary>
    /// Everything behind the Overview: the tabs the other sections fill, the diagnostics findings, the verdict
    /// strip and the final status and timing. Runs only once a collection has finished. UI thread only.
    /// </summary>
    private void ApplyRemainingTabs(AgSnapshot snapshot)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        RowMerge.Apply(_seeding, snapshot.Seeding, r => r.Key, CopySeeding);
        RowMerge.Apply(_autoSeeding, snapshot.AutoSeeding, r => r.Key, CopyAutoSeed);
        RowMerge.Apply(_clusterMembers, snapshot.ClusterMembers, r => r.Key, CopyClusterMember);
        RowMerge.Apply(_clusterNetworks, snapshot.ClusterNetworks, r => r.Key, CopyClusterNetwork);
        RowMerge.Apply(_clusterNodes, snapshot.ClusterNodes, r => r.Key, CopyClusterNode);
        RowMerge.Apply(_listeners, snapshot.Listeners, r => r.Key, CopyListener);
        RowMerge.Apply(_routing, snapshot.Routing, r => r.Key, CopyRouting);
        RowMerge.Apply(_throughput, snapshot.Throughput, r => r.Key, CopyThroughput);
        RowMerge.Apply(_transport, snapshot.Transport, r => r.Key, CopyTransport);

        // Findings are immutable value rows, so the merge only needs to add and remove; keying on the whole
        // finding means an unchanged one keeps its place (and the user's scroll position) across polls.
        RowMerge.Apply(_issues, snapshot.Issues, IssueKey, (existing, updated) => { });
        _issuesView.Refresh();
        UpdateIssuesHint();

        ApplyClusterSummary(snapshot);
        ApplyVerdict(snapshot);
        ApplyThroughputHint(snapshot);

        SeedingEmpty.Visibility = _seeding.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_caps != null && !_caps.HasPhysicalSeedingStats)
            SeedingEmpty.Text = "This SQL Server version does not expose sys.dm_hadr_physical_seeding_stats (automatic seeding requires SQL Server 2016 or later).";

        ShowNotice(BuildWarningText(snapshot));

        StatusText.Text = BuildStatus(stillCollecting: false);
        TimingText.Text = BuildTiming(snapshot);
    }

    /// <summary>
    /// The status line: what this replica holds and how much of it is in trouble, read off the bound collections
    /// rather than the snapshot so it says the same thing whether it is called part-way through a collection or at
    /// the end of one.
    /// </summary>
    private string BuildStatus(bool stillCollecting)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        int bad = _databases.Count(d => d.IsUnhealthy) + _replicas.Count(r => r.IsUnhealthy);
        int degraded = _databases.Count(d => d.IsWarning) + _replicas.Count(r => r.IsWarning);
        string inventory = $"{_groups.Count} group(s), {_replicas.Count} replica(s), {_databases.Count} database replica(s)";

        string status;
        if (bad == 0 && degraded == 0) status = inventory + " — all healthy.";
        else if (bad == 0) status = $"{inventory} — {degraded} degraded.";
        else if (degraded == 0) status = $"{inventory} — {bad} unhealthy.";
        else status = $"{inventory} — {bad} unhealthy, {degraded} degraded.";

        // Worth saying out loud: a secondary only reports its own database rows, and sees NULL for the other
        // replicas' operational and connected state. Without this note the sparse grids look like a bug.
        var local = _replicas.FirstOrDefault(r => r.IsLocal);
        if (local != null && !string.Equals(local.Role, "PRIMARY", StringComparison.OrdinalIgnoreCase))
        {
            string primary = _groups.Select(g => g.PrimaryReplica).FirstOrDefault(p => !string.IsNullOrEmpty(p));
            status += primary != null
                ? $"  Connected to a secondary — connect to {primary} for full cross-replica detail."
                : "  Connected to a secondary — cross-replica detail is limited.";
        }

        // Said explicitly because the Overview is complete at this point while every other tab is still empty —
        // which without a word about it reads as those tabs having nothing to show.
        if (stillCollecting) status += "   Still reading the other tabs…";

        return status;
    }

    /// <summary>
    /// When the snapshot was taken, how many sections it read and how long it took. The section count is here
    /// because what this window can cover varies with the release and with the login's rights: "9 sections" beside
    /// an empty tab says something a duration on its own does not.
    /// </summary>
    private static string BuildTiming(AgSnapshot snapshot)
    {
        string sections = snapshot.SectionsFailed > 0
            ? $"{snapshot.SectionsRead - snapshot.SectionsFailed} of {snapshot.SectionsRead} sections"
            : $"{snapshot.SectionsRead} sections";

        return $"{snapshot.CollectedAtLocal:HH:mm:ss} · {sections} · {snapshot.Duration.TotalMilliseconds:N0} ms";
    }

    // -------------------------------------------------------------------------------------------------
    // Verdict strip, cluster card and the two hint lines
    // -------------------------------------------------------------------------------------------------

    private static string IssueKey(AgIssueRow issue) => $"{issue.Severity}|{issue.Area}|{issue.Subject}|{issue.Detail}";

    private void UpdateIssuesHint()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (IssuesHint == null) return;

        int critical = _issues.Count(i => i.Severity == AgIssueSeverity.Critical);
        int warning = _issues.Count(i => i.Severity == AgIssueSeverity.Warning);
        int info = _issues.Count(i => i.Severity == AgIssueSeverity.Information);

        if (critical == 0 && warning == 0)
        {
            IssuesHint.Text = IssuesOnlyCheck?.IsChecked == true
                ? "No warnings or critical findings — the grid is empty because \"Problems only\" is hiding the informational rows."
                : $"No problems found. {info} informational note(s) below say what was checked.";
            return;
        }

        var parts = new List<string>();
        if (critical > 0) parts.Add($"{critical} critical");
        if (warning > 0) parts.Add($"{warning} warning");
        if (info > 0 && IssuesOnlyCheck?.IsChecked != true) parts.Add($"{info} informational");

        IssuesHint.Text = string.Join(", ", parts) + ". Hover the last column for the full explanation.";
    }

    /// <summary>
    /// The Overview's one-line answer. Worst finding wins, because the point of the strip is that you should not
    /// have to read the grids to know whether to worry.
    /// </summary>
    private void ApplyVerdict(AgSnapshot snapshot)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        int critical = snapshot.Issues.Count(i => i.Severity == AgIssueSeverity.Critical);
        int warning = snapshot.Issues.Count(i => i.Severity == AgIssueSeverity.Warning);

        if (critical > 0)
        {
            var worst = snapshot.Issues.First(i => i.Severity == AgIssueSeverity.Critical);
            SetVerdict(critical == 1 ? "1 critical finding" : $"{critical} critical findings",
                       $"{worst.Subject} — {worst.Detail}", AgIssueSeverity.Critical);
        }
        else if (warning > 0)
        {
            var worst = snapshot.Issues.First(i => i.Severity == AgIssueSeverity.Warning);
            SetVerdict(warning == 1 ? "1 warning" : $"{warning} warnings",
                       $"{worst.Subject} — {worst.Detail}", AgIssueSeverity.Warning);
        }
        else
        {
            SetVerdict("Healthy",
                       $"{snapshot.Groups.Count} group(s), {snapshot.Replicas.Count} replica(s) and {snapshot.Databases.Count} database replica(s) checked with no problems found.",
                       AgIssueSeverity.Information);
        }
    }

    private void SetVerdict(string headline, string detail, AgIssueSeverity severity)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        VerdictText.Text = headline;
        VerdictText.Foreground = (System.Windows.Media.Brush)_severityBrush.Convert(severity, typeof(System.Windows.Media.Brush), null, System.Globalization.CultureInfo.CurrentUICulture);
        VerdictDetailText.Text = detail ?? "";
    }

    private readonly AgSeverityBrushConverter _severityBrush = new AgSeverityBrushConverter();

    /// <summary>Fills the Cluster tab's card and the Overview's cluster line from the same snapshot.</summary>
    private void ApplyClusterSummary(AgSnapshot snapshot)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var cluster = snapshot?.Cluster;
        var health = new HealthBrushConverter();

        if (cluster == null)
        {
            ClusterNameText.Text = "—";
            QuorumTypeText.Text = "—";
            QuorumStateText.Text = "—";
            QuorumStateText.Foreground = (System.Windows.Media.Brush)health.Convert(null, typeof(System.Windows.Media.Brush), null, System.Globalization.CultureInfo.CurrentUICulture);

            // No cluster row is the expected shape for a read-scale group, so this is stated rather than blank.
            ClusterSummaryText.Text = snapshot == null
                ? ""
                : "No Windows failover cluster reported — normal for a CLUSTER_TYPE = NONE read-scale availability group.";
            return;
        }

        ClusterNameText.Text = string.IsNullOrWhiteSpace(cluster.ClusterName) ? "—" : cluster.ClusterName;
        QuorumTypeText.Text = cluster.QuorumType ?? "—";
        QuorumStateText.Text = cluster.QuorumState ?? "—";
        QuorumStateText.Foreground = (System.Windows.Media.Brush)health.Convert(
            cluster.IsQuorumHealthy ? "HEALTHY" : "NOT_HEALTHY", typeof(System.Windows.Media.Brush), null, System.Globalization.CultureInfo.CurrentUICulture);

        int votes = snapshot.ClusterMembers.Sum(m => m.QuorumVotes.GetValueOrDefault());
        int down = snapshot.ClusterMembers.Count(m => m.IsUnhealthy);
        string edition = _caps == null ? null : _caps.Edition;

        ClusterSummaryText.Text = $"Cluster {cluster.ClusterName} · quorum {cluster.QuorumState} ({cluster.QuorumType})"
            + $" · {snapshot.ClusterMembers.Count} member(s), {votes} vote(s)"
            + (down > 0 ? $", {down} down" : "")
            + (string.IsNullOrEmpty(edition) ? "" : $" · {edition}");
    }

    private void ApplyThroughputHint(AgSnapshot snapshot)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (ThroughputHint == null) return;

        string scope = "The Database Replica counter object only reports databases on this instance, so this describes the replica you are connected to.";

        // Without an interval the rate columns are all dashes, and saying why beats leaving the user to guess.
        ThroughputHint.Text = snapshot.CounterIntervalSeconds == null
            ? scope + "  Rate columns need two readings — they fill in on the next refresh."
            : scope + $"  Rates are over the last {snapshot.CounterIntervalSeconds.Value:N1}s, measured by the server's own clock.";
    }

    private string BuildWarningText(AgSnapshot snapshot)
    {
        if (snapshot.Warnings.Count == 0) return null;
        return "Some sections could not be collected — " + string.Join("; ", snapshot.Warnings);
    }

    private void ShowNotice(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (string.IsNullOrWhiteSpace(text))
        {
            NoticeBorder.Visibility = Visibility.Collapsed;
            NoticeText.Text = "";
            return;
        }

        NoticeText.Text = text;
        NoticeBorder.Visibility = Visibility.Visible;
    }

    // -------------------------------------------------------------------------------------------------
    // Row copiers for the in-place merge. Keys are excluded — they are what matched the rows.
    // -------------------------------------------------------------------------------------------------

    private static void CopyGroup(AgGroupRow into, AgGroupRow from)
    {
        into.GroupId = from.GroupId;
        into.PrimaryReplica = from.PrimaryReplica;
        into.SynchronizationHealth = from.SynchronizationHealth;
        into.PrimaryRecoveryHealth = from.PrimaryRecoveryHealth;
        into.ClusterType = from.ClusterType;
        into.AutomatedBackupPreference = from.AutomatedBackupPreference;
        into.FailureConditionLevel = from.FailureConditionLevel;
        into.HealthCheckTimeout = from.HealthCheckTimeout;
        into.RequiredSynchronizedSecondaries = from.RequiredSynchronizedSecondaries;
        into.IsDistributed = from.IsDistributed;
        into.ReplicaCount = from.ReplicaCount;
        into.DatabaseCount = from.DatabaseCount;
        into.UnhealthyCount = from.UnhealthyCount;
        into.WarningCount = from.WarningCount;
    }

    private static void CopyReplica(AgReplicaRow into, AgReplicaRow from)
    {
        into.Role = from.Role;
        into.AvailabilityMode = from.AvailabilityMode;
        into.FailoverMode = from.FailoverMode;
        into.OperationalState = from.OperationalState;
        into.ConnectedState = from.ConnectedState;
        into.SynchronizationHealth = from.SynchronizationHealth;
        into.RecoveryHealth = from.RecoveryHealth;
        into.SeedingMode = from.SeedingMode;
        into.ReadableSecondary = from.ReadableSecondary;
        into.BackupPriority = from.BackupPriority;
        into.EndpointUrl = from.EndpointUrl;
        into.IsLocal = from.IsLocal;
        into.LastConnectErrorNumber = from.LastConnectErrorNumber;
        into.LastConnectErrorDescription = from.LastConnectErrorDescription;
        into.LastConnectErrorTimestamp = from.LastConnectErrorTimestamp;
    }

    private static void CopyDatabase(AgDatabaseRow into, AgDatabaseRow from)
    {
        into.IsPrimaryReplica = from.IsPrimaryReplica;
        into.IsLocal = from.IsLocal;
        into.AvailabilityMode = from.AvailabilityMode;
        into.SynchronizationState = from.SynchronizationState;
        into.SynchronizationHealth = from.SynchronizationHealth;
        into.DatabaseState = from.DatabaseState;
        into.SuspendReason = from.SuspendReason;
        into.IsSuspended = from.IsSuspended;
        into.IsFailoverReady = from.IsFailoverReady;
        into.LogSendQueueKb = from.LogSendQueueKb;
        into.LogSendRateKbSec = from.LogSendRateKbSec;
        into.RedoQueueKb = from.RedoQueueKb;
        into.RedoRateKbSec = from.RedoRateKbSec;
        into.FilestreamSendRateKbSec = from.FilestreamSendRateKbSec;
        into.SecondaryLagSeconds = from.SecondaryLagSeconds;
        into.LastCommitTime = from.LastCommitTime;
        into.EndOfLogLsn = from.EndOfLogLsn;
        into.LastHardenedLsn = from.LastHardenedLsn;
        into.LastRedoneLsn = from.LastRedoneLsn;

        // The history buffers were assigned to the freshly collected row; move them across and notify.
        into.SendQueueHistory = from.SendQueueHistory;
        into.RedoQueueHistory = from.RedoQueueHistory;
        into.RaiseHistoryChanged();
    }

    private static void CopySeeding(AgSeedingRow into, AgSeedingRow from)
    {
        into.InternalState = from.InternalState;
        into.TransferredBytes = from.TransferredBytes;
        into.DatabaseSizeBytes = from.DatabaseSizeBytes;
        into.TransferRateBytesPerSecond = from.TransferRateBytesPerSecond;
        into.StartTimeUtc = from.StartTimeUtc;
        into.EndTimeUtc = from.EndTimeUtc;
        into.EstimateCompleteUtc = from.EstimateCompleteUtc;
        into.TotalDiskIoWaitMs = from.TotalDiskIoWaitMs;
        into.TotalNetworkWaitMs = from.TotalNetworkWaitMs;
        into.IsCompressionEnabled = from.IsCompressionEnabled;
        into.FailureMessage = from.FailureMessage;
    }

    private static void CopyClusterMember(AgClusterMemberRow into, AgClusterMemberRow from)
    {
        into.MemberType = from.MemberType;
        into.MemberState = from.MemberState;
        into.QuorumVotes = from.QuorumVotes;
    }

    private static void CopyClusterNetwork(AgClusterNetworkRow into, AgClusterNetworkRow from)
    {
        into.NetworkSubnetMask = from.NetworkSubnetMask;
        into.IsPublic = from.IsPublic;
        into.IsIpv4 = from.IsIpv4;
    }

    private static void CopyClusterNode(AgClusterNodeRow into, AgClusterNodeRow from) => into.JoinState = from.JoinState;

    private static void CopyListener(AgListenerRow into, AgListenerRow from)
    {
        into.Port = from.Port;
        into.IpSubnetMask = from.IpSubnetMask;
        into.NetworkSubnetIp = from.NetworkSubnetIp;
        into.IsDhcp = from.IsDhcp;
        into.IsConformant = from.IsConformant;
        into.IpConfigurationFromCluster = from.IpConfigurationFromCluster;
        into.State = from.State;
    }

    private static void CopyRouting(AgRoutingRow into, AgRoutingRow from)
    {
        into.TargetReadableSecondary = from.TargetReadableSecondary;
        into.TargetRole = from.TargetRole;
        into.ReadOnlyRoutingUrl = from.ReadOnlyRoutingUrl;
        into.ReadWriteRoutingUrl = from.ReadWriteRoutingUrl;
    }

    private static void CopyThroughput(AgThroughputRow into, AgThroughputRow from)
    {
        into.LogSendQueueKb = from.LogSendQueueKb;
        into.RecoveryQueueKb = from.RecoveryQueueKb;
        into.RedoBytesRemainingKb = from.RedoBytesRemainingKb;
        into.LogRemainingForUndoKb = from.LogRemainingForUndoKb;
        into.TotalLogRequiringUndoKb = from.TotalLogRequiringUndoKb;
        into.TransactionDelayMsPerSec = from.TransactionDelayMsPerSec;
        into.MirroredWriteTransactionsPerSec = from.MirroredWriteTransactionsPerSec;
        into.LogBytesReceivedPerSec = from.LogBytesReceivedPerSec;
        into.RedoneBytesPerSec = from.RedoneBytesPerSec;
        into.FileBytesReceivedPerSec = from.FileBytesReceivedPerSec;

        // The threshold can change under the row (settings edit), and it decides the tint.
        into.CommitDelayWarningMs = from.CommitDelayWarningMs;
        into.RaiseThresholdChanged();
    }

    private static void CopyTransport(AgTransportRow into, AgTransportRow from)
    {
        into.BytesSentToReplicaPerSec = from.BytesSentToReplicaPerSec;
        into.BytesSentToTransportPerSec = from.BytesSentToTransportPerSec;
        into.BytesReceivedFromReplicaPerSec = from.BytesReceivedFromReplicaPerSec;
        into.SendsToReplicaPerSec = from.SendsToReplicaPerSec;
        into.ReceivesFromReplicaPerSec = from.ReceivesFromReplicaPerSec;
        into.ResentMessagesPerSec = from.ResentMessagesPerSec;
        into.FlowControlPerSec = from.FlowControlPerSec;
        into.FlowControlTimeMsPerSec = from.FlowControlTimeMsPerSec;
    }

    private static void CopyAutoSeed(AgAutoSeedRow into, AgAutoSeedRow from)
    {
        into.CompletionTime = from.CompletionTime;
        into.CurrentState = from.CurrentState;
        into.PerformedSeeding = from.PerformedSeeding;
        into.IsSource = from.IsSource;
        into.FailureState = from.FailureState;
        into.ErrorCode = from.ErrorCode;
        into.NumberOfAttempts = from.NumberOfAttempts;
    }

    // -------------------------------------------------------------------------------------------------
    // Errors tab
    // -------------------------------------------------------------------------------------------------

    private void LoadEvents_Click(object sender, RoutedEventArgs e)
    {
        var package = _package;
        if (package == null) return;

        string connection = _monitorConnection;
        if (string.IsNullOrEmpty(connection))
        {
            EventsHint.Text = "Refresh first so the monitor knows which server to read.";
            return;
        }

        int top = EventCounts[Math.Max(0, Math.Min(EventCounts.Length - 1, EventCountCombo.SelectedIndex))];

        _ = package.JoinableTaskFactory.RunAsync(async () =>
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            LoadEventsButton.IsEnabled = false;
            EventsHint.Text = "Reading the AlwaysOn_health event file…";

            try
            {
                await TaskScheduler.Default;
                var events = await AgQueryService.ReadHealthEventsAsync(connection, top, package.DisposalToken).ConfigureAwait(false);

                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                _allEvents = events;
                ApplyEventFilter();
                EventsHint.Text = events.Count == 0
                    ? "The session is running but its current file holds no events yet."
                    : $"{events.Count} event(s) from the session's current rollover file.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                EventsHint.Text = ex.Message;
            }
            finally
            {
                try
                {
                    await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                    LoadEventsButton.IsEnabled = true;
                }
                catch { }
            }
        });
    }

    private void ErrorsOnly_Changed(object sender, RoutedEventArgs e) => ApplyEventFilter();

    private void ApplyEventFilter()
    {
        if (EventGrid == null) return;
        bool errorsOnly = ErrorsOnlyCheck.IsChecked == true;
        EventGrid.ItemsSource = errorsOnly ? _allEvents.Where(x => x.IsError).ToList() : _allEvents;
    }

    // -------------------------------------------------------------------------------------------------
    // Copy / open as query
    // -------------------------------------------------------------------------------------------------

    private void CopyTab_Click(object sender, RoutedEventArgs e)
    {
        var grid = ActiveGrid();
        if (grid == null || grid.Items.Count == 0) { StatusText.Text = "Nothing to copy on this tab."; return; }

        var text = new StringBuilder();
        var columns = grid.Columns.Where(c => c.Visibility == Visibility.Visible).ToList();

        text.AppendLine(string.Join("\t", columns.Select(c => c.Header?.ToString() ?? "")));

        foreach (var item in grid.Items)
        {
            var cells = columns.Select(c => CellText(c, item)?.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ') ?? "");
            text.AppendLine(string.Join("\t", cells));
        }

        try
        {
            Clipboard.SetText(text.ToString());
            StatusText.Text = $"Copied {grid.Items.Count} row(s) as tab-separated text.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Copy failed: " + ex.Message;
        }
    }

    /// <summary>
    /// Resolves a cell's displayed text for the clipboard. Template columns (the coloured state cells and the
    /// sparklines) have no binding to read, so their underlying property is looked up by the column's
    /// SortMemberPath — which is set on exactly those columns for this reason.
    /// </summary>
    private static string CellText(DataGridColumn column, object item)
    {
        if (column is DataGridBoundColumn bound && bound.Binding is System.Windows.Data.Binding binding)
            return ValueOf(item, binding.Path?.Path);

        return ValueOf(item, column.SortMemberPath);
    }

    private static string ValueOf(object item, string path)
    {
        if (item == null || string.IsNullOrEmpty(path)) return "";
        var property = item.GetType().GetProperty(path);
        return property == null ? "" : Convert.ToString(property.GetValue(item));
    }

    /// <summary>
    /// The grid "Copy tab" acts on. Tabs that carry more than one grid nominate their primary one — the
    /// throughput detail, the quorum members, the listeners — since that is what the tab is named after.
    /// </summary>
    private DataGrid ActiveGrid()
    {
        switch (Tabs.SelectedIndex)
        {
            case TabOverview: return AttentionGrid;
            case TabDiagnostics: return IssueGrid;
            case TabReplicas: return ReplicaGrid;
            case TabDatabases: return DatabaseGrid;
            case TabThroughput: return ThroughputGrid;
            case TabCluster: return ClusterMemberGrid;
            case TabListeners: return ListenerGrid;
            case TabSeeding: return AutoSeedGrid;
            case TabErrors: return EventGrid;
            default: return null;
        }
    }

    private void OpenAsQuery_Click(object sender, RoutedEventArgs e)
    {
        var caps = _caps;
        if (caps == null) { StatusText.Text = "Refresh first so the monitor knows what this server supports."; return; }

        string sql = SqlForActiveTab(caps);
        if (sql == null) { StatusText.Text = "No query backs this tab."; return; }

        OpenTextInNewQueryWindow(sql);
    }

    /// <summary>
    /// The exact T-SQL behind the active tab, capability substitutions included, so what the user gets back
    /// runs on the server they are looking at rather than on the newest one.
    /// </summary>
    private string SqlForActiveTab(AgCapabilities caps)
    {
        switch (Tabs.SelectedIndex)
        {
            case TabOverview: return AgQueryService.GroupsSql(caps) + Environment.NewLine + AgQueryService.DatabasesSql(caps);

            // The findings are derived in this process, so what the user gets is the state they were derived
            // from — with a header saying so, otherwise the returned batch looks like it should reproduce them.
            case TabDiagnostics:
                return "-- The Diagnostics tab evaluates its rules in the extension, against the result of these"
                     + Environment.NewLine
                     + "-- three queries. There is no server-side query that produces the findings themselves."
                     + Environment.NewLine + Environment.NewLine
                     + AgQueryService.GroupsSql(caps) + Environment.NewLine
                     + AgQueryService.ReplicasSql(caps) + Environment.NewLine
                     + AgQueryService.DatabasesSql(caps);

            case TabReplicas: return AgQueryService.ReplicasSql(caps);
            case TabDatabases: return AgQueryService.DatabasesSql(caps);
            case TabThroughput: return AgQueryService.CountersSql;
            case TabCluster: return AgQueryService.ClusterSql(caps) + Environment.NewLine + AgQueryService.ClusterNodesSql(caps);
            case TabListeners: return AgQueryService.ListenersSql(caps) + Environment.NewLine + AgQueryService.RoutingSql(caps);
            case TabSeeding: return AgQueryService.PhysicalSeedingSql + Environment.NewLine + AgQueryService.AutoSeedingSql;
            case TabErrors: return AgQueryService.HealthEventsSql(EventCounts[Math.Max(0, Math.Min(EventCounts.Length - 1, EventCountCombo.SelectedIndex))]);
            default: return null;
        }
    }

    /// <summary>
    /// Writes the SQL to a temp .sql file and opens it as a query window, so it gets T-SQL highlighting and F5.
    /// Mirrors the approach in the Script Library and SQL History tool windows.
    /// </summary>
    private void OpenTextInNewQueryWindow(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
        if (dte == null) { StatusText.Text = "No DTE available."; return; }

        try
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SQLExtended", "AgMonitor");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"AlwaysOn_{DateTime.Now:yyyyMMdd_HHmmss_fff}.sql");
            System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));

            dte.ItemOperations.OpenFile(path, Constants.vsViewKindTextView);
            StatusText.Text = "Opened the tab's query in a new window.";
        }
        catch (Exception ex)
        {
            try { Clipboard.SetText(text); } catch { }
            StatusText.Text = $"Could not open a query window ({ex.Message}). Copied the SQL to the clipboard instead.";
        }
    }
}
