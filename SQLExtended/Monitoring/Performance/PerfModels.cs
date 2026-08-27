using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace SQLExtended.Monitoring.Performance;

/// <summary>Change-notification base, so the grids can be merged in place by <see cref="RowMerge"/> on each poll.</summary>
internal abstract class PerfRowBase : INotifyPropertyChanged
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

/// <summary>
/// One running request, or one sleeping session holding an open transaction. The second case has no row in
/// sys.dm_exec_requests at all, which is exactly why an idle-but-open-transaction head blocker is so easy to
/// miss in Activity Monitor.
/// </summary>
internal sealed class PerfRequestRow : PerfRowBase
{
    public string Key => SessionId.ToString();

    public int SessionId { get; set; }

    private int _blockingSessionId; public int BlockingSessionId { get => _blockingSessionId; set { Set(ref _blockingSessionId, value); Raise(nameof(IsUnhealthy)); } }
    private string _loginName; public string LoginName { get => _loginName; set => Set(ref _loginName, value); }
    private string _hostName; public string HostName { get => _hostName; set => Set(ref _hostName, value); }
    private string _programName; public string ProgramName { get => _programName; set => Set(ref _programName, value); }
    private string _databaseName; public string DatabaseName { get => _databaseName; set => Set(ref _databaseName, value); }
    private string _status; public string Status { get => _status; set => Set(ref _status, value); }
    private string _command; public string Command { get => _command; set => Set(ref _command, value); }
    private string _waitType; public string WaitType { get => _waitType; set => Set(ref _waitType, value); }
    private string _lastWaitType; public string LastWaitType { get => _lastWaitType; set => Set(ref _lastWaitType, value); }
    private string _waitResource; public string WaitResource { get => _waitResource; set => Set(ref _waitResource, value); }
    private long _waitTimeMs; public long WaitTimeMs { get => _waitTimeMs; set { Set(ref _waitTimeMs, value); Raise(nameof(IsWarning)); } }
    private long _cpuTimeMs; public long CpuTimeMs { get => _cpuTimeMs; set => Set(ref _cpuTimeMs, value); }
    private long _elapsedMs; public long ElapsedMs { get => _elapsedMs; set { Set(ref _elapsedMs, value); Raise(nameof(IsWarning)); } }
    private long _logicalReads; public long LogicalReads { get => _logicalReads; set => Set(ref _logicalReads, value); }
    private long _physicalReads; public long PhysicalReads { get => _physicalReads; set => Set(ref _physicalReads, value); }
    private long _writes; public long Writes { get => _writes; set => Set(ref _writes, value); }
    private long _grantedMemoryKb; public long GrantedMemoryKb { get => _grantedMemoryKb; set => Set(ref _grantedMemoryKb, value); }
    private int _openTransactions; public int OpenTransactionCount { get => _openTransactions; set => Set(ref _openTransactions, value); }
    private double _percentComplete; public double PercentComplete { get => _percentComplete; set => Set(ref _percentComplete, value); }
    private DateTime? _startTime; public DateTime? StartTime { get => _startTime; set => Set(ref _startTime, value); }
    private string _statementText; public string StatementText { get => _statementText; set => Set(ref _statementText, value); }
    private string _batchText; public string BatchText { get => _batchText; set => Set(ref _batchText, value); }
    private string _queryHash; public string QueryHash { get => _queryHash; set => Set(ref _queryHash, value); }
    private bool _isRunning; public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }

    /// <summary>Set during blocking analysis: how many sessions this one blocks, directly or transitively.</summary>
    private int _blockedCount; public int BlockedCount { get => _blockedCount; set { Set(ref _blockedCount, value); Raise(nameof(IsUnhealthy)); } }

    /// <summary>Being blocked, or blocking someone, is the thing you opened this tab to find.</summary>
    public bool IsUnhealthy => BlockingSessionId != 0 || BlockedCount > 0;

    /// <summary>Long-running or stuck on a wait, but nothing is blocked — worth a look, not an alarm.</summary>
    public bool IsWarning => !IsUnhealthy && (ElapsedMs > 30000 || WaitTimeMs > 5000);
}

/// <summary>
/// One line of a blocking chain, flattened for display with an indent. Derived client-side from the activity
/// rows rather than queried separately, so the two tabs can never disagree about who is blocking whom.
/// </summary>
internal sealed class PerfBlockingRow : PerfRowBase
{
    public string Key => $"{HeadBlockerSessionId}|{SessionId}";

    public int HeadBlockerSessionId { get; set; }
    public int SessionId { get; set; }

    private int _depth; public int Depth { get => _depth; set { Set(ref _depth, value); Raise(nameof(Indent)); Raise(nameof(IsHeadBlocker)); } }
    private int _blockedCount; public int BlockedCount { get => _blockedCount; set => Set(ref _blockedCount, value); }
    private string _loginName; public string LoginName { get => _loginName; set => Set(ref _loginName, value); }
    private string _hostName; public string HostName { get => _hostName; set => Set(ref _hostName, value); }
    private string _programName; public string ProgramName { get => _programName; set => Set(ref _programName, value); }
    private string _databaseName; public string DatabaseName { get => _databaseName; set => Set(ref _databaseName, value); }
    private string _status; public string Status { get => _status; set => Set(ref _status, value); }
    private string _waitType; public string WaitType { get => _waitType; set => Set(ref _waitType, value); }
    private string _waitResource; public string WaitResource { get => _waitResource; set => Set(ref _waitResource, value); }
    private long _waitTimeMs; public long WaitTimeMs { get => _waitTimeMs; set => Set(ref _waitTimeMs, value); }
    private long _elapsedMs; public long ElapsedMs { get => _elapsedMs; set => Set(ref _elapsedMs, value); }
    private int _openTransactions; public int OpenTransactionCount { get => _openTransactions; set => Set(ref _openTransactions, value); }
    private string _statementText; public string StatementText { get => _statementText; set => Set(ref _statementText, value); }

    /// <summary>Two spaces per level of the chain, so the tree shape survives sorting being off.</summary>
    public string Indent => Depth <= 0 ? "" : new string(' ', Depth * 4) + "└ ";

    public bool IsHeadBlocker => Depth == 0;

    /// <summary>The head of a chain is the row to act on; everything below it is a symptom.</summary>
    public bool IsUnhealthy => Depth == 0;
}

/// <summary>
/// One wait type over the sample window. The delta columns are the point of this tab — cumulative wait stats
/// since instance start tell you about last month, not about the slowdown happening right now.
/// </summary>
internal sealed class PerfWaitRow : PerfRowBase
{
    public string Key => WaitType ?? "";

    public string WaitType { get; set; }

    private long _waitTimeMsDelta; public long WaitTimeMsDelta { get => _waitTimeMsDelta; set { Set(ref _waitTimeMsDelta, value); Raise(nameof(AvgMsPerWait)); } }
    private long _signalWaitMsDelta; public long SignalWaitMsDelta { get => _signalWaitMsDelta; set { Set(ref _signalWaitMsDelta, value); Raise(nameof(SignalWaitPercent)); } }
    private long _waitingTasksDelta; public long WaitingTasksDelta { get => _waitingTasksDelta; set { Set(ref _waitingTasksDelta, value); Raise(nameof(AvgMsPerWait)); } }
    private double _percentOfTotal; public double PercentOfTotal { get => _percentOfTotal; set => Set(ref _percentOfTotal, value); }
    private long _waitTimeMsTotal; public long WaitTimeMsTotal { get => _waitTimeMsTotal; set => Set(ref _waitTimeMsTotal, value); }
    private long _waitingTasksTotal; public long WaitingTasksTotal { get => _waitingTasksTotal; set => Set(ref _waitingTasksTotal, value); }
    private long _maxWaitTimeMs; public long MaxWaitTimeMs { get => _maxWaitTimeMs; set => Set(ref _maxWaitTimeMs, value); }

    public double? AvgMsPerWait => WaitingTasksDelta > 0 ? WaitTimeMsDelta / (double)WaitingTasksDelta : null;

    /// <summary>High signal wait share points at CPU pressure rather than the resource the wait names.</summary>
    public double? SignalWaitPercent => WaitTimeMsDelta > 0 ? SignalWaitMsDelta * 100d / WaitTimeMsDelta : null;

    /// <summary>
    /// Waits that indicate the server is out of a resource entirely rather than merely busy. Worth colouring
    /// because seeing any of these at all changes what you investigate next.
    /// </summary>
    private static readonly HashSet<string> PoisonWaits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "THREADPOOL", "RESOURCE_SEMAPHORE", "RESOURCE_SEMAPHORE_QUERY_COMPILE",
        "LOG_RATE_GOVERNOR", "SE_REPL_CATCHUP_THROTTLE", "PAGELATCH_DT", "PAGELATCH_EX_DT"
    };

    public bool IsUnhealthy => WaitTimeMsDelta > 0 && PoisonWaits.Contains(WaitType ?? "");
}

/// <summary>One cached plan's aggregate statistics, from sys.dm_exec_query_stats.</summary>
internal sealed class PerfQueryRow : PerfRowBase
{
    public string Key { get; set; }

    private string _databaseName; public string DatabaseName { get => _databaseName; set => Set(ref _databaseName, value); }
    private long _executionCount; public long ExecutionCount { get => _executionCount; set => Set(ref _executionCount, value); }
    private double _totalCpuMs; public double TotalCpuMs { get => _totalCpuMs; set => Set(ref _totalCpuMs, value); }
    private double _avgCpuMs; public double AvgCpuMs { get => _avgCpuMs; set => Set(ref _avgCpuMs, value); }
    private double _totalDurationMs; public double TotalDurationMs { get => _totalDurationMs; set => Set(ref _totalDurationMs, value); }
    private double _avgDurationMs; public double AvgDurationMs { get => _avgDurationMs; set => Set(ref _avgDurationMs, value); }
    private double _maxDurationMs; public double MaxDurationMs { get => _maxDurationMs; set => Set(ref _maxDurationMs, value); }
    private long _totalLogicalReads; public long TotalLogicalReads { get => _totalLogicalReads; set => Set(ref _totalLogicalReads, value); }
    private long _avgLogicalReads; public long AvgLogicalReads { get => _avgLogicalReads; set => Set(ref _avgLogicalReads, value); }
    private long _totalPhysicalReads; public long TotalPhysicalReads { get => _totalPhysicalReads; set => Set(ref _totalPhysicalReads, value); }
    private long _totalLogicalWrites; public long TotalLogicalWrites { get => _totalLogicalWrites; set => Set(ref _totalLogicalWrites, value); }
    private DateTime? _creationTime; public DateTime? CreationTime { get => _creationTime; set => Set(ref _creationTime, value); }
    private DateTime? _lastExecutionTime; public DateTime? LastExecutionTime { get => _lastExecutionTime; set => Set(ref _lastExecutionTime, value); }
    private string _statementText; public string StatementText { get => _statementText; set => Set(ref _statementText, value); }
    private string _queryHash; public string QueryHash { get => _queryHash; set => Set(ref _queryHash, value); }
}

/// <summary>
/// One database file's I/O over the sample window. Latency is the derived number that matters: stall
/// milliseconds divided by operations, over the interval rather than since startup.
/// </summary>
internal sealed class PerfFileRow : PerfRowBase
{
    public string Key => $"{DatabaseId}|{FileId}";

    public int DatabaseId { get; set; }
    public int FileId { get; set; }

    private string _databaseName; public string DatabaseName { get => _databaseName; set => Set(ref _databaseName, value); }
    private string _logicalName; public string LogicalName { get => _logicalName; set => Set(ref _logicalName, value); }
    private string _physicalName; public string PhysicalName { get => _physicalName; set => Set(ref _physicalName, value); }
    private string _fileType; public string FileType { get => _fileType; set => Set(ref _fileType, value); }
    private long _readsDelta; public long ReadsDelta { get => _readsDelta; set => Set(ref _readsDelta, value); }
    private long _writesDelta; public long WritesDelta { get => _writesDelta; set => Set(ref _writesDelta, value); }
    private long _bytesReadDelta; public long BytesReadDelta { get => _bytesReadDelta; set => Set(ref _bytesReadDelta, value); }
    private long _bytesWrittenDelta; public long BytesWrittenDelta { get => _bytesWrittenDelta; set => Set(ref _bytesWrittenDelta, value); }
    private long _sizeOnDiskBytes; public long SizeOnDiskBytes { get => _sizeOnDiskBytes; set => Set(ref _sizeOnDiskBytes, value); }

    private double? _readLatencyMs; public double? ReadLatencyMs { get => _readLatencyMs; set { Set(ref _readLatencyMs, value); RaiseHealth(); } }
    private double? _writeLatencyMs; public double? WriteLatencyMs { get => _writeLatencyMs; set { Set(ref _writeLatencyMs, value); RaiseHealth(); } }

    // Long-standing storage guidance: under 10 ms is healthy, 20 ms is where users notice, 50 ms is a problem.
    private const double WarnLatencyMs = 20;
    private const double BadLatencyMs = 50;

    /// <summary>Latency is only meaningful when the interval actually did some I/O on this file.</summary>
    private bool HasReads => ReadsDelta > 0;
    private bool HasWrites => WritesDelta > 0;

    public bool IsUnhealthy => (HasReads && ReadLatencyMs >= BadLatencyMs) || (HasWrites && WriteLatencyMs >= BadLatencyMs);

    public bool IsWarning => !IsUnhealthy
                          && ((HasReads && ReadLatencyMs >= WarnLatencyMs) || (HasWrites && WriteLatencyMs >= WarnLatencyMs));

    private void RaiseHealth()
    {
        Raise(nameof(IsUnhealthy));
        Raise(nameof(IsWarning));
    }
}

/// <summary>
/// The headline numbers on the Live tab. A single object rather than a row collection — it is bound directly
/// and updated in place, so the tiles never flicker between polls.
/// </summary>
internal sealed class PerfVitals : PerfRowBase
{
    private double? _cpuSqlPercent; public double? CpuSqlPercent { get => _cpuSqlPercent; set => Set(ref _cpuSqlPercent, value); }
    private double? _cpuOtherPercent; public double? CpuOtherPercent { get => _cpuOtherPercent; set => Set(ref _cpuOtherPercent, value); }
    private double? _batchRequestsPerSec; public double? BatchRequestsPerSec { get => _batchRequestsPerSec; set => Set(ref _batchRequestsPerSec, value); }
    private double? _compilationsPerSec; public double? CompilationsPerSec { get => _compilationsPerSec; set => Set(ref _compilationsPerSec, value); }
    private double? _recompilesPerSec; public double? RecompilesPerSec { get => _recompilesPerSec; set => Set(ref _recompilesPerSec, value); }
    private double? _transactionsPerSec; public double? TransactionsPerSec { get => _transactionsPerSec; set => Set(ref _transactionsPerSec, value); }
    private double? _lockWaitsPerSec; public double? LockWaitsPerSec { get => _lockWaitsPerSec; set => Set(ref _lockWaitsPerSec, value); }

    private long? _pageLifeExpectancy; public long? PageLifeExpectancy { get => _pageLifeExpectancy; set => Set(ref _pageLifeExpectancy, value); }
    private long? _totalServerMemoryKb; public long? TotalServerMemoryKb { get => _totalServerMemoryKb; set => Set(ref _totalServerMemoryKb, value); }
    private long? _targetServerMemoryKb; public long? TargetServerMemoryKb { get => _targetServerMemoryKb; set => Set(ref _targetServerMemoryKb, value); }
    private long? _physicalMemoryInUseKb; public long? PhysicalMemoryInUseKb { get => _physicalMemoryInUseKb; set => Set(ref _physicalMemoryInUseKb, value); }

    private int _activeRequests; public int ActiveRequests { get => _activeRequests; set => Set(ref _activeRequests, value); }
    private int _blockedRequests; public int BlockedRequests { get => _blockedRequests; set => Set(ref _blockedRequests, value); }
    private int _userSessions; public int UserSessions { get => _userSessions; set => Set(ref _userSessions, value); }
    private int _activeTransactions; public int ActiveTransactions { get => _activeTransactions; set => Set(ref _activeTransactions, value); }
    private long _longestRunningSeconds; public long LongestRunningSeconds { get => _longestRunningSeconds; set => Set(ref _longestRunningSeconds, value); }
    private int _cpuCount; public int CpuCount { get => _cpuCount; set => Set(ref _cpuCount, value); }

    private double _tempdbFreeMb; public double TempdbFreeMb { get => _tempdbFreeMb; set => Set(ref _tempdbFreeMb, value); }
    private double _tempdbUserObjectMb; public double TempdbUserObjectMb { get => _tempdbUserObjectMb; set => Set(ref _tempdbUserObjectMb, value); }
    private double _tempdbInternalObjectMb; public double TempdbInternalObjectMb { get => _tempdbInternalObjectMb; set => Set(ref _tempdbInternalObjectMb, value); }
    private double _tempdbVersionStoreMb; public double TempdbVersionStoreMb { get => _tempdbVersionStoreMb; set => Set(ref _tempdbVersionStoreMb, value); }
    private double _tempdbTotalMb; public double TempdbTotalMb { get => _tempdbTotalMb; set => Set(ref _tempdbTotalMb, value); }

    public double TempdbUsedPercent => TempdbTotalMb > 0 ? (TempdbTotalMb - TempdbFreeMb) * 100d / TempdbTotalMb : 0;

    // Rolling windows for the tile sparklines; replaced with a fresh array each poll so the bindings fire.
    private IReadOnlyList<double> _cpuHistory; public IReadOnlyList<double> CpuHistory { get => _cpuHistory; set => Set(ref _cpuHistory, value); }
    private IReadOnlyList<double> _batchHistory; public IReadOnlyList<double> BatchHistory { get => _batchHistory; set => Set(ref _batchHistory, value); }
    private IReadOnlyList<double> _pleHistory; public IReadOnlyList<double> PleHistory { get => _pleHistory; set => Set(ref _pleHistory, value); }
    private IReadOnlyList<double> _blockedHistory; public IReadOnlyList<double> BlockedHistory { get => _blockedHistory; set => Set(ref _blockedHistory, value); }
    private IReadOnlyList<double> _activeHistory; public IReadOnlyList<double> ActiveHistory { get => _activeHistory; set => Set(ref _activeHistory, value); }
    private IReadOnlyList<double> _tempdbHistory; public IReadOnlyList<double> TempdbHistory { get => _tempdbHistory; set => Set(ref _tempdbHistory, value); }
}

/// <summary>One name/value fact about the instance, for the Server info tab's grid.</summary>
internal sealed class PerfServerPropertyRow : PerfRowBase
{
    public string Key => Group + "|" + Name;

    public string Group { get; set; }
    public string Name { get; set; }

    private string _value; public string Value { get => _value; set => Set(ref _value, value); }
    private string _hint; public string Hint { get => _hint; set => Set(ref _hint, value); }

    /// <summary>Tints the row. Reserved for settings that are worth a second look, not for anything unusual.</summary>
    private bool _isWarning; public bool IsWarning { get => _isWarning; set => Set(ref _isWarning, value); }
}

/// <summary>
/// What the instance says about itself, plus where its build sits in the servicing and support timeline.
///
/// <para>Collected once per pinned server and on an explicit Refresh — <b>not on the poll timer</b>. Nothing
/// here changes on a five-second scale except uptime, so re-reading it every poll would buy a round trip and a
/// capability probe per tick for no new information. <see cref="CollectedAtLocal"/> is displayed for that
/// reason: it is the "as at" for uptime.</para>
/// </summary>
internal sealed class PerfServerInfo
{
    public DateTime CollectedAtLocal { get; set; }

    public string ServerName { get; set; }
    public string ProductVersion { get; set; }
    public string ProductLevel { get; set; }
    public string ProductUpdateLevel { get; set; }
    public string Edition { get; set; }
    public int? EngineEdition { get; set; }
    public string VersionString { get; set; }

    public DateTime? StartTime { get; set; }
    public long? UptimeSeconds { get; set; }

    /// <summary>Where this build sits in the build list. Never null once a version was read.</summary>
    public SqlBuildMatch Build { get; set; }

    public List<PerfServerPropertyRow> Properties { get; } = new List<PerfServerPropertyRow>();

    /// <summary>The listed builds newer than this one, newest first — what an upgrade would pick up.</summary>
    public List<SqlServerBuild> NewerBuilds { get; } = new List<SqlServerBuild>();

    /// <summary>
    /// Azure SQL Database, Managed Instance, Synapse, Edge and Fabric: Microsoft patches these and their
    /// <c>ProductVersion</c> does not correspond to a row in a box-product build list. Saying "3 CUs behind"
    /// about one would be nonsense, so the patch verdict is suppressed rather than guessed.
    /// </summary>
    public bool IsAzureManaged =>
        EngineEdition == 5 || EngineEdition == 6 || EngineEdition == 8 || EngineEdition == 9
        || EngineEdition == 11 || EngineEdition == 12;

    public string EngineEditionDescription
    {
        get
        {
            switch (EngineEdition)
            {
                case 1: return "Personal or Desktop";
                case 2: return "Standard (Standard, Web or BI)";
                case 3: return "Enterprise (Enterprise, Developer or Evaluation)";
                case 4: return "Express (Express, Express with Tools or LocalDB)";
                case 5: return "Azure SQL Database";
                case 6: return "Azure Synapse Analytics";
                case 8: return "Azure SQL Managed Instance";
                case 9: return "Azure SQL Edge";
                case 11: return "Azure Synapse serverless SQL pool";
                case 12: return "Fabric SQL database";
                case null: return null;
                default: return "Engine edition " + EngineEdition.Value.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}

/// <summary>Everything one poll collected.</summary>
internal sealed class PerfSnapshot
{
    public PerfVitals Vitals { get; } = new PerfVitals();
    public List<PerfRequestRow> Requests { get; } = new List<PerfRequestRow>();
    public List<PerfBlockingRow> Blocking { get; } = new List<PerfBlockingRow>();
    public List<PerfWaitRow> Waits { get; } = new List<PerfWaitRow>();
    public List<PerfQueryRow> Queries { get; } = new List<PerfQueryRow>();
    public List<PerfFileRow> Files { get; } = new List<PerfFileRow>();

    public string ServerName { get; set; }

    /// <summary>The login this poll ran as, per <c>SUSER_SNAME()</c>. Shown beside the server in the header.</summary>
    public string LoginName { get; set; }
    public DateTime CollectedAtLocal { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// How many sections this poll read and how many of those failed, reported next to the timing. What the window
    /// covers varies with the release and the login's rights, so "6 sections in 212 ms" says considerably more
    /// about an empty tab than the duration alone.
    /// </summary>
    public int SectionsRead { get; set; }

    public int SectionsFailed { get; set; }

    /// <summary>
    /// The Server info tab. Null on the polls that did not ask for it — it is collected on the first poll for a
    /// server and on an explicit Refresh, not on the timer.
    /// </summary>
    public PerfServerInfo ServerInfo { get; set; }

    /// <summary>Seconds covered by the delta columns. Null on the very first poll, before a baseline exists.</summary>
    public double? IntervalSeconds { get; set; }

    /// <summary>Per-section failures, so one unavailable DMV degrades one tab rather than the dashboard.</summary>
    public List<string> Warnings { get; } = new List<string>();
}

/// <summary>Which column the Top Queries tab sorts and truncates by.</summary>
internal enum PerfQueryMetric
{
    AvgCpu,
    TotalCpu,
    AvgDuration,
    TotalDuration,
    AvgLogicalReads,
    TotalLogicalReads,
    ExecutionCount
}
