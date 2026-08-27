using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;
using SQLExtended.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SQLExtended.Monitoring.Performance;

/// <summary>
/// The live performance dashboard. Pinned to the connection it was opened from and polls the standard DMVs,
/// turning the cumulative ones into rates over the sample window.
///
/// <para><b>The connection is captured once, when the window is opened, and kept until it closes</b>; the window
/// does not follow the active query window. Everything here is a rate measured between two polls, so a connection
/// that moved mid-series would silently splice two servers' counters into one chart. The tool window is registered
/// <c>MultiInstances</c> so one window per server can be open at once — see <see cref="MonitorWindows"/> for the
/// matching and reuse rules, shared with the other three dashboards.</para>
///
/// Threading mirrors the Always On monitor: collection runs entirely off the UI thread, only the merge into the
/// bound collections happens on it, and polls never overlap — a slow server stretches the interval rather than
/// queueing work behind itself.
/// </summary>
public partial class PerfMonitorControl : UserControl
{
    private readonly ObservableCollection<PerfRequestRow> _requests = new ObservableCollection<PerfRequestRow>();
    private readonly ObservableCollection<PerfBlockingRow> _blocking = new ObservableCollection<PerfBlockingRow>();
    private readonly ObservableCollection<PerfWaitRow> _waits = new ObservableCollection<PerfWaitRow>();
    private readonly ObservableCollection<PerfWaitRow> _topWaits = new ObservableCollection<PerfWaitRow>();
    private readonly ObservableCollection<PerfQueryRow> _queries = new ObservableCollection<PerfQueryRow>();
    private readonly ObservableCollection<PerfFileRow> _files = new ObservableCollection<PerfFileRow>();
    private readonly ObservableCollection<PerfFileRow> _topFiles = new ObservableCollection<PerfFileRow>();
    private readonly ObservableCollection<PerfServerPropertyRow> _serverProperties = new ObservableCollection<PerfServerPropertyRow>();
    private readonly ObservableCollection<SqlServerBuild> _newerBuilds = new ObservableCollection<SqlServerBuild>();

    private readonly PerfVitals _vitals = new PerfVitals();
    private readonly PerfHistory _history = new PerfHistory();
    private readonly PerfDeltaTracker _tracker = new PerfDeltaTracker();
    private readonly DispatcherTimer _timer;

    private AsyncPackage _package;
    private CancellationTokenSource _inFlight;
    private bool _polling;

    private string _monitorConnection;
    private string _connectionKey;

    // The connection this window is pinned to, captured when it was opened — see the class remarks.
    private readonly MonitorPin _pin = new MonitorPin();

    // SERVERPROPERTY('ServerName') from the last poll, for the caption and the header.
    private string _serverName;

    // SUSER_SNAME() from the last poll, for the header. Until one arrives the connection string's own answer stands
    // in — see MonitorPin.LoginFor.
    private string _loginName;

    // The Server info tab's contents. Held rather than re-read each poll: nothing on it changes on a
    // five-second scale, so it is collected on the first poll for a server and on an explicit Refresh.
    private bool _haveServerInfo;

    // A poll finished against a connection this window is no longer pinned to; its replacement runs once the
    // in-flight one has unwound (BeginRefresh refuses to overlap polls).
    private bool _restartAfterPoll;

    /// <summary>
    /// The server this window is pinned to, normalised for matching (see <see cref="MonitorWindows.ServerKey"/>).
    /// Null until pinned; <see cref="PerfMonitorCommand"/> reads it to decide whether an open window already covers
    /// the server being asked for.
    /// </summary>
    internal string PinnedServerKey => _pin.ServerKey;

    /// <summary>
    /// Set by the hosting tool window so the pane's caption can name the server. With several windows open the
    /// caption is the only thing distinguishing their tabs, so it is not optional decoration.
    /// </summary>
    internal Action<string> CaptionChanged;

    private static readonly int[] IntervalSeconds = { 2, 5, 10, 30, 60 };
    private static readonly int[] TopCounts = { 25, 50, 100, 250 };

    private static readonly (string Label, PerfQueryMetric Metric)[] Metrics =
    {
        ("Average CPU", PerfQueryMetric.AvgCpu),
        ("Total CPU", PerfQueryMetric.TotalCpu),
        ("Average duration", PerfQueryMetric.AvgDuration),
        ("Total duration", PerfQueryMetric.TotalDuration),
        ("Average reads", PerfQueryMetric.AvgLogicalReads),
        ("Total reads", PerfQueryMetric.TotalLogicalReads),
        ("Execution count", PerfQueryMetric.ExecutionCount)
    };

    /// <summary>How many rows the Live tab's summary grids show before it stops being a summary.</summary>
    private const int LiveSummaryRows = 6;

    public PerfMonitorControl()
    {
        InitializeComponent();

        VitalsPanel.DataContext = _vitals;
        ActivityGrid.ItemsSource = _requests;
        BlockingGrid.ItemsSource = _blocking;
        WaitsGrid.ItemsSource = _waits;
        TopWaitsGrid.ItemsSource = _topWaits;
        QueriesGrid.ItemsSource = _queries;
        FilesGrid.ItemsSource = _files;
        TopFilesGrid.ItemsSource = _topFiles;
        ServerInfoGrid.ItemsSource = _serverProperties;
        NewerBuildsGrid.ItemsSource = _newerBuilds;

        foreach (int seconds in IntervalSeconds) IntervalCombo.Items.Add(seconds + "s");
        IntervalCombo.SelectedIndex = 1; // 5s

        foreach (var metric in Metrics) MetricCombo.Items.Add(metric.Label);
        MetricCombo.SelectedIndex = 0;

        foreach (int count in TopCounts) TopCountCombo.Items.Add("Top " + count);
        TopCountCombo.SelectedIndex = 0;

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
        int index = Clamp(IntervalCombo.SelectedIndex, IntervalSeconds.Length);
        _timer.Interval = TimeSpan.FromSeconds(IntervalSeconds[index]);
    }

    /// <summary>Changing the metric or row count only matters on the next collection, so fetch one now.</summary>
    private void QueryOption_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_package == null || _monitorConnection == null) return;
        BeginRefresh(userInitiated: true);
    }

    private void BenignWaits_Changed(object sender, RoutedEventArgs e)
    {
        // The benign filter is applied server-side, so the stored wait baseline no longer matches the row set
        // the next poll will return. Dropping it costs one interval and avoids reporting bogus first deltas.
        _tracker.Clear();
        if (_package != null && _monitorConnection != null) BeginRefresh(userInitiated: true);
    }

    private void ActivityFilter_Changed(object sender, RoutedEventArgs e) => ApplyActivityFilter();

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        try { _inFlight?.Cancel(); } catch { }
        _history.Clear();
        _tracker.Clear();
    }

    private static int Clamp(int index, int length) => Math.Max(0, Math.Min(length - 1, index));

    // -------------------------------------------------------------------------------------------------
    // Pinning
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Binds this window to one server for the rest of its life and starts a collection. UI thread only.
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

        // Re-pinning to a different instance: the rate columns, the charts and the delta baseline all belong to the
        // old server's uptime. Only reachable when the window cap is hit, never silently.
        if (_pin.Set(connectionString))
        {
            _requests.Clear(); _blocking.Clear(); _waits.Clear(); _topWaits.Clear(); _queries.Clear(); _files.Clear(); _topFiles.Clear();
            _history.Clear();
            _tracker.Clear();
            _connectionKey = null;
            _serverName = null;
            _loginName = null;
            ClearServerInfo();
            TimingText.Text = "";
        }

        ApplyPinnedChrome(serverLabel);
        ShowNotice(null);
        BeginRefresh(userInitiated: true);
    }

    /// <summary>Names the pinned server in the caption, the header and the header's tooltip.</summary>
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
            ? "No SQL connection to pin this window to. Open or focus a query window on the server you want to watch, then press Refresh."
            : $"Could not get a connection for {requestedServer}. Open a query window against it, then press Refresh.");

        StatusText.Text = "Not connected.";
        CaptionChanged?.Invoke(null);
    }

    /// <summary>
    /// Says that this window was re-pointed at a different server because the window cap was reached, rather than a
    /// new one being opened — the grids alone give no hint that anything was displaced.
    /// </summary>
    internal void ShowRepinnedNotice(int maxWindows)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        ShowNotice($"This window was re-pinned to {_pin.Target} — {maxWindows} Performance windows were already open, "
                 + "which is the maximum. Close one to open a server in its own window.");
    }

    // -------------------------------------------------------------------------------------------------
    // Refresh pipeline
    // -------------------------------------------------------------------------------------------------

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
            // and re-merge the Live tab twice per tick, which is cost and noise for a window already populated.
            var progress = userInitiated
                ? new MonitorStatusReporter(package.JoinableTaskFactory, cts.Token, text => StatusText.Text = text)
                : null;

            if (userInitiated) StatusText.Text = "Connecting…";

            if (!_pin.IsPinned)
            {
                // Unpinned: the window was opened with nothing to pin to, so Refresh means "use the active query
                // window now, and keep it" — which is what ShowNoConnection told the user to do. Connection
                // discovery reflects into SSMS internals and must run on the UI thread.
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
            string connection = PerfQueryService.BuildMonitorConnectionString(_pin.Connection);
            var metric = Metrics[Clamp(MetricCombo.SelectedIndex, Metrics.Length)].Metric;
            int top = TopCounts[Clamp(TopCountCombo.SelectedIndex, TopCounts.Length)];
            bool includeBenign = BenignWaitsCheck.IsChecked == true;

            // A different server's counters have nothing to do with this one's.
            if (!string.Equals(_connectionKey, connection, StringComparison.Ordinal))
            {
                _connectionKey = connection;
                _tracker.Clear();
                _history.Clear();
                ClearServerInfo();
            }

            // The Server info tab is not on the timer: it is read the first time a server is polled, and again
            // whenever the user asks for a refresh. Uptime is the only thing on it that moves.
            bool includeServerInfo = !_haveServerInfo || userInitiated;

            // Read on the UI thread — SQLExtendedSettings.Current is not thread-safe to fault in from a worker, and
            // the Server info collection runs on one.
            int recentDumpDays = SQLExtendedSettings.Current.PerfRecentDumpDays;

            // Shows the Live tab as soon as the sections behind it are in, rather than after the top-queries scan
            // and the server information read it does not display. The collection awaits this, so nothing is
            // writing to the snapshot while the grids read it — see MonitorPlan.
            Func<PerfSnapshot, Task> liveReady = !userInitiated ? (Func<PerfSnapshot, Task>)null : async partial =>
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(cts.Token);

                if (string.Equals(PerfQueryService.BuildMonitorConnectionString(_pin.Connection), connection, StringComparison.Ordinal))
                    ApplyLive(partial, stillCollecting: true);

                await TaskScheduler.Default;
            };

            await TaskScheduler.Default;

            var snapshot = await PerfQueryService.CollectAsync(connection, _tracker, metric, top, includeBenign, includeServerInfo, recentDumpDays,
                                                              progress, liveReady, cts.Token).ConfigureAwait(false);

            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cts.Token);

            // The window was re-pinned while this poll was in flight, so the snapshot describes a server this
            // window no longer shows. The re-pin's own BeginRefresh was refused as "already running", so the
            // replacement is queued here — otherwise the new server's grids stay empty until the next tick.
            if (!string.Equals(PerfQueryService.BuildMonitorConnectionString(_pin.Connection), connection, StringComparison.Ordinal))
            {
                _restartAfterPoll = true;
                return;
            }

            _monitorConnection = connection;
            Apply(snapshot);
        }
        catch (OperationCanceledException)
        {
            // Tool window closed or SSMS shutting down.
        }
        catch (Exception ex)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            StatusText.Text = "Refresh failed: " + ex.Message;
            ShowNotice(ex.Message);

            if (AutoRefreshCheck.IsChecked == true)
            {
                AutoRefreshCheck.IsChecked = false;
                StatusText.Text = "Refresh failed, auto-refresh stopped: " + ex.Message;
            }

            ActivityLogHelper.LogError(package, "SQLExtended Performance Monitor", "Refresh failed: " + ex);
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
    /// Split in two so the Live tab can be drawn part-way through a collection — see <see cref="ApplyLive"/>. Both
    /// halves are called here in order, so a poll that had no early paint (every timer tick) ends up in exactly the
    /// state it did before the split.
    /// </summary>
    private void Apply(PerfSnapshot snapshot)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        ApplyLive(snapshot, stillCollecting: false);

        // Deliberately out of ApplyLive and after it: this one accumulates, and ApplyLive runs twice on a
        // user-initiated poll. Recording there would put two samples per poll into every chart on this tab.
        _history.Record(_vitals);

        ApplyRemainingTabs(snapshot);
    }

    /// <summary>
    /// The Live tab: the vitals tiles and charts, and the two summary grids at its foot.
    ///
    /// <para>Called twice on a user-initiated poll — once the moment its sections are in (with
    /// <paramref name="stillCollecting"/> set, from <c>PerfQueryService</c>'s hook) and once at the end. It is
    /// therefore written to be idempotent: copies, merges and text, nothing that accumulates.</para>
    /// </summary>
    private void ApplyLive(PerfSnapshot snapshot, bool stillCollecting)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // The instance's own name for itself, which the caption and header prefer over the connect target — behind
        // a listener or a CNAME they differ, and with several windows open the real name is what tells them apart.
        _serverName = snapshot.ServerName;
        _loginName = snapshot.LoginName;
        ApplyPinnedChrome(null);

        CopyVitals(snapshot.Vitals);

        RowMerge.Apply(_waits, snapshot.Waits, r => r.Key, CopyWait);
        RowMerge.Apply(_files, snapshot.Files, r => r.Key, CopyFile);

        // The Live tab's two summary grids are the head of the full tabs, reusing the merged instances so the
        // numbers can never disagree between the summary and the detail.
        ReplaceTop(_topWaits, _waits, LiveSummaryRows);
        ReplaceTop(_topFiles, SortedByWorstLatency(_files), LiveSummaryRows);

        TopWaitsGrid.Visibility = _topWaits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TopWaitsEmpty.Visibility = _topWaits.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        TopFilesGrid.Visibility = _topFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TopFilesEmpty.Visibility = _topFiles.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        if (snapshot.IntervalSeconds == null)
        {
            TopWaitsEmpty.Text = "Rate columns need two samples — they will fill in on the next refresh.";
            TopFilesEmpty.Text = TopWaitsEmpty.Text;
            WaitsHint.Text = "Rate columns need two samples — they will fill in on the next refresh.";
        }
        else
        {
            TopWaitsEmpty.Text = "No waits recorded in the sample window.";
            TopFilesEmpty.Text = "No file I/O recorded in the sample window.";
            WaitsHint.Text = $"Deltas over the last {snapshot.IntervalSeconds.Value:N1}s, not totals since the instance started.";
        }

        StatusText.Text = BuildStatus(snapshot, stillCollecting);
    }

    /// <summary>
    /// Everything behind the Live tab: activity, blocking, top queries, the Server info tab, and the final status
    /// and timing. Runs only once a collection has finished. UI thread only.
    /// </summary>
    private void ApplyRemainingTabs(PerfSnapshot snapshot)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        RowMerge.Apply(_requests, snapshot.Requests, r => r.Key, CopyRequest);
        RowMerge.Apply(_blocking, snapshot.Blocking, r => r.Key, CopyBlocking);
        RowMerge.Apply(_queries, snapshot.Queries, r => r.Key, CopyQuery);

        // Null on the polls that did not ask for it, which is most of them — leave the tab as it was.
        if (snapshot.ServerInfo != null) ApplyServerInfo(snapshot.ServerInfo);

        ApplyActivityFilter();

        bool blocked = _blocking.Count > 0;
        BlockingGrid.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        BlockingEmpty.Visibility = blocked ? Visibility.Collapsed : Visibility.Visible;

        ShowNotice(snapshot.Warnings.Count == 0 ? null : "Some sections could not be collected — " + string.Join("; ", snapshot.Warnings));

        StatusText.Text = BuildStatus(snapshot, stillCollecting: false);
        TimingText.Text = BuildTiming(snapshot);
    }

    /// <param name="stillCollecting">
    /// Set while only the Live tab's sections are in. The blocking-chain count is left off then rather than
    /// reported as zero — the requests it is derived from have not been read yet, and "0 blocking chains" is a
    /// statement about the server, not about how far this poll has got.
    /// </param>
    private string BuildStatus(PerfSnapshot snapshot, bool stillCollecting)
    {
        var parts = new List<string>
        {
            $"{_vitals.ActiveRequests} active",
            $"{_vitals.UserSessions} sessions"
        };

        if (_vitals.BlockedRequests > 0) parts.Add($"{_vitals.BlockedRequests} blocked");
        if (!stillCollecting && _blocking.Count > 0) parts.Add($"{_blocking.Count(b => b.IsHeadBlocker)} blocking chain(s)");

        parts.Add(snapshot.IntervalSeconds == null
            ? "rates pending a second sample"
            : $"rates over {snapshot.IntervalSeconds.Value:N1}s");

        string status = string.Join(" · ", parts);

        // Said explicitly because the Live tab is complete at this point while Activity, Top queries and Server
        // info are still empty — which without a word about it reads as those tabs having nothing to show.
        if (stillCollecting) status += "   Still reading the other tabs…";

        return status;
    }

    /// <summary>
    /// When the snapshot was taken, how many sections it read and how long it took. The section count is here
    /// because what this window can cover varies with the release and with the login's rights: "6 sections" beside
    /// an empty tab says something a duration on its own does not.
    /// </summary>
    private static string BuildTiming(PerfSnapshot snapshot)
    {
        string sections = snapshot.SectionsFailed > 0
            ? $"{snapshot.SectionsRead - snapshot.SectionsFailed} of {snapshot.SectionsRead} sections"
            : $"{snapshot.SectionsRead} sections";

        return $"{snapshot.CollectedAtLocal:HH:mm:ss} · {sections} · {snapshot.Duration.TotalMilliseconds:N0} ms";
    }

    /// <summary>Replaces a summary collection with the first N of a source, reusing the same row instances.</summary>
    private static void ReplaceTop<T>(ObservableCollection<T> target, IEnumerable<T> source, int count)
    {
        var wanted = source.Take(count).ToList();

        // Short lists, replaced wholesale: the summary grids are not interactive enough for a merge to earn
        // its complexity, and clearing six rows costs nothing.
        target.Clear();
        foreach (var item in wanted) target.Add(item);
    }

    private static IEnumerable<PerfFileRow> SortedByWorstLatency(IEnumerable<PerfFileRow> files) =>
        files.OrderByDescending(f => Math.Max(f.ReadLatencyMs ?? 0, f.WriteLatencyMs ?? 0));

    private void ApplyActivityFilter()
    {
        if (ActivityGrid == null) return;

        bool runningOnly = ActiveOnlyCheck.IsChecked == true;
        ActivityGrid.ItemsSource = runningOnly ? (System.Collections.IEnumerable)_requests.Where(r => r.IsRunning).ToList() : _requests;
    }

    // -------------------------------------------------------------------------------------------------
    // Server info
    // -------------------------------------------------------------------------------------------------

    private static readonly System.Windows.Media.Brush Neutral = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly System.Windows.Media.Brush Good = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0xB0));
    private static readonly System.Windows.Media.Brush Warn = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD7, 0xBA, 0x7D));
    private static readonly System.Windows.Media.Brush Bad = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x87, 0x71));
    private static readonly System.Windows.Media.Brush Muted = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));

    private void ClearServerInfo()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _haveServerInfo = false;
        _serverProperties.Clear();
        _newerBuilds.Clear();

        InfoReleaseText.Text = "—";
        InfoEditionText.Text = "";
        InfoVersionText.Text = "";
        InfoPatchText.Text = "—";
        InfoPatchText.Foreground = Neutral;
        InfoPatchStatusText.Text = "";
        InfoLatestText.Text = "";
        InfoSupportText.Text = "—";
        InfoSupportText.Foreground = Neutral;
        InfoSupportSubText.Text = "";
        InfoUptimeText.Text = "—";
        InfoStartedText.Text = "";

        NewerBuildsGrid.Visibility = Visibility.Collapsed;
        NewerBuildsEmpty.Visibility = Visibility.Visible;
        NewerBuildsEmpty.Text = "Nothing to show until the server information has been collected.";
    }

    /// <summary>
    /// Fills the Server info tab. UI thread only.
    ///
    /// <para>Every verdict here is qualified by the build list's snapshot date, and the three cases the patch
    /// tile has to keep apart are the reason this is written out rather than reduced to a single string: the
    /// build being the newest listed, the build being <i>above</i> everything listed (the snapshot is stale,
    /// which says nothing about the server), and the build not being listed at all. Collapsing the middle case
    /// into the first turns "I cannot tell" into a clean bill of health.</para>
    /// </summary>
    private void ApplyServerInfo(PerfServerInfo info)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _haveServerInfo = true;

        RowMerge.Apply(_serverProperties, info.Properties, r => r.Key, CopyServerProperty);

        var match = info.Build;
        var release = match?.Release;

        InfoReleaseText.Text = release?.Name ?? info.ProductVersion ?? "(unknown)";
        InfoEditionText.Text = info.Edition ?? info.EngineEditionDescription ?? "";
        InfoVersionText.Text = info.ProductVersion == null ? "" : "build " + info.ProductVersion;

        ApplyPatchTile(info, match, release);
        ApplySupportTile(match, release);

        InfoUptimeText.Text = PerfServerInfoQuery.Duration(info.UptimeSeconds) ?? "—";
        InfoStartedText.Text = info.StartTime == null ? "" : "started " + info.StartTime.Value.ToString("yyyy-MM-dd HH:mm");

        // Replaced rather than merged by key: these are the catalog's own immutable instances, so a merge would
        // copy each row onto itself, and the build list carries the odd repeated build number (two hotfix
        // articles, one build) that a key-based merge would silently collapse into one row.
        _newerBuilds.Clear();
        foreach (var build in info.NewerBuilds) _newerBuilds.Add(build);

        bool any = _newerBuilds.Count > 0;
        NewerBuildsGrid.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        NewerBuildsEmpty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        NewerBuildsEmpty.Text = release == null
            ? "This release is not in the build list snapshot, so nothing can be listed for it."
            : info.IsAzureManaged
                ? "Azure keeps this service patched; there is no update list to show."
                : $"No build newer than this one is listed for {release.Name} in the {SqlBuildCatalog.SnapshotDate} snapshot.";

        InfoSourceRun.Text = $"Build list snapshot {SqlBuildCatalog.SnapshotDate}, collected {info.CollectedAtLocal:HH:mm:ss} — source ";
    }

    private void ApplyPatchTile(PerfServerInfo info, SqlBuildMatch match, SqlServerRelease release)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var best = match?.Best;

        // What the instance calls its own level, used when the build list has nothing to say. It is the more
        // reliable of the two for identity and the less useful for "is anything newer out".
        string reported = FirstNonEmpty(info.ProductUpdateLevel, info.ProductLevel);

        InfoPatchText.Text = best?.Label ?? reported ?? info.ProductVersion ?? "—";
        InfoLatestText.Text = "";

        if (info.IsAzureManaged)
        {
            InfoPatchText.Foreground = Neutral;
            InfoPatchStatusText.Foreground = Muted;
            InfoPatchStatusText.Text = "Patched by Azure. The build list covers the box product, so no update comparison applies here.";
            return;
        }

        if (release == null)
        {
            InfoPatchText.Foreground = Neutral;
            InfoPatchStatusText.Foreground = Warn;
            InfoPatchStatusText.Text = $"This release is not in the {SqlBuildCatalog.SnapshotDate} build list snapshot — almost certainly newer than it. "
                                     + "Regenerate the snapshot to compare builds.";
            return;
        }

        if (release.LatestBuild != null)
        {
            InfoLatestText.Text = "Newest listed for " + release.Name + ": " + release.LatestBuild.Display
                                + (release.LatestBuild.Released == null ? "" : ", " + release.LatestBuild.Released.Value.ToString("yyyy-MM-dd"));
        }

        // Withdrawn and pre-release come first: both matter more than how far behind the build is.
        if (best != null && best.Withdrawn)
        {
            InfoPatchText.Foreground = Bad;
            InfoPatchStatusText.Foreground = Bad;
            InfoPatchStatusText.Text = "This build was withdrawn by Microsoft after release. " + (best.Description ?? "");
            return;
        }

        if (best != null && best.Kind == SqlBuildKind.Preview)
        {
            InfoPatchText.Foreground = Bad;
            InfoPatchStatusText.Foreground = Bad;
            InfoPatchStatusText.Text = "This is a pre-release build: " + (best.Description ?? best.Build) + ".";
            return;
        }

        if (match.NewerThanCatalog)
        {
            // Above everything the snapshot lists. That is a statement about the snapshot, not the server, and
            // the wording has to be the one thing it cannot be mistaken for — "up to date".
            InfoPatchText.Foreground = Neutral;
            InfoPatchStatusText.Foreground = Muted;
            InfoPatchStatusText.Text = $"Newer than every build listed for {release.Name} in the {SqlBuildCatalog.SnapshotDate} snapshot, "
                                     + "so the snapshot cannot say whether anything newer exists.";
            return;
        }

        if (match.IsLatestKnown)
        {
            InfoPatchText.Foreground = Good;
            InfoPatchStatusText.Foreground = Good;
            InfoPatchStatusText.Text = $"Newest build listed for {release.Name} as at the {SqlBuildCatalog.SnapshotDate} snapshot"
                                     + (best.Released == null ? "." : $", released {best.Released.Value:yyyy-MM-dd}.");
            return;
        }

        InfoPatchText.Foreground = Warn;
        InfoPatchStatusText.Foreground = Warn;

        var parts = new List<string>();
        if (match.Exact == null && best != null)
            parts.Add($"This exact build is not listed; the closest below it is {best.Display}");

        parts.Add(match.NewerCumulativeUpdates > 0
            ? $"{match.NewerBuilds} newer build(s) listed, including {match.NewerCumulativeUpdates} cumulative update(s)"
            : $"{match.NewerBuilds} newer build(s) listed");

        InfoPatchStatusText.Text = string.Join(". ", parts) + ".";
    }

    private void ApplySupportTile(SqlBuildMatch match, SqlServerRelease release)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (release == null || match == null || match.Phase == SqlSupportPhase.Unknown)
        {
            InfoSupportText.Text = "Unknown";
            InfoSupportText.Foreground = Neutral;
            InfoSupportSubText.Foreground = Muted;
            InfoSupportSubText.Text = "No support dates for this release in the build list snapshot.";
            return;
        }

        int? days = match.DaysUntilSupportEnds;

        switch (match.Phase)
        {
            case SqlSupportPhase.Mainstream:
                // Amber inside the last year: that is when it stops being a date on a slide and starts being a
                // project with a lead time.
                bool soon = days != null && days.Value <= 365;
                InfoSupportText.Text = "Mainstream";
                InfoSupportText.Foreground = soon ? Warn : Good;
                InfoSupportSubText.Foreground = soon ? Warn : Muted;
                InfoSupportSubText.Text = $"Ends {release.MainstreamSupportEnd:yyyy-MM-dd}"
                                        + (days == null ? "" : $" — {days.Value:N0} days")
                                        + (release.ExtendedSupportEnd == null ? "" : $". Extended support to {release.ExtendedSupportEnd:yyyy-MM-dd}.");
                break;

            case SqlSupportPhase.Extended:
                InfoSupportText.Text = "Extended";
                InfoSupportText.Foreground = Warn;
                InfoSupportSubText.Foreground = Warn;
                InfoSupportSubText.Text = $"Mainstream support ended {release.MainstreamSupportEnd:yyyy-MM-dd}. "
                                        + $"Extended support ends {release.ExtendedSupportEnd:yyyy-MM-dd}"
                                        + (days == null ? "." : $" — {days.Value:N0} days. ")
                                        + "Security fixes only.";
                break;

            default:
                InfoSupportText.Text = "Out of support";
                InfoSupportText.Foreground = Bad;
                InfoSupportSubText.Foreground = Bad;
                DateTime? ended = release.ExtendedSupportEnd ?? release.MainstreamSupportEnd;
                InfoSupportSubText.Text = (ended == null ? "Support has ended." : $"Support ended {ended:yyyy-MM-dd}.")
                                        + " No further security updates are published for this release.";
                break;
        }
    }

    private static void CopyServerProperty(PerfServerPropertyRow into, PerfServerPropertyRow from)
    {
        into.Value = from.Value;
        into.Hint = from.Hint;
        into.IsWarning = from.IsWarning;
    }

    /// <summary>Opens the build list in the default browser so the tab's verdict can be checked against it.</summary>
    private void InfoSource_Navigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try { System.Diagnostics.Process.Start(e.Uri.AbsoluteUri); }
        catch (Exception ex) { StatusText.Text = "Could not open the browser: " + ex.Message; }

        e.Handled = true;
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
    // Row copiers for the in-place merge
    // -------------------------------------------------------------------------------------------------

    private void CopyVitals(PerfVitals from)
    {
        _vitals.CpuSqlPercent = from.CpuSqlPercent;
        _vitals.CpuOtherPercent = from.CpuOtherPercent;
        _vitals.CpuCount = from.CpuCount;
        _vitals.CpuHistory = from.CpuHistory;
        _vitals.BatchRequestsPerSec = from.BatchRequestsPerSec;
        _vitals.CompilationsPerSec = from.CompilationsPerSec;
        _vitals.RecompilesPerSec = from.RecompilesPerSec;
        _vitals.TransactionsPerSec = from.TransactionsPerSec;
        _vitals.LockWaitsPerSec = from.LockWaitsPerSec;
        _vitals.PageLifeExpectancy = from.PageLifeExpectancy;
        _vitals.TotalServerMemoryKb = from.TotalServerMemoryKb;
        _vitals.TargetServerMemoryKb = from.TargetServerMemoryKb;
        _vitals.PhysicalMemoryInUseKb = from.PhysicalMemoryInUseKb;
        _vitals.ActiveRequests = from.ActiveRequests;
        _vitals.BlockedRequests = from.BlockedRequests;
        _vitals.UserSessions = from.UserSessions;
        _vitals.ActiveTransactions = from.ActiveTransactions;
        _vitals.LongestRunningSeconds = from.LongestRunningSeconds;
        _vitals.TempdbFreeMb = from.TempdbFreeMb;
        _vitals.TempdbUserObjectMb = from.TempdbUserObjectMb;
        _vitals.TempdbInternalObjectMb = from.TempdbInternalObjectMb;
        _vitals.TempdbVersionStoreMb = from.TempdbVersionStoreMb;
        _vitals.TempdbTotalMb = from.TempdbTotalMb;
    }

    private static void CopyRequest(PerfRequestRow into, PerfRequestRow from)
    {
        into.BlockingSessionId = from.BlockingSessionId;
        into.BlockedCount = from.BlockedCount;
        into.LoginName = from.LoginName;
        into.HostName = from.HostName;
        into.ProgramName = from.ProgramName;
        into.DatabaseName = from.DatabaseName;
        into.Status = from.Status;
        into.Command = from.Command;
        into.WaitType = from.WaitType;
        into.WaitTimeMs = from.WaitTimeMs;
        into.LastWaitType = from.LastWaitType;
        into.WaitResource = from.WaitResource;
        into.CpuTimeMs = from.CpuTimeMs;
        into.ElapsedMs = from.ElapsedMs;
        into.LogicalReads = from.LogicalReads;
        into.PhysicalReads = from.PhysicalReads;
        into.Writes = from.Writes;
        into.GrantedMemoryKb = from.GrantedMemoryKb;
        into.OpenTransactionCount = from.OpenTransactionCount;
        into.PercentComplete = from.PercentComplete;
        into.StartTime = from.StartTime;
        into.StatementText = from.StatementText;
        into.BatchText = from.BatchText;
        into.QueryHash = from.QueryHash;
        into.IsRunning = from.IsRunning;
    }

    private static void CopyBlocking(PerfBlockingRow into, PerfBlockingRow from)
    {
        into.Depth = from.Depth;
        into.BlockedCount = from.BlockedCount;
        into.LoginName = from.LoginName;
        into.HostName = from.HostName;
        into.ProgramName = from.ProgramName;
        into.DatabaseName = from.DatabaseName;
        into.Status = from.Status;
        into.WaitType = from.WaitType;
        into.WaitResource = from.WaitResource;
        into.WaitTimeMs = from.WaitTimeMs;
        into.ElapsedMs = from.ElapsedMs;
        into.OpenTransactionCount = from.OpenTransactionCount;
        into.StatementText = from.StatementText;
    }

    private static void CopyWait(PerfWaitRow into, PerfWaitRow from)
    {
        into.WaitTimeMsDelta = from.WaitTimeMsDelta;
        into.SignalWaitMsDelta = from.SignalWaitMsDelta;
        into.WaitingTasksDelta = from.WaitingTasksDelta;
        into.PercentOfTotal = from.PercentOfTotal;
        into.WaitTimeMsTotal = from.WaitTimeMsTotal;
        into.WaitingTasksTotal = from.WaitingTasksTotal;
        into.MaxWaitTimeMs = from.MaxWaitTimeMs;
    }

    private static void CopyQuery(PerfQueryRow into, PerfQueryRow from)
    {
        into.DatabaseName = from.DatabaseName;
        into.ExecutionCount = from.ExecutionCount;
        into.TotalCpuMs = from.TotalCpuMs;
        into.AvgCpuMs = from.AvgCpuMs;
        into.TotalDurationMs = from.TotalDurationMs;
        into.AvgDurationMs = from.AvgDurationMs;
        into.MaxDurationMs = from.MaxDurationMs;
        into.TotalLogicalReads = from.TotalLogicalReads;
        into.AvgLogicalReads = from.AvgLogicalReads;
        into.TotalLogicalWrites = from.TotalLogicalWrites;
        into.TotalPhysicalReads = from.TotalPhysicalReads;
        into.CreationTime = from.CreationTime;
        into.LastExecutionTime = from.LastExecutionTime;
        into.StatementText = from.StatementText;
        into.QueryHash = from.QueryHash;
    }

    private static void CopyFile(PerfFileRow into, PerfFileRow from)
    {
        into.DatabaseName = from.DatabaseName;
        into.LogicalName = from.LogicalName;
        into.PhysicalName = from.PhysicalName;
        into.FileType = from.FileType;
        into.ReadsDelta = from.ReadsDelta;
        into.WritesDelta = from.WritesDelta;
        into.BytesReadDelta = from.BytesReadDelta;
        into.BytesWrittenDelta = from.BytesWrittenDelta;
        into.SizeOnDiskBytes = from.SizeOnDiskBytes;
        into.ReadLatencyMs = from.ReadLatencyMs;
        into.WriteLatencyMs = from.WriteLatencyMs;
    }

    // -------------------------------------------------------------------------------------------------
    // Copy, show SQL, open as query
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
    /// Opens the selected row's full SQL. The grids show a single collapsed line because a fixed-height row
    /// cannot show more, but the whole batch is what you need once you have found the offending statement.
    /// </summary>
    private void ShowSql_Click(object sender, RoutedEventArgs e)
    {
        object selected = ActiveGrid()?.SelectedItem;
        string sql = null;

        if (selected is PerfRequestRow request) sql = FirstNonEmpty(request.BatchText, request.StatementText);
        else if (selected is PerfBlockingRow blocking) sql = blocking.StatementText;
        else if (selected is PerfQueryRow query) sql = query.StatementText;

        if (string.IsNullOrWhiteSpace(sql))
        {
            StatusText.Text = "Select a row with SQL text first.";
            return;
        }

        OpenTextInNewQueryWindow(sql);
    }

    private static string FirstNonEmpty(params string[] candidates)
    {
        foreach (var candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate;

        return null;
    }

    /// <summary>
    /// Resolves a cell's displayed text for the clipboard. Template columns have no binding to read, so their
    /// underlying property comes from the column's SortMemberPath.
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
    /// Tab positions, named because both switches below key off them. They were bare literals until the Server
    /// info tab was added — the same shape that bit the Always On monitor, where inserting a tab in the middle
    /// silently repointed both "copy this tab" and "show me this tab's SQL" at the wrong one.
    /// </summary>
    private const int TabLive = 0;
    private const int TabActivity = 1;
    private const int TabBlocking = 2;
    private const int TabWaits = 3;
    private const int TabQueries = 4;
    private const int TabFiles = 5;
    private const int TabServerInfo = 6;

    private DataGrid ActiveGrid()
    {
        switch (Tabs.SelectedIndex)
        {
            case TabLive: return TopWaitsGrid;
            case TabActivity: return ActivityGrid;
            case TabBlocking: return BlockingGrid;
            case TabWaits: return WaitsGrid;
            case TabQueries: return QueriesGrid;
            case TabFiles: return FilesGrid;
            case TabServerInfo: return ServerInfoGrid;
            default: return null;
        }
    }

    private void OpenAsQuery_Click(object sender, RoutedEventArgs e)
    {
        string sql = SqlForActiveTab();
        if (sql == null) { StatusText.Text = "No query backs this tab."; return; }

        OpenTextInNewQueryWindow(sql);
    }

    /// <summary>The T-SQL behind the active tab, so the dashboard's numbers can be reproduced by hand.</summary>
    private string SqlForActiveTab()
    {
        bool includeBenign = BenignWaitsCheck.IsChecked == true;

        switch (Tabs.SelectedIndex)
        {
            case TabLive: return PerfQueryService.VitalsSql + PerfQueryService.WaitsSql(includeBenign) + PerfQueryService.FileStatsSql;
            case TabActivity:
            case TabBlocking: return PerfQueryService.RequestsSql;
            case TabWaits: return PerfQueryService.WaitsSql(includeBenign);
            case TabQueries:
                return "DECLARE @top int = " + TopCounts[Clamp(TopCountCombo.SelectedIndex, TopCounts.Length)] + ";" + Environment.NewLine
                         + PerfQueryService.TopQueriesSql(Metrics[Clamp(MetricCombo.SelectedIndex, Metrics.Length)].Metric);
            case TabFiles: return PerfQueryService.FileStatsSql;

            // Rendered against a capability set that claims every optional column, so what opens is the full
            // statement rather than whatever this particular instance was found to support.
            case TabServerInfo: return PerfServerInfoQuery.Sql(PerfServerInfoQuery.Capabilities.All);
            default: return null;
        }
    }

    /// <summary>
    /// Writes the text to a temp .sql file and opens it as a query window, for T-SQL highlighting and F5.
    /// Mirrors the Script Library, SQL History and Always On tool windows.
    /// </summary>
    private void OpenTextInNewQueryWindow(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
        if (dte == null) { StatusText.Text = "No DTE available."; return; }

        try
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SQLExtended", "PerfMonitor");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"Perf_{DateTime.Now:yyyyMMdd_HHmmss_fff}.sql");
            System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));

            dte.ItemOperations.OpenFile(path, Constants.vsViewKindTextView);
            StatusText.Text = "Opened in a new query window.";
        }
        catch (Exception ex)
        {
            try { Clipboard.SetText(text); } catch { }
            StatusText.Text = $"Could not open a query window ({ex.Message}). Copied the SQL to the clipboard instead.";
        }
    }
}
