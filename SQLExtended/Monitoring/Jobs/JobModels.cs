using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SQLExtended.Monitoring.Jobs;

/// <summary>
/// Minimal change-notification base, matching <c>AgRowBase</c>. The jobs grid refreshes on a timer and is
/// merged in place by job_id (see <see cref="RowMerge"/>) rather than rebuilt, so rows must raise
/// PropertyChanged for new values to reach the UI without resetting selection or scroll position.
/// </summary>
internal abstract class JobRowBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>The outcome codes msdb.dbo.sysjobhistory.run_status uses.</summary>
internal enum JobRunOutcome
{
    Failed = 0,
    Succeeded = 1,
    Retry = 2,
    Cancelled = 3,
    InProgress = 4,
    Unknown = 99
}

/// <summary>
/// One Agent job: static metadata from sysjobs, live state from sysjobactivity, and last/average run
/// figures from sysjobhistory. Assembled by <see cref="JobQueryService"/> from three result sets keyed
/// on job_id.
/// </summary>
internal sealed class JobRow : JobRowBase
{
    public Guid JobId { get; set; }
    public string Key => JobId.ToString();

    /// <summary>
    /// Notifying, and copied by the merge, even though rows are keyed on job_id: a job renamed on the server keeps
    /// the same id, so a plain auto-property would leave the grid showing the old name until SSMS restarted.
    /// </summary>
    private string _name; public string Name { get => _name; set => Set(ref _name, value); }

    // --- sysjobs metadata ---
    private bool _isEnabled; public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }
    private string _category; public string Category { get => _category; set => Set(ref _category, value); }
    private string _owner; public string Owner { get => _owner; set => Set(ref _owner, value); }
    private string _description; public string Description { get => _description; set => Set(ref _description, value); }
    private DateTime? _dateCreated; public DateTime? DateCreated { get => _dateCreated; set => Set(ref _dateCreated, value); }
    private int _stepCount; public int StepCount { get => _stepCount; set { Set(ref _stepCount, value); Raise(nameof(CurrentStep)); } }

    /// <summary>
    /// The job's category is on the hidden list (SSRS subscriptions by default). Set by the query service from
    /// the configured list; the grid's CollectionView filter reads it, so toggling visibility costs no round trip.
    /// </summary>
    private bool _isHiddenCategory; public bool IsHiddenCategory { get => _isHiddenCategory; set => Set(ref _isHiddenCategory, value); }

    /// <summary>Operator notified on the job's e-mail notification, and the address it would go to.</summary>
    private string _notifyOperator; public string NotifyOperator { get => _notifyOperator; set { Set(ref _notifyOperator, value); Raise(nameof(NotificationEmail)); } }
    private string _notifyEmailAddress; public string NotifyEmailAddress { get => _notifyEmailAddress; set { Set(ref _notifyEmailAddress, value); Raise(nameof(NotificationEmail)); } }

    /// <summary>
    /// sysjobs.notify_level_email: 0 never, 1 on success, 2 on failure, 3 always. An operator with level 0
    /// is configured but will never be mailed, which is worth showing rather than hiding.
    /// </summary>
    private int _notifyLevelEmail; public int NotifyLevelEmail { get => _notifyLevelEmail; set { Set(ref _notifyLevelEmail, value); Raise(nameof(NotificationEmail)); } }

    /// <summary>The notification column: address plus when it fires, or blank when no operator is set.</summary>
    public string NotificationEmail
    {
        get
        {
            string target = !string.IsNullOrWhiteSpace(NotifyEmailAddress) ? NotifyEmailAddress : NotifyOperator;
            if (string.IsNullOrWhiteSpace(target)) return null;

            switch (NotifyLevelEmail)
            {
                case 1: return target + " (on success)";
                case 2: return target + " (on failure)";
                case 3: return target + " (always)";
                default: return target + " (never — notify level 0)";
            }
        }
    }

    // --- sysjobactivity: live state ---
    private DateTime? _startExecutionDate; public DateTime? StartExecutionDate { get => _startExecutionDate; set { Set(ref _startExecutionDate, value); Raise(nameof(IsRunning)); Raise(nameof(Status)); } }
    private DateTime? _stopExecutionDate; public DateTime? StopExecutionDate { get => _stopExecutionDate; set { Set(ref _stopExecutionDate, value); Raise(nameof(IsRunning)); Raise(nameof(Status)); } }
    private DateTime? _nextRunDate; public DateTime? NextRunDate { get => _nextRunDate; set => Set(ref _nextRunDate, value); }
    private int? _currentStepId; public int? CurrentStepId { get => _currentStepId; set { Set(ref _currentStepId, value); Raise(nameof(CurrentStep)); } }
    private string _currentStepName; public string CurrentStepName { get => _currentStepName; set { Set(ref _currentStepName, value); Raise(nameof(CurrentStep)); } }

    /// <summary>
    /// Seconds the current execution has been running, computed server-side against the server's own clock
    /// (<c>GETDATE()</c>) rather than the client's — the two can differ by minutes across time zones or drift,
    /// and a negative elapsed reads as a bug.
    /// </summary>
    private int? _elapsedSeconds; public int? ElapsedSeconds { get => _elapsedSeconds; set => Set(ref _elapsedSeconds, value); }

    public bool IsRunning => StartExecutionDate != null && StopExecutionDate == null;

    /// <summary>"3 of 7 — Load staging", or blank when the job is not running.</summary>
    public string CurrentStep
    {
        get
        {
            if (!IsRunning || CurrentStepId == null) return null;
            string position = StepCount > 0 ? $"{CurrentStepId} of {StepCount}" : CurrentStepId.ToString();
            return string.IsNullOrWhiteSpace(CurrentStepName) ? position : $"{position} — {CurrentStepName}";
        }
    }

    /// <summary>
    /// Display status. Deliberately not taken from sysjobactivity alone: a disabled job with a schedule still
    /// has activity rows, and reading "Idle" next to a job that will never fire is misleading.
    /// </summary>
    public string Status
    {
        get
        {
            if (IsRunning) return "Running";
            return IsEnabled ? "Idle" : "Disabled";
        }
    }

    // --- sysjobhistory: last run and average ---
    private JobRunOutcome _lastRunOutcome = JobRunOutcome.Unknown;
    public JobRunOutcome LastRunOutcome { get => _lastRunOutcome; set { Set(ref _lastRunOutcome, value); Raise(nameof(LastRunOutcomeText)); Raise(nameof(IsFailed)); Raise(nameof(IsWarning)); } }

    private DateTime? _lastRunDate; public DateTime? LastRunDate { get => _lastRunDate; set => Set(ref _lastRunDate, value); }
    private int? _lastRunDurationSeconds; public int? LastRunDurationSeconds { get => _lastRunDurationSeconds; set => Set(ref _lastRunDurationSeconds, value); }
    private double? _averageDurationSeconds; public double? AverageDurationSeconds { get => _averageDurationSeconds; set => Set(ref _averageDurationSeconds, value); }
    private string _lastRunMessage; public string LastRunMessage { get => _lastRunMessage; set => Set(ref _lastRunMessage, value); }

    public string LastRunOutcomeText => OutcomeText(LastRunOutcome);

    /// <summary>Red row tint — the job's last run failed. Picked up by DarkGridRow's IsFailed trigger.</summary>
    public bool IsFailed => LastRunOutcome == JobRunOutcome.Failed;

    /// <summary>Amber row tint — retried or cancelled. Mutually exclusive with <see cref="IsFailed"/>.</summary>
    public bool IsWarning => !IsFailed && (LastRunOutcome == JobRunOutcome.Retry || LastRunOutcome == JobRunOutcome.Cancelled);

    internal static string OutcomeText(JobRunOutcome outcome)
    {
        switch (outcome)
        {
            case JobRunOutcome.Failed: return "Failed";
            case JobRunOutcome.Succeeded: return "Succeeded";
            case JobRunOutcome.Retry: return "Retry";
            case JobRunOutcome.Cancelled: return "Cancelled";
            case JobRunOutcome.InProgress: return "In progress";
            default: return null;
        }
    }
}

/// <summary>One step of a job, from msdb.dbo.sysjobsteps. Loaded on demand for the selected job.</summary>
internal sealed class JobStepRow
{
    public int StepId { get; set; }
    public string StepName { get; set; }
    public string Subsystem { get; set; }
    public string DatabaseName { get; set; }
    public string ProxyName { get; set; }
    public string OnSuccessAction { get; set; }
    public string OnFailAction { get; set; }
    public int RetryAttempts { get; set; }
    public int RetryIntervalMinutes { get; set; }
    public string Command { get; set; }

    public JobRunOutcome LastRunOutcome { get; set; } = JobRunOutcome.Unknown;
    public DateTime? LastRunDate { get; set; }
    public int? LastRunDurationSeconds { get; set; }

    public string LastRunOutcomeText => JobRow.OutcomeText(LastRunOutcome);
    public bool IsFailed => LastRunOutcome == JobRunOutcome.Failed;
    public bool IsWarning => !IsFailed && (LastRunOutcome == JobRunOutcome.Retry || LastRunOutcome == JobRunOutcome.Cancelled);
}

/// <summary>One row of msdb.dbo.sysjobhistory — a job-level summary (step 0) or an individual step's run.</summary>
internal sealed class JobHistoryRow
{
    public DateTime? RunDate { get; set; }
    public int StepId { get; set; }
    public string StepName { get; set; }
    public JobRunOutcome RunStatus { get; set; } = JobRunOutcome.Unknown;
    public int DurationSeconds { get; set; }
    public int RetriesAttempted { get; set; }
    public string ServerName { get; set; }
    public string Message { get; set; }

    /// <summary>Step 0 is the job-level summary row Agent writes after the last step.</summary>
    public bool IsJobSummary => StepId == 0;

    public string StepLabel => IsJobSummary ? "(job outcome)" : $"{StepId} — {StepName}";
    public string RunStatusText => JobRow.OutcomeText(RunStatus);
    public bool IsFailed => RunStatus == JobRunOutcome.Failed;
    public bool IsWarning => !IsFailed && (RunStatus == JobRunOutcome.Retry || RunStatus == JobRunOutcome.Cancelled);
}

/// <summary>Everything one poll cycle collected. Assembled off the UI thread, then merged into the grid.</summary>
internal sealed class JobsSnapshot
{
    public List<JobRow> Jobs { get; } = new List<JobRow>();

    public string ServerName { get; set; }

    /// <summary>The login this poll ran as, per <c>SUSER_SNAME()</c>. Shown beside the server in the header.</summary>
    public string LoginName { get; set; }
    public DateTime CollectedAtLocal { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// How many sections this poll read and how many of those failed, reported next to the timing. What the window
    /// covers varies with the release and the login's rights, so "3 sections in 212 ms" says considerably more
    /// about an empty column than the duration alone.
    /// </summary>
    public int SectionsRead { get; set; }

    public int SectionsFailed { get; set; }

    /// <summary>
    /// Per-section failures and the permission note. Each result set is read independently so an unexpected
    /// difference in one msdb table degrades a few columns instead of blanking the dashboard.
    /// </summary>
    public List<string> Warnings { get; } = new List<string>();

    /// <summary>Set when Agent is not installed or msdb is unreadable — the UI shows this instead of an empty grid.</summary>
    public string UnavailableReason { get; set; }
    public bool IsAvailable => UnavailableReason == null;
}
