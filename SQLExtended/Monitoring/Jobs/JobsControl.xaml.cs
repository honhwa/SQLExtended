using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
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
using System.Windows.Input;
using System.Windows.Threading;

namespace SQLExtended.Monitoring.Jobs;

/// <summary>
/// The SQL Server Agent jobs dashboard. Pinned to the connection it was opened from (forced to msdb), polls
/// sysjobs / sysjobactivity / sysjobhistory on a timer, and loads per-job steps and history on demand.
///
/// <para><b>This window does not follow the active query window.</b> Agent work is per-instance and long-running:
/// you leave the window up watching one server's overnight jobs while editing queries against another, and a
/// dashboard that silently re-pointed itself would mean every reading — and worse, every Run now / Stop / Enable —
/// landed on whichever editor last had focus. So the connection is captured once, when the window is opened, and
/// the window keeps it until closed. The tool window is registered <c>MultiInstances</c> so one window per server
/// can be open at the same time; see <see cref="MonitorWindows"/> for the matching and reuse rules, shared with the
/// other three dashboards.</para>
///
/// Threading matches the other two dashboards: collection runs entirely on a background thread and only the
/// merge into the bound collection happens on the UI thread. Polls never overlap — a slow server makes the
/// interval effectively longer rather than queueing work up behind itself.
///
/// Category and name filtering are applied through the grid's <see cref="ICollectionView"/> rather than in the
/// WHERE clause. Job counts are small, and filtering client-side means the "Show hidden categories" toggle and
/// every keystroke in the filter box are instant instead of a round trip — and the status line can report how
/// many rows the filter is holding back, which a server-side filter cannot.
/// </summary>
public partial class JobsControl : UserControl
{
    private readonly ObservableCollection<JobRow> _jobs = new ObservableCollection<JobRow>();
    private readonly ICollectionView _jobsView;
    private readonly DispatcherTimer _timer;

    private AsyncPackage _package;
    private CancellationTokenSource _inFlight;
    private bool _polling;

    // A poll finished against a connection this window is no longer pinned to; its replacement runs as soon as the
    // in-flight one has unwound (BeginRefresh refuses to overlap polls).
    private bool _restartAfterPoll;

    // The connection actually used for the last successful poll, reused by the on-demand detail loads.
    private string _monitorConnection;

    // The connection this window is pinned to, captured when it was opened. Every poll uses this and never asks
    // SSMS what the active editor is connected to — see the class remarks.
    private readonly MonitorPin _pin = new MonitorPin();

    // SERVERPROPERTY('ServerName') from the last poll. The Job Properties dialog needs the name SMO knows the
    // instance by for its URN, which is not necessarily the connection string's Data Source.
    private string _serverName;

    // SUSER_SNAME() from the last poll, for the header. Until one arrives the connection string's own answer stands
    // in — see MonitorPin.LoginFor. Worth having in view here: what this login may start, stop or disable is a
    // different set from what it may see.
    private string _loginName;

    // Which job the Steps/History tabs currently show, so switching tabs does not re-query needlessly.
    private Guid _detailJobId;
    private List<JobHistoryRow> _allHistory = new List<JobHistoryRow>();

    // An error the user has to read outlives the next poll — see ShowNotice's sticky parameter.
    private bool _noticeIsSticky;

    // What the banner's Copy button hands over: the message plus whatever long-form detail came with it.
    private string _noticeClipboardText;

    private const int HistoryMaxRows = 1000;
    private static readonly int[] IntervalSeconds = { 5, 10, 15, 30, 60 };

    /// <summary>
    /// The server this window is pinned to, normalised for matching (see <see cref="MonitorWindows.ServerKey"/>).
    /// Null until the window is pinned. <see cref="JobsCommand"/> reads this to decide whether an open window
    /// already covers the server being asked for.
    /// </summary>
    internal string PinnedServerKey => _pin.ServerKey;

    /// <summary>
    /// Set by the hosting tool window so the pane's caption can name the server. With several windows open the
    /// caption is the only thing distinguishing their tabs, so it is not optional decoration.
    /// </summary>
    internal Action<string> CaptionChanged;

    public JobsControl()
    {
        InitializeComponent();

        JobGrid.ItemsSource = _jobs;
        _jobsView = CollectionViewSource.GetDefaultView(_jobs);
        _jobsView.Filter = JobFilter;

        foreach (int seconds in IntervalSeconds)
            IntervalCombo.Items.Add(seconds + "s");
        IntervalCombo.SelectedIndex = NearestIntervalIndex(SQLExtendedSettings.Current.JobsRefreshSeconds);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(IntervalSeconds[IntervalCombo.SelectedIndex]) };
        _timer.Tick += (s, e) => BeginRefresh(userInitiated: false);

        Unloaded += OnUnloaded;
    }

    internal void SetPackage(AsyncPackage package) => _package = package;

    // -------------------------------------------------------------------------------------------------
    // Pinning
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Binds this window to one server for the rest of its life and starts a collection. UI thread only.
    /// </summary>
    /// <param name="connectionString">
    /// Any connection to the instance — the active query window's, or Object Explorer's for the clicked node. It
    /// is re-pointed at msdb here, so callers do not have to care which database it arrived on.
    /// </param>
    /// <param name="serverLabel">
    /// How the caller names the server (the Object Explorer node's name, say). Only used for the caption until the
    /// first poll reports <c>SERVERPROPERTY('ServerName')</c>; null falls back to the connection's Data Source.
    /// </param>
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

        // Re-pinning to a different instance: everything on screen belongs to the old one, and a job_id means
        // nothing over there. Only reachable when the window cap is hit (see JobsCommand), never silently.
        if (_pin.Set(connectionString))
        {
            _jobs.Clear();
            ClearDetail();
            _serverName = null;
            _loginName = null;
            TimingText.Text = "";
        }

        ApplyPinnedChrome(serverLabel);

        // A stale "no connection" or previous server's error must not outlive the new pin.
        _noticeIsSticky = false;
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
            ? "No SQL connection to pin this window to. Open or focus a query window connected to the server whose Agent jobs you want, then press Refresh."
            : $"Could not get a connection for {requestedServer}. Open a query window against it, then press Refresh.", sticky: true);

        StatusText.Text = "Not connected.";
        CaptionChanged?.Invoke(null);
    }

    /// <summary>
    /// Says that this window was re-pointed at a different server because the window cap was reached, rather than
    /// a new one being opened. Sticky: the user asked for a server and got an existing window's contents replaced,
    /// which they have to know about — the grid alone gives no hint that anything was displaced.
    /// </summary>
    internal void ShowRepinnedNotice(int maxWindows)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        ShowNotice($"This window was re-pinned to {_pin.Target} — {maxWindows} Agent Jobs windows were already open, "
                 + "which is the maximum. Close one to open a server in its own window.", sticky: true);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value;
        return null;
    }

    private static int NearestIntervalIndex(int seconds)
    {
        int best = 0;
        for (int i = 1; i < IntervalSeconds.Length; i++)
            if (Math.Abs(IntervalSeconds[i] - seconds) < Math.Abs(IntervalSeconds[best] - seconds)) best = i;
        return best;
    }

    // -------------------------------------------------------------------------------------------------
    // Toolbar
    // -------------------------------------------------------------------------------------------------

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        // Pressing Refresh is the user acknowledging whatever the banner said.
        _noticeIsSticky = false;
        BeginRefresh(userInitiated: true);
    }

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
    }

    // -------------------------------------------------------------------------------------------------
    // Filtering
    // -------------------------------------------------------------------------------------------------

    private void Filter_Changed(object sender, TextChangedEventArgs e) => RefreshFilter();

    private void ShowHidden_Changed(object sender, RoutedEventArgs e) => RefreshFilter();

    private void RefreshFilter()
    {
        if (_jobsView == null) return;
        _jobsView.Refresh();
        UpdateStatus();
    }

    private bool JobFilter(object item)
    {
        if (!(item is JobRow job)) return false;

        if (job.IsHiddenCategory && ShowHiddenCheck?.IsChecked != true) return false;

        string text = FilterBox?.Text;
        if (string.IsNullOrWhiteSpace(text)) return true;

        text = text.Trim();
        return Contains(job.Name, text) || Contains(job.Category, text) || Contains(job.Owner, text);
    }

    private static bool Contains(string haystack, string needle) =>
        haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

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
            // Refresh. On the timer they would replace a settled summary with a flicker of step text and re-merge
            // the grid twice per tick, which is cost and noise for a window already populated.
            var progress = userInitiated
                ? new MonitorStatusReporter(package.JoinableTaskFactory, cts.Token, text => StatusText.Text = text)
                : null;

            if (userInitiated) StatusText.Text = "Connecting to msdb…";

            if (!_pin.IsPinned)
            {
                // Unpinned: the window was opened with nothing to pin to, so Refresh means "use the active query
                // window now, and keep it" — which is exactly what ShowNoConnection told the user to do.
                // Connection discovery must happen on the UI thread; it reflects into SSMS's editor internals.
                string baseConnection = ConnectionHelper.GetActiveConnectionString();
                if (string.IsNullOrEmpty(baseConnection))
                {
                    ShowNoConnection(null);
                    return;
                }

                _pin.Set(baseConnection);
                _noticeIsSticky = false;
                ShowNotice(null);
                ApplyPinnedChrome(null);
            }

            // The pinned connection, not whatever the active editor happens to be on. Captured here on the UI
            // thread so the poll can be compared against it afterwards and discarded if the pin moved underneath.
            string connection = JobQueryService.BuildMonitorConnectionString(_pin.Connection);

            var settings = SQLExtendedSettings.Current;
            var hidden = JobValueParser.ParseCategories(settings.JobsHiddenCategories);
            int averageSamples = settings.JobsAverageSampleRuns;

            // Shows the job list as soon as it and the current activity are in, rather than after the run-history
            // read that only fills in two of its columns. The collection awaits this, so nothing is writing to the
            // snapshot while the grid reads it — see MonitorPlan.
            Func<JobsSnapshot, Task> jobsReady = !userInitiated ? (Func<JobsSnapshot, Task>)null : async partial =>
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(cts.Token);

                if (string.Equals(JobQueryService.BuildMonitorConnectionString(_pin.Connection), connection, StringComparison.Ordinal))
                    ApplyJobs(partial, stillCollecting: true);

                await TaskScheduler.Default;
            };

            // Everything from here is off the UI thread.
            await TaskScheduler.Default;
            var snapshot = await JobQueryService.CollectAsync(connection, hidden, averageSamples, progress, jobsReady, cts.Token).ConfigureAwait(false);

            await package.JoinableTaskFactory.SwitchToMainThreadAsync(cts.Token);

            // The window was re-pinned while this poll was in flight, so the snapshot describes a server this
            // window no longer shows. Applying it would put another instance's jobs under the new caption. The
            // re-pin's own BeginRefresh was refused as "already running", so the replacement is queued here —
            // otherwise the new server's grid stays empty until the next timer tick, or forever with Auto off.
            if (!string.Equals(JobQueryService.BuildMonitorConnectionString(_pin.Connection), connection, StringComparison.Ordinal))
            {
                _restartAfterPoll = true;
                return;
            }

            // A different server invalidates the detail tabs — the selected job_id means nothing over there.
            if (!string.Equals(_monitorConnection, connection, StringComparison.Ordinal))
                ClearDetail();

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
            ShowNotice(ex.Message, tooltip: ex.ToString(), sticky: true);

            // A failing server on a timer would otherwise log and retry forever.
            if (AutoRefreshCheck.IsChecked == true)
            {
                AutoRefreshCheck.IsChecked = false;
                StatusText.Text = "Refresh failed, auto-refresh stopped: " + ex.Message;
            }

            ActivityLogHelper.LogError(package, "SQLExtended Agent Jobs", "Refresh failed: " + ex);
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
    /// Merges a completed snapshot into the bound collection and updates the chrome. UI thread only.
    ///
    /// Split so the grid can be drawn part-way through a collection — see <see cref="ApplyJobs"/>. Both halves run
    /// here in order, so a poll that had no early paint (every timer tick) ends up in exactly the state it did
    /// before the split.
    /// </summary>
    private void Apply(JobsSnapshot snapshot)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        ApplyJobs(snapshot, stillCollecting: false);
        if (!snapshot.IsAvailable) return;

        ShowNotice(snapshot.Warnings.Count == 0 ? null : "Note — " + string.Join("; ", snapshot.Warnings) + ".");
        TimingText.Text = BuildTiming(snapshot);
    }

    /// <summary>
    /// The job grid and the status line.
    ///
    /// <para>Called twice on a user-initiated poll — once the moment the jobs and their activity are in (with
    /// <paramref name="stillCollecting"/> set, from <c>JobQueryService</c>'s hook) and once at the end, by which
    /// time the run history has filled in the last-run and average columns on the same row instances. It is
    /// therefore written to be idempotent: a merge, a view refresh and text, nothing that accumulates.</para>
    /// </summary>
    private void ApplyJobs(JobsSnapshot snapshot, bool stillCollecting)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // The instance's own name for itself, which the caption and header prefer over the connect target — behind
        // a listener or a CNAME they differ, and with several windows open the real name is what tells them apart.
        _serverName = snapshot.ServerName;
        _loginName = snapshot.LoginName;
        ApplyPinnedChrome(null);

        if (!snapshot.IsAvailable)
        {
            _jobs.Clear();
            ClearDetail();

            // Sticky: "Agent is not installed here" is a standing condition, not a transient warning, and it must
            // not be displaced by the next poll finding nothing to report.
            ShowNotice(snapshot.UnavailableReason, sticky: true);
            StatusText.Text = "Nothing to monitor.";
            TimingText.Text = "";

            if (AutoRefreshCheck.IsChecked == true)
                AutoRefreshCheck.IsChecked = false;
            return;
        }

        RowMerge.Apply(_jobs, snapshot.Jobs, r => r.Key, CopyJob);
        _jobsView.Refresh();

        UpdateStatus(snapshot, stillCollecting);
    }

    /// <summary>
    /// When the snapshot was taken, how many sections it read and how long it took. The section count is here
    /// because what this window can cover varies with the login's rights: "3 sections" beside a column of dashes
    /// says something a duration on its own does not.
    /// </summary>
    private static string BuildTiming(JobsSnapshot snapshot)
    {
        string sections = snapshot.SectionsFailed > 0
            ? $"{snapshot.SectionsRead - snapshot.SectionsFailed} of {snapshot.SectionsRead} sections"
            : $"{snapshot.SectionsRead} sections";

        return $"{snapshot.CollectedAtLocal:HH:mm:ss} · {sections} · {snapshot.Duration.TotalMilliseconds:N0} ms";
    }

    /// <param name="stillCollecting">
    /// Set while only the job list and its activity are in. Says so, because the grid looks complete at that point
    /// while its last-run and average columns are still empty.
    /// </param>
    private void UpdateStatus(JobsSnapshot snapshot = null, bool stillCollecting = false)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_jobs.Count == 0 && snapshot == null) return;

        int shown = _jobsView.Cast<object>().Count();
        int running = _jobs.Count(j => j.IsRunning);
        int failed = _jobs.Count(j => j.IsFailed);
        int disabled = _jobs.Count(j => !j.IsEnabled);

        var parts = new List<string> { $"{shown} of {_jobs.Count} job(s)" };
        if (running > 0) parts.Add($"{running} running");
        if (failed > 0) parts.Add($"{failed} last failed");
        if (disabled > 0) parts.Add($"{disabled} disabled");

        string status = string.Join(" · ", parts);

        int hiddenCount = _jobs.Count(j => j.IsHiddenCategory);
        if (hiddenCount > 0 && ShowHiddenCheck.IsChecked != true)
            status += $"  ({hiddenCount} hidden by category — tick \"Show hidden categories\" to include them.)";

        if (stillCollecting) status += "   Still reading the run history…";

        StatusText.Text = status;
    }

    /// <param name="tooltip">
    /// Optional long-form detail (a full stack trace). The banner has to stay short enough to read at a glance,
    /// but for a reflection failure into SSMS internals the trace is the only thing that identifies the cause,
    /// and the ActivityLog is not written unless SSMS was launched with /log — so it hangs off the banner.
    /// </param>
    /// <param name="sticky">
    /// Keep the message until the user does something about it. Without this the next poll — 15 seconds away, or
    /// immediately if the action triggered a refresh — replaces it with that snapshot's (usually empty) warning
    /// text, and an error the user needed to read flashes up and vanishes.
    /// </param>
    private void ShowNotice(string text, string tooltip = null, bool sticky = false)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // A non-sticky message must not paint over a sticky one that is still standing.
        if (_noticeIsSticky && !sticky) return;

        _noticeIsSticky = sticky && !string.IsNullOrWhiteSpace(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            NoticeBorder.Visibility = Visibility.Collapsed;
            NoticeText.Text = "";
            NoticeText.ToolTip = null;
            _noticeClipboardText = null;
            return;
        }

        NoticeText.Text = text;

        // A raw string tooltip renders as one unwrapped line and a stack trace runs off the screen.
        NoticeText.ToolTip = string.IsNullOrWhiteSpace(tooltip)
            ? null
            : new TextBlock { Text = tooltip, TextWrapping = TextWrapping.Wrap, MaxWidth = 700, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 11 };

        // Assembled here rather than at copy time so the button always hands over the detail that belongs to the
        // message currently on screen, and includes the server — the first thing anyone reading a pasted error asks.
        _noticeClipboardText = BuildNoticeClipboardText(text, tooltip);

        NoticeBorder.Visibility = Visibility.Visible;
    }

    private string BuildNoticeClipboardText(string text, string detail)
    {
        var builder = new StringBuilder();
        builder.AppendLine("SQLExtended Agent Jobs");
        builder.AppendLine("Server: " + (string.IsNullOrEmpty(_serverName) ? "(unknown)" : _serverName));
        builder.AppendLine("When:   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine();
        builder.AppendLine(text);

        if (!string.IsNullOrWhiteSpace(detail))
        {
            builder.AppendLine();
            builder.AppendLine("--- detail ---");
            builder.AppendLine(detail);
        }

        return builder.ToString();
    }

    private void NoticeCopy_Click(object sender, RoutedEventArgs e)
    {
        string payload = _noticeClipboardText;
        if (string.IsNullOrEmpty(payload)) return;

        try
        {
            Clipboard.SetText(payload);
            StatusText.Text = "Copied the message and its detail to the clipboard.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Copy failed: " + ex.Message;
        }
    }

    private void NoticeClose_Click(object sender, RoutedEventArgs e)
    {
        // Dismissing is the acknowledgement, so the sticky flag goes with it.
        _noticeIsSticky = false;
        ShowNotice(null);
    }

    /// <summary>Row copier for the in-place merge. JobId is excluded — it is what matched the rows.</summary>
    private static void CopyJob(JobRow into, JobRow from)
    {
        into.Name = from.Name;
        into.IsEnabled = from.IsEnabled;
        into.Category = from.Category;
        into.IsHiddenCategory = from.IsHiddenCategory;
        into.Owner = from.Owner;
        into.Description = from.Description;
        into.DateCreated = from.DateCreated;
        into.StepCount = from.StepCount;
        into.NotifyLevelEmail = from.NotifyLevelEmail;
        into.NotifyOperator = from.NotifyOperator;
        into.NotifyEmailAddress = from.NotifyEmailAddress;

        into.StartExecutionDate = from.StartExecutionDate;
        into.StopExecutionDate = from.StopExecutionDate;
        into.NextRunDate = from.NextRunDate;
        into.CurrentStepId = from.CurrentStepId;
        into.CurrentStepName = from.CurrentStepName;
        into.ElapsedSeconds = from.ElapsedSeconds;

        into.LastRunOutcome = from.LastRunOutcome;
        into.LastRunDate = from.LastRunDate;
        into.LastRunDurationSeconds = from.LastRunDurationSeconds;
        into.AverageDurationSeconds = from.AverageDurationSeconds;
        into.LastRunMessage = from.LastRunMessage;
    }

    // -------------------------------------------------------------------------------------------------
    // Run now / Stop / Enable / Disable
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// WPF does not select the row under a right-click, so without this the context menu would act on whatever
    /// was selected before — which for destructive items is the wrong job, silently.
    /// </summary>
    private void JobGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is JobRow) JobGrid.SelectedItem = row.Item;
    }

    private void JobContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var job = JobGrid.SelectedItem as JobRow;
        bool has = job != null;

        // A disabled job can still be started by hand, so Run Now is gated on "not already running" only.
        RunNowItem.IsEnabled = has && !job.IsRunning;
        StopItem.IsEnabled = has && job.IsRunning;
        EnableItem.IsEnabled = has && !job.IsEnabled;
        DisableItem.IsEnabled = has && job.IsEnabled;
        PropertiesItem.IsEnabled = has;
    }

    private void RunNow_Click(object sender, RoutedEventArgs e) => RunAction(JobAction.Start);
    private void Stop_Click(object sender, RoutedEventArgs e) => RunAction(JobAction.Stop);
    private void Enable_Click(object sender, RoutedEventArgs e) => RunAction(JobAction.Enable);
    private void Disable_Click(object sender, RoutedEventArgs e) => RunAction(JobAction.Disable);

    /// <summary>
    /// Confirms, then performs one of the four state-changing actions and refreshes.
    ///
    /// The confirmation is not ceremony: this window follows whatever query window has focus, so the instance it
    /// is pointed at is not always the one the user has in mind, and starting or stopping a production job by a
    /// misclick in a grid is not recoverable. The prompt therefore names both the job and the server.
    /// </summary>
    private void RunAction(JobAction action)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!(JobGrid.SelectedItem is JobRow job)) { StatusText.Text = "Select a job first."; return; }

        var package = _package;
        if (package == null) return;

        string connection = _monitorConnection;
        if (string.IsNullOrEmpty(connection))
        {
            StatusText.Text = "Refresh first so the dashboard knows which server to act on.";
            return;
        }

        if (!Confirm(action, job)) return;

        // Acting on a job supersedes whatever the banner was complaining about.
        _noticeIsSticky = false;
        ShowNotice(null);

        var jobId = job.JobId;
        string jobName = job.Name;
        string verb = JobActionService.Describe(action);

        StatusText.Text = $"{verb}: {jobName}…";

        _ = package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await TaskScheduler.Default;

                switch (action)
                {
                    case JobAction.Start: await JobActionService.StartAsync(connection, jobId, package.DisposalToken).ConfigureAwait(false); break;
                    case JobAction.Stop: await JobActionService.StopAsync(connection, jobId, package.DisposalToken).ConfigureAwait(false); break;
                    case JobAction.Enable: await JobActionService.SetEnabledAsync(connection, jobId, true, package.DisposalToken).ConfigureAwait(false); break;
                    case JobAction.Disable: await JobActionService.SetEnabledAsync(connection, jobId, false, package.DisposalToken).ConfigureAwait(false); break;
                }

                // Starting and stopping are asynchronous inside Agent: sp_start_job returns once the request is
                // accepted, and sysjobactivity catches up a moment later. Refreshing immediately would show the
                // old state and read as "the command did nothing".
                if (action == JobAction.Start || action == JobAction.Stop)
                    await Task.Delay(TimeSpan.FromSeconds(1), package.DisposalToken).ConfigureAwait(false);

                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                StatusText.Text = action == JobAction.Start
                    ? $"Started {jobName}. Agent runs it in the background — watch the Status column."
                    : $"{verb} succeeded: {jobName}.";

                BeginRefresh(userInitiated: false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

                // Typically "job is already running", "job is not running", or a permissions refusal — the
                // server's own wording is more accurate than anything this code could pre-check for.
                StatusText.Text = $"{verb} failed: {ex.Message}";
                ShowNotice($"{verb} failed on {jobName} — {ex.Message}", tooltip: ex.ToString(), sticky: true);
                ActivityLogHelper.LogError(package, "SQLExtended Agent Jobs", $"{verb} failed on {jobName}: {ex}");
            }
        });
    }

    private bool Confirm(JobAction action, JobRow job)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string server = string.IsNullOrEmpty(_serverName) ? "the current server" : _serverName;
        string question;

        switch (action)
        {
            case JobAction.Start:
                question = $"Run the job \"{job.Name}\" now on {server}?";
                break;
            case JobAction.Stop:
                question = $"Stop the running job \"{job.Name}\" on {server}?\r\n\r\n"
                         + "The current step is interrupted; steps already completed are not rolled back.";
                break;
            case JobAction.Enable:
                question = $"Enable the job \"{job.Name}\" on {server}?\r\n\r\nIt will run on its schedule again.";
                break;
            default:
                question = $"Disable the job \"{job.Name}\" on {server}?\r\n\r\nIt will stop running on its schedule.";
                break;
        }

        // DEFBUTTON_SECOND puts the focus on No: for Stop and Disable, Enter should not be the destructive answer.
        int result = VsShellUtilities.ShowMessageBox(_package, question, "SQLExtended — Agent Jobs",
            OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);

        return result == (int)Microsoft.VisualStudio.VSConstants.MessageBoxResult.IDYES;
    }

    // -------------------------------------------------------------------------------------------------
    // Job Properties — hands off to SSMS's own dialog, the same one Object Explorer opens
    // -------------------------------------------------------------------------------------------------

    private void JobGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // MouseDoubleClick fires for the column headers, the scrollbars and the empty area below the rows too,
        // so only act when the click actually landed inside a row.
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) == null) return;

        e.Handled = true;
        OpenJobProperties();
    }

    private void JobGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OpenJobProperties();
    }

    private void JobProperties_Click(object sender, RoutedEventArgs e) => OpenJobProperties();

    private static T FindAncestor<T>(DependencyObject from) where T : DependencyObject
    {
        while (from != null)
        {
            if (from is T match) return match;
            from = from is System.Windows.Media.Visual || from is System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(from)
                : LogicalTreeHelper.GetParent(from);
        }
        return null;
    }

    /// <summary>
    /// Opens SSMS's Job Properties dialog for the selected job, then refreshes so any edit is reflected.
    ///
    /// The dialog is modal and WinForms — it blocks this method until closed, which is why the refresh is
    /// afterwards rather than on a timer tick. Auto-refresh is paused across it: a background poll mutating
    /// the grid underneath a modal dialog buys nothing and the timer would otherwise keep firing.
    /// </summary>
    private void OpenJobProperties()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!(JobGrid.SelectedItem is JobRow job)) { StatusText.Text = "Select a job first."; return; }

        var package = _package;
        if (package == null) return;

        if (string.IsNullOrEmpty(_monitorConnection) || string.IsNullOrEmpty(_serverName))
        {
            StatusText.Text = "Refresh first so the dashboard knows which server the job is on.";
            return;
        }

        bool wasAutoRefreshing = AutoRefreshCheck.IsChecked == true;
        var uiShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
        bool opened = false;

        try
        {
            if (wasAutoRefreshing) _timer.Stop();

            IntPtr owner = IntPtr.Zero;
            uiShell?.GetDialogOwnerHwnd(out owner);
            uiShell?.EnableModeless(0);

            StatusText.Text = $"Opening Job Properties for {job.Name}…";
            JobDialogLauncher.ShowJobProperties(package, _serverName, job.JobId, job.Name, _monitorConnection, owner);
            StatusText.Text = $"Closed Job Properties for {job.Name}.";
            opened = true;
        }
        catch (Exception ex)
        {
            // Undocumented internals: a servicing update that moves one of the types costs this menu item and
            // nothing else. Object Explorer is always still there as the way in.
            StatusText.Text = "Could not open the Job Properties dialog: " + JobDialogLauncher.Innermost(ex).Message;
            ShowNotice($"Could not open SSMS's Job Properties dialog for {job.Name}. "
                     + JobDialogLauncher.DescribeChain(ex)
                     + "  Right-click the job in Object Explorer instead. Hover this message for the full stack trace.",
                       tooltip: ex.ToString(), sticky: true);
            ActivityLogHelper.LogError(package, "SQLExtended Agent Jobs", "Job Properties launch failed: " + ex);
        }
        finally
        {
            uiShell?.EnableModeless(1);
            if (wasAutoRefreshing) _timer.Start();
        }

        // Only on success. Refreshing after a failure was overwriting the error banner with the next poll's
        // (empty) warning text, so the message the user needed flashed up and vanished.
        if (!opened) return;

        // The dialog may have renamed, retimed or disabled the job; the detail tabs are stale for the same reason.
        ClearDetail();
        BeginRefresh(userInitiated: true);
    }

    // -------------------------------------------------------------------------------------------------
    // Steps / History — loaded on demand for the selected job
    // -------------------------------------------------------------------------------------------------

    private void JobGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadDetailIfNeeded();

    private void Tabs_Changed(object sender, SelectionChangedEventArgs e)
    {
        // TabControl bubbles SelectionChanged from the grids inside it; only react to the tabs themselves.
        if (!ReferenceEquals(e.OriginalSource, Tabs)) return;
        LoadDetailIfNeeded();
    }

    private void ClearDetail()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _detailJobId = Guid.Empty;
        _allHistory = new List<JobHistoryRow>();
        if (StepGrid != null) StepGrid.ItemsSource = null;
        if (HistoryGrid != null) HistoryGrid.ItemsSource = null;
    }

    /// <summary>
    /// Loads steps and history for the selected job, but only while one of those tabs is actually showing —
    /// a selection change on the Jobs tab should not fire two more queries the user will never look at.
    /// </summary>
    private void LoadDetailIfNeeded()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (Tabs == null || Tabs.SelectedIndex == 0) return;

        var job = JobGrid?.SelectedItem as JobRow;
        if (job == null)
        {
            StepsHint.Text = "Select a job on the Jobs tab.";
            HistoryHint.Text = "Select a job on the Jobs tab.";
            return;
        }

        if (job.JobId == _detailJobId) return;

        var package = _package;
        if (package == null) return;

        string connection = _monitorConnection;
        if (string.IsNullOrEmpty(connection))
        {
            StepsHint.Text = "Refresh first so the dashboard knows which server to read.";
            HistoryHint.Text = "Refresh first so the dashboard knows which server to read.";
            return;
        }

        _detailJobId = job.JobId;
        int historyDays = SQLExtendedSettings.Current.JobsHistoryDays;

        StepsHint.Text = $"Loading steps for {job.Name}…";
        HistoryHint.Text = $"Loading history for {job.Name}…";

        _ = package.JoinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                await TaskScheduler.Default;
                var steps = await JobQueryService.GetStepsAsync(connection, job.JobId, package.DisposalToken).ConfigureAwait(false);
                var history = await JobQueryService.GetHistoryAsync(connection, job.JobId, historyDays, HistoryMaxRows, package.DisposalToken).ConfigureAwait(false);

                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

                // The user may have moved on while this was in flight; do not overwrite a newer selection.
                if (_detailJobId != job.JobId) return;

                StepGrid.ItemsSource = steps;
                StepsHint.Text = $"{steps.Count} step(s) in {job.Name}.";

                _allHistory = history;
                ApplyHistoryFilter();
                HistoryHint.Text = history.Count == 0
                    ? $"No history for {job.Name} in the last {historyDays} day(s) — Agent's history limits may have purged it."
                    : $"{history.Count} history row(s) for {job.Name} over the last {historyDays} day(s)"
                      + (history.Count >= HistoryMaxRows ? $", capped at {HistoryMaxRows}." : ".");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
                _detailJobId = Guid.Empty;
                StepsHint.Text = ex.Message;
                HistoryHint.Text = ex.Message;
            }
        });
    }

    private void JobOutcomesOnly_Changed(object sender, RoutedEventArgs e) => ApplyHistoryFilter();

    private void ApplyHistoryFilter()
    {
        if (HistoryGrid == null) return;
        bool summaryOnly = JobOutcomesOnlyCheck.IsChecked == true;
        HistoryGrid.ItemsSource = summaryOnly ? _allHistory.Where(h => h.IsJobSummary).ToList() : _allHistory;
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
    /// Resolves a cell's displayed text for the clipboard. Template columns (the coloured outcome cells) have
    /// no binding to read, so their underlying property is looked up by the column's SortMemberPath — which is
    /// set on exactly those columns for this reason.
    /// </summary>
    private static string CellText(DataGridColumn column, object item)
    {
        if (column is DataGridBoundColumn bound && bound.Binding is Binding binding)
            return ValueOf(item, binding.Path?.Path);

        return ValueOf(item, column.SortMemberPath);
    }

    private static string ValueOf(object item, string path)
    {
        if (item == null || string.IsNullOrEmpty(path)) return "";
        var property = item.GetType().GetProperty(path);
        return property == null ? "" : Convert.ToString(property.GetValue(item));
    }

    private DataGrid ActiveGrid()
    {
        switch (Tabs.SelectedIndex)
        {
            case 0: return JobGrid;
            case 1: return StepGrid;
            case 2: return HistoryGrid;
            default: return null;
        }
    }

    private void OpenAsQuery_Click(object sender, RoutedEventArgs e)
    {
        var settings = SQLExtendedSettings.Current;
        string sql;

        switch (Tabs.SelectedIndex)
        {
            case 0:
                sql = JobQueryService.CollectSqlForDisplay(settings.JobsAverageSampleRuns);
                break;
            case 1:
                if (_detailJobId == Guid.Empty) { StatusText.Text = "Select a job first."; return; }
                sql = JobQueryService.StepsSqlForDisplay(_detailJobId);
                break;
            case 2:
                if (_detailJobId == Guid.Empty) { StatusText.Text = "Select a job first."; return; }
                sql = JobQueryService.HistorySqlForDisplay(_detailJobId, settings.JobsHistoryDays, HistoryMaxRows);
                break;
            default:
                StatusText.Text = "No query backs this tab.";
                return;
        }

        OpenTextInNewQueryWindow(sql);
    }

    /// <summary>
    /// Writes the SQL to a temp .sql file and opens it as a query window, so it gets T-SQL highlighting and F5.
    /// Mirrors the approach in the Always On monitor and the Script Library.
    /// </summary>
    private void OpenTextInNewQueryWindow(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
        if (dte == null) { StatusText.Text = "No DTE available."; return; }

        try
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SQLExtended", "AgentJobs");
            System.IO.Directory.CreateDirectory(dir);
            string path = System.IO.Path.Combine(dir, $"AgentJobs_{DateTime.Now:yyyyMMdd_HHmmss_fff}.sql");
            System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));

            dte.ItemOperations.OpenFile(path, EnvDTE.Constants.vsViewKindTextView);
            StatusText.Text = "Opened the tab's query in a new window.";
        }
        catch (Exception ex)
        {
            try { Clipboard.SetText(text); } catch { }
            StatusText.Text = $"Could not open a query window ({ex.Message}). Copied the SQL to the clipboard instead.";
        }
    }
}
