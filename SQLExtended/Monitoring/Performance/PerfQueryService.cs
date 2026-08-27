using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SQLExtended.Monitoring.Performance;

/// <summary>
/// Reads live server health out of the standard DMVs. Runs on a background thread against the window's pinned
/// connection, and needs only VIEW SERVER STATE.
///
/// As with the Always On monitor, the connection is forced to master and every section is collected inside its
/// own try/catch so one unavailable DMV costs one tab rather than the whole dashboard.
/// </summary>
internal static class PerfQueryService
{
    internal const int CommandTimeoutSeconds = 20;

    /// <summary>How long the very first poll waits between its baseline and measurement reads.</summary>
    internal const int BaselineSampleMs = 1000;

    public static string BuildMonitorConnectionString(string baseConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
            ApplicationName = "SQLExtended Performance Monitor",
            ConnectTimeout = 10
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Collects one snapshot. When <paramref name="tracker"/> has no baseline the cumulative sources are read
    /// twice, one second apart, so the delta columns have real numbers on the very first refresh instead of a
    /// grid full of dashes. That costs a second once per server, not on every poll.
    /// </summary>
    /// <param name="progress">Reports each section as it starts, for the status line. Null on the timer polls.</param>
    /// <param name="onLiveReady">
    /// Awaited once the sections the Live tab is drawn from have been read, before the rest are collected — see
    /// <see cref="MonitorPlan"/>. Top queries and the server information read are the two expensive ones here, and
    /// neither appears on the tab the window opens on.
    /// </param>
    /// <param name="recentDumpDays">
    /// The Server info tab's memory-dump flagging window, from settings. Read on the UI thread by the control and
    /// carried through here rather than looked up in the collection — see <see cref="PerfServerInfoQuery"/>.
    /// </param>
    public static async Task<PerfSnapshot> CollectAsync(string connectionString, PerfDeltaTracker tracker, PerfQueryMetric metric, int topQueries,
                                                        bool includeBenignWaits, bool includeServerInfo, int recentDumpDays,
                                                        IProgress<MonitorStep> progress, Func<PerfSnapshot, Task> onLiveReady, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var snapshot = new PerfSnapshot { CollectedAtLocal = DateTime.Now };

        using (var conn = new SqlConnection(connectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);

            var plan = new MonitorPlan(progress, snapshot.Warnings.Add);

            // First, and primary, because everything below reads through the tracker it seeds — including the
            // interval the vitals rates are computed over. It carries a deliberate one-second wait, so it is also
            // the reason the first poll of a server is the slow one and the status line has to say what it is doing.
            plan.AddIf(tracker.NeedsBaseline, "a baseline sample (one second)", async () =>
            {
                await SeedBaselineAsync(conn, tracker, includeBenignWaits, ct).ConfigureAwait(false);
                await Task.Delay(BaselineSampleMs, ct).ConfigureAwait(false);
            }, primary: true);

            plan.Add("server vitals", () => ReadVitalsAsync(conn, tracker, snapshot, ct), primary: true)
                .Add("wait statistics", () => ReadWaitsAsync(conn, tracker, snapshot, includeBenignWaits, ct), primary: true)
                .Add("file I/O", () => ReadFileStatsAsync(conn, tracker, snapshot, ct), primary: true)
                .Add("active requests", () => ReadRequestsAsync(conn, snapshot, ct))
                .Add("top queries", () => ReadTopQueriesAsync(conn, snapshot, metric, topQueries, ct))

                // Asked for on the first poll for a server and on an explicit Refresh, never on the timer: it costs
                // a capability probe plus a seven-result-set read for facts that do not change between ticks. Last
                // for the same reason — it is the most expensive read here and it backs the tab furthest from view.
                .AddIf(includeServerInfo, "server information", async () =>
                {
                    snapshot.ServerInfo = await PerfServerInfoQuery.CollectAsync(conn, DateTime.Now, recentDumpDays, ct).ConfigureAwait(false);
                });

            await plan.RunAsync(() => onLiveReady?.Invoke(snapshot) ?? Task.CompletedTask).ConfigureAwait(false);

            snapshot.SectionsRead = plan.Ran;
            snapshot.SectionsFailed = plan.Failed;
        }

        BuildBlockingChains(snapshot);
        snapshot.Duration = DateTime.UtcNow - started;
        return snapshot;
    }

    /// <summary>Reads the cumulative sources once and stores them, without producing any rows.</summary>
    private static async Task SeedBaselineAsync(SqlConnection conn, PerfDeltaTracker tracker, bool includeBenignWaits, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(TicksSql + WaitsSql(includeBenignWaits) + FileStatsSql + CountersSql, conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
                tracker.SetTicks(Long(reader, "ms_ticks") ?? 0);

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            tracker.StoreWaits(await ReadWaitSamplesAsync(reader, ct).ConfigureAwait(false));

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            tracker.StoreFiles(await ReadFileSamplesAsync(reader, ct).ConfigureAwait(false));

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            tracker.StoreCounters(await ReadCounterSamplesAsync(reader, ct).ConfigureAwait(false));
        }
    }

    // =====================================================================================================
    // Vitals
    // =====================================================================================================

    private const string TicksSql = @"
SELECT si.ms_ticks FROM sys.dm_os_sys_info AS si;
";

    internal const string VitalsSql = @"
-- Instance-wide counts, sized and timed from the DMVs rather than from repeated round trips.
SELECT
    (SELECT COUNT(*) FROM sys.dm_exec_requests AS r
      JOIN sys.dm_exec_sessions AS s ON s.session_id = r.session_id
     WHERE s.is_user_process = 1 AND r.session_id <> @@SPID)                                  AS active_requests,
    (SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id <> 0)                AS blocked_requests,
    (SELECT COUNT(*) FROM sys.dm_exec_sessions WHERE is_user_process = 1)                     AS user_sessions,
    (SELECT COUNT(*) FROM sys.dm_tran_active_transactions)                                    AS active_transactions,
    (SELECT ISNULL(MAX(DATEDIFF(second, r.start_time, GETDATE())), 0) FROM sys.dm_exec_requests AS r
      JOIN sys.dm_exec_sessions AS s ON s.session_id = r.session_id
     WHERE s.is_user_process = 1 AND r.session_id <> @@SPID)                                  AS longest_running_seconds,
    si.ms_ticks,
    si.cpu_count,
    si.committed_target_kb,
    pm.physical_memory_in_use_kb,
    SERVERPROPERTY('ServerName')                                                              AS server_name,
    SUSER_SNAME()                                                                             AS login_name
FROM sys.dm_os_sys_info AS si
CROSS JOIN sys.dm_os_process_memory AS pm;
";

    private const string TempdbSql = @"
SELECT
    SUM(unallocated_extent_page_count)        * 8 / 1024.0 AS free_mb,
    SUM(user_object_reserved_page_count)      * 8 / 1024.0 AS user_object_mb,
    SUM(internal_object_reserved_page_count)  * 8 / 1024.0 AS internal_object_mb,
    SUM(version_store_reserved_page_count)    * 8 / 1024.0 AS version_store_mb,
    SUM(total_page_count)                     * 8 / 1024.0 AS total_mb
FROM tempdb.sys.dm_db_file_space_usage;
";

    // The scheduler-monitor ring buffer keeps roughly one CPU sample per minute for the last few hours, so the
    // CPU chart has real history the moment the window opens rather than starting empty.
    private const string CpuRingBufferSql = @"
SELECT TOP (60)
    rb.sql_cpu,
    rb.idle_cpu,
    100 - rb.sql_cpu - rb.idle_cpu AS other_cpu
FROM (
    SELECT
        CAST(record AS xml).value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS sql_cpu,
        CAST(record AS xml).value('(./Record/SchedulerMonitorEvent/SystemHealth/SystemIdle)[1]', 'int')         AS idle_cpu,
        [timestamp]
    FROM sys.dm_os_ring_buffers
    WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR' AND record LIKE N'%<SystemHealth>%'
) AS rb
ORDER BY rb.[timestamp] DESC;
";

    private const string CountersSql = @"
SELECT RTRIM(counter_name) AS counter_name, RTRIM(instance_name) AS instance_name, cntr_value, cntr_type
FROM sys.dm_os_performance_counters
WHERE counter_name IN (
    N'Batch Requests/sec', N'SQL Compilations/sec', N'SQL Re-Compilations/sec', N'Transactions/sec',
    N'Page life expectancy', N'Total Server Memory (KB)', N'Target Server Memory (KB)',
    N'Processes blocked', N'User Connections', N'Lock Waits/sec');
";

    private static async Task ReadVitalsAsync(SqlConnection conn, PerfDeltaTracker tracker, PerfSnapshot snapshot, CancellationToken ct)
    {
        var vitals = snapshot.Vitals;

        using (var cmd = new SqlCommand(VitalsSql + TempdbSql + CpuRingBufferSql + CountersSql, conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            long msTicks = 0;
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                vitals.ActiveRequests = Int(reader, "active_requests") ?? 0;
                vitals.BlockedRequests = Int(reader, "blocked_requests") ?? 0;
                vitals.UserSessions = Int(reader, "user_sessions") ?? 0;
                vitals.ActiveTransactions = Int(reader, "active_transactions") ?? 0;
                vitals.LongestRunningSeconds = Long(reader, "longest_running_seconds") ?? 0;
                vitals.CpuCount = Int(reader, "cpu_count") ?? 0;
                vitals.TargetServerMemoryKb = Long(reader, "committed_target_kb");
                vitals.PhysicalMemoryInUseKb = Long(reader, "physical_memory_in_use_kb");
                snapshot.ServerName = Str(reader, "server_name");
                snapshot.LoginName = Str(reader, "login_name");
                msTicks = Long(reader, "ms_ticks") ?? 0;
            }

            snapshot.IntervalSeconds = tracker.IntervalSecondsFrom(msTicks);

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                vitals.TempdbFreeMb = Double(reader, "free_mb") ?? 0;
                vitals.TempdbUserObjectMb = Double(reader, "user_object_mb") ?? 0;
                vitals.TempdbInternalObjectMb = Double(reader, "internal_object_mb") ?? 0;
                vitals.TempdbVersionStoreMb = Double(reader, "version_store_mb") ?? 0;
                vitals.TempdbTotalMb = Double(reader, "total_mb") ?? 0;
            }

            // CPU history arrives newest-first; reverse it so the sparkline reads left-to-right in time.
            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var cpuHistory = new List<double>();
            bool first = true;
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                double sqlCpu = Double(reader, "sql_cpu") ?? 0;
                if (first)
                {
                    vitals.CpuSqlPercent = sqlCpu;
                    vitals.CpuOtherPercent = Double(reader, "other_cpu") ?? 0;
                    first = false;
                }
                cpuHistory.Add(sqlCpu);
            }
            cpuHistory.Reverse();
            vitals.CpuHistory = cpuHistory;

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var counters = await ReadCounterSamplesAsync(reader, ct).ConfigureAwait(false);
            ApplyCounters(vitals, counters, tracker, snapshot.IntervalSeconds);

            tracker.StoreCounters(counters);
            tracker.SetTicks(msTicks);
        }
    }

    /// <summary>
    /// Maps the raw counter rows onto the vitals. Cumulative counters become per-second rates via the tracker;
    /// raw counters are used as-is.
    /// </summary>
    private static void ApplyCounters(PerfVitals vitals, Dictionary<string, long> counters, PerfDeltaTracker tracker, double? intervalSeconds)
    {
        vitals.BatchRequestsPerSec = tracker.RateFor("Batch Requests/sec", Counter(counters, "Batch Requests/sec"), intervalSeconds);
        vitals.CompilationsPerSec = tracker.RateFor("SQL Compilations/sec", Counter(counters, "SQL Compilations/sec"), intervalSeconds);
        vitals.RecompilesPerSec = tracker.RateFor("SQL Re-Compilations/sec", Counter(counters, "SQL Re-Compilations/sec"), intervalSeconds);
        vitals.TransactionsPerSec = tracker.RateFor("Transactions/sec", Counter(counters, "Transactions/sec"), intervalSeconds);
        vitals.LockWaitsPerSec = tracker.RateFor("Lock Waits/sec", Counter(counters, "Lock Waits/sec"), intervalSeconds);

        if (counters.TryGetValue("Page life expectancy", out long ple)) vitals.PageLifeExpectancy = ple;
        if (counters.TryGetValue("Total Server Memory (KB)", out long total)) vitals.TotalServerMemoryKb = total;
    }

    private static long Counter(Dictionary<string, long> counters, string name) => counters.TryGetValue(name, out long value) ? value : 0;

    /// <summary>
    /// Reduces the counter rows to one value per counter name. Transactions/sec is emitted per database plus a
    /// _Total; page life expectancy appears once for Buffer Manager and again per NUMA node under Buffer Node.
    /// Taking the instance-less row where one exists, and otherwise the largest, picks the instance-wide value
    /// in both cases.
    /// </summary>
    private static async Task<Dictionary<string, long>> ReadCounterSamplesAsync(SqlDataReader reader, CancellationToken ct)
    {
        var counters = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var hasInstanceless = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            string name = Str(reader, "counter_name");
            string instance = Str(reader, "instance_name") ?? "";
            long value = Long(reader, "cntr_value") ?? 0;
            if (name == null) continue;

            bool isInstanceless = instance.Length == 0;
            bool isTotal = string.Equals(instance, "_Total", StringComparison.OrdinalIgnoreCase);

            if (isInstanceless)
            {
                counters[name] = value;
                hasInstanceless.Add(name);
                continue;
            }

            if (hasInstanceless.Contains(name)) continue;

            if (isTotal || !counters.TryGetValue(name, out long existing) || value > existing)
                counters[name] = value;
        }

        return counters;
    }

    // =====================================================================================================
    // Active requests
    // =====================================================================================================

    // The second branch matters as much as the first: a session that is sleeping with an open transaction has
    // no row in sys.dm_exec_requests at all, yet it is the classic head of a blocking chain.
    internal const string RequestsSql = @"
SELECT
    r.session_id,
    r.blocking_session_id,
    s.login_name,
    s.host_name,
    s.program_name,
    DB_NAME(r.database_id)                                          AS database_name,
    r.status,
    r.command,
    r.wait_type,
    CONVERT(bigint, r.wait_time)                                    AS wait_time_ms,
    r.last_wait_type,
    r.wait_resource,
    CONVERT(bigint, r.cpu_time)                                     AS cpu_time_ms,
    CONVERT(bigint, r.total_elapsed_time)                           AS elapsed_ms,
    r.logical_reads,
    r.reads                                                         AS physical_reads,
    r.writes,
    CONVERT(bigint, r.granted_query_memory) * 8                     AS granted_memory_kb,
    r.open_transaction_count,
    r.percent_complete,
    r.start_time,
    SUBSTRING(t.text, (r.statement_start_offset / 2) + 1,
        ((CASE r.statement_end_offset WHEN -1 THEN DATALENGTH(t.text)
                                      ELSE r.statement_end_offset END - r.statement_start_offset) / 2) + 1) AS statement_text,
    t.text                                                          AS batch_text,
    CONVERT(varchar(34), r.query_hash, 1)                           AS query_hash,
    CONVERT(bit, 1)                                                 AS is_running
FROM sys.dm_exec_requests AS r
JOIN sys.dm_exec_sessions AS s ON s.session_id = r.session_id
OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) AS t
WHERE r.session_id <> @@SPID AND s.is_user_process = 1

UNION ALL

SELECT
    s.session_id,
    0,
    s.login_name,
    s.host_name,
    s.program_name,
    DB_NAME(s.database_id),
    s.status,
    N'(idle, open transaction)',
    -- Every NULL here is typed on purpose. A bare NULL literal is an int, and int outranks nvarchar in
    -- UNION type precedence, so leaving them untyped risks the whole column resolving to int and the
    -- first branch's text failing to convert.
    CONVERT(nvarchar(60),  NULL),
    CONVERT(bigint, 0),
    CONVERT(nvarchar(60),  NULL),
    CONVERT(nvarchar(256), NULL),
    CONVERT(bigint, s.cpu_time),
    CONVERT(bigint, DATEDIFF(second, s.last_request_end_time, GETDATE())) * 1000,
    s.logical_reads,
    s.reads,
    s.writes,
    CONVERT(bigint, 0),
    0,
    CONVERT(real, 0),
    s.last_request_end_time,
    t.text,
    t.text,
    CONVERT(varchar(34), NULL),
    CONVERT(bit, 0)
FROM sys.dm_exec_sessions AS s
JOIN sys.dm_tran_session_transactions AS st ON st.session_id = s.session_id
OUTER APPLY (
    SELECT TOP (1) c.most_recent_sql_handle
    FROM sys.dm_exec_connections AS c
    WHERE c.session_id = s.session_id
    ORDER BY c.last_read DESC
) AS mc
OUTER APPLY sys.dm_exec_sql_text(mc.most_recent_sql_handle) AS t
WHERE s.is_user_process = 1
  AND s.session_id <> @@SPID
  AND NOT EXISTS (SELECT 1 FROM sys.dm_exec_requests AS r2 WHERE r2.session_id = s.session_id);
";

    private static async Task ReadRequestsAsync(SqlConnection conn, PerfSnapshot snapshot, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(RequestsSql, conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                snapshot.Requests.Add(new PerfRequestRow
                {
                    SessionId = Int(reader, "session_id") ?? 0,
                    BlockingSessionId = Int(reader, "blocking_session_id") ?? 0,
                    LoginName = Str(reader, "login_name"),
                    HostName = Str(reader, "host_name"),
                    ProgramName = Str(reader, "program_name"),
                    DatabaseName = Str(reader, "database_name"),
                    Status = Str(reader, "status"),
                    Command = Str(reader, "command"),
                    WaitType = Str(reader, "wait_type"),
                    WaitTimeMs = Long(reader, "wait_time_ms") ?? 0,
                    LastWaitType = Str(reader, "last_wait_type"),
                    WaitResource = Str(reader, "wait_resource"),
                    CpuTimeMs = Long(reader, "cpu_time_ms") ?? 0,
                    ElapsedMs = Long(reader, "elapsed_ms") ?? 0,
                    LogicalReads = Long(reader, "logical_reads") ?? 0,
                    PhysicalReads = Long(reader, "physical_reads") ?? 0,
                    Writes = Long(reader, "writes") ?? 0,
                    GrantedMemoryKb = Long(reader, "granted_memory_kb") ?? 0,
                    OpenTransactionCount = Int(reader, "open_transaction_count") ?? 0,
                    PercentComplete = Double(reader, "percent_complete") ?? 0,
                    StartTime = Date(reader, "start_time"),
                    StatementText = Str(reader, "statement_text"),
                    BatchText = Str(reader, "batch_text"),
                    QueryHash = Str(reader, "query_hash"),
                    IsRunning = Bool(reader, "is_running") == true
                });
            }
        }
    }

    /// <summary>
    /// Walks blocking_session_id up to each chain's head and flattens the result depth-first.
    ///
    /// Derived from the already-collected request rows rather than a second query: two round trips would let
    /// the Activity and Blocking tabs disagree about the same instant, which is exactly the kind of thing that
    /// sends you chasing a session that was never really blocked.
    /// </summary>
    private static void BuildBlockingChains(PerfSnapshot snapshot)
    {
        var bySession = new Dictionary<int, PerfRequestRow>();
        foreach (var request in snapshot.Requests) bySession[request.SessionId] = request;

        // Direct children of each blocker.
        var children = new Dictionary<int, List<PerfRequestRow>>();
        foreach (var request in snapshot.Requests)
        {
            int blocker = request.BlockingSessionId;
            if (blocker == 0 || blocker == request.SessionId) continue;

            if (!children.TryGetValue(blocker, out var list))
                children[blocker] = list = new List<PerfRequestRow>();
            list.Add(request);
        }

        if (children.Count == 0) return;

        // A chain head is a blocker that is not itself blocked. A blocker we never saw as a session — a system
        // task, or one that vanished between the two reads — still counts as a head so its victims are shown.
        var heads = new List<int>();
        foreach (int blocker in children.Keys)
        {
            bySession.TryGetValue(blocker, out var blockerRow);
            if (blockerRow == null || blockerRow.BlockingSessionId == 0)
                heads.Add(blocker);
        }

        heads.Sort();

        foreach (int head in heads)
        {
            bySession.TryGetValue(head, out var headRow);
            AppendChain(snapshot, children, head, headRow, head, 0, new HashSet<int>());
        }

        // Fill in each request's transitive victim count so the Activity tab can flag blockers too.
        foreach (var request in snapshot.Requests)
            request.BlockedCount = CountVictims(children, request.SessionId, new HashSet<int>());
    }

    private static void AppendChain(PerfSnapshot snapshot, Dictionary<int, List<PerfRequestRow>> children,
                                    int headSession, PerfRequestRow row, int sessionId, int depth, HashSet<int> visited)
    {
        // Blocking graphs can contain cycles when a deadlock is mid-resolution; never recurse into one twice.
        if (!visited.Add(sessionId)) return;

        snapshot.Blocking.Add(new PerfBlockingRow
        {
            HeadBlockerSessionId = headSession,
            SessionId = sessionId,
            Depth = depth,
            BlockedCount = CountVictims(children, sessionId, new HashSet<int>()),
            LoginName = row?.LoginName,
            HostName = row?.HostName,
            ProgramName = row?.ProgramName,
            DatabaseName = row?.DatabaseName,
            Status = row?.Status ?? "(not a user session)",
            WaitType = row?.WaitType,
            WaitResource = row?.WaitResource,
            WaitTimeMs = row?.WaitTimeMs ?? 0,
            ElapsedMs = row?.ElapsedMs ?? 0,
            OpenTransactionCount = row?.OpenTransactionCount ?? 0,
            StatementText = row?.StatementText
        });

        if (!children.TryGetValue(sessionId, out var victims)) return;

        victims.Sort((a, b) => b.WaitTimeMs.CompareTo(a.WaitTimeMs));
        foreach (var victim in victims)
            AppendChain(snapshot, children, headSession, victim, victim.SessionId, depth + 1, visited);
    }

    private static int CountVictims(Dictionary<int, List<PerfRequestRow>> children, int sessionId, HashSet<int> visited)
    {
        if (!visited.Add(sessionId)) return 0;
        if (!children.TryGetValue(sessionId, out var victims)) return 0;

        int count = victims.Count;
        foreach (var victim in victims)
            count += CountVictims(children, victim.SessionId, visited);

        return count;
    }

    // =====================================================================================================
    // Wait statistics
    // =====================================================================================================

    /// <summary>
    /// The waits every server accumulates constantly while doing nothing interesting — background tasks
    /// sleeping, workers parked, queues idling. Left in, they bury the handful of waits that explain a
    /// slowdown. This is the widely used community filter list; the tab has a checkbox to show them anyway.
    /// </summary>
    private static readonly string[] BenignWaits =
    {
        "BROKER_EVENTHANDLER", "BROKER_RECEIVE_WAITFOR", "BROKER_TASK_STOP", "BROKER_TO_FLUSH", "BROKER_TRANSMITTER",
        "CHECKPOINT_QUEUE", "CHKPT", "CLR_AUTO_EVENT", "CLR_MANUAL_EVENT", "CLR_SEMAPHORE",
        "DBMIRROR_DBM_EVENT", "DBMIRROR_EVENTS_QUEUE", "DBMIRROR_WORKER_QUEUE", "DBMIRRORING_CMD",
        "DIRTY_PAGE_POLL", "DISPATCHER_QUEUE_SEMAPHORE", "EXECSYNC", "FSAGENT",
        "FT_IFTS_SCHEDULER_IDLE_WAIT", "FT_IFTSHC_MUTEX", "HADR_CLUSAPI_CALL", "HADR_FILESTREAM_IOMGR_IOCOMPLETION",
        "HADR_LOGCAPTURE_WAIT", "HADR_NOTIFICATION_DEQUEUE", "HADR_TIMER_TASK", "HADR_WORK_QUEUE",
        "KSOURCE_WAKEUP", "LAZYWRITER_SLEEP", "LOGMGR_QUEUE", "MEMORY_ALLOCATION_EXT",
        "ONDEMAND_TASK_QUEUE", "PARALLEL_REDO_DRAIN_WORKER", "PARALLEL_REDO_LOG_CACHE", "PARALLEL_REDO_TRAN_LIST",
        "PARALLEL_REDO_WORKER_SYNC", "PARALLEL_REDO_WORKER_WAIT_WORK",
        "PREEMPTIVE_XE_GETTARGETSTATE", "PWAIT_ALL_COMPONENTS_INITIALIZED", "PWAIT_DIRECTLOGCONSUMER_GETNEXT",
        "QDS_PERSIST_TASK_MAIN_LOOP_SLEEP", "QDS_ASYNC_QUEUE", "QDS_CLEANUP_STALE_QUERIES_TASK_MAIN_LOOP_SLEEP",
        "QDS_SHUTDOWN_QUEUE", "REDO_THREAD_PENDING_WORK", "REQUEST_FOR_DEADLOCK_SEARCH", "RESOURCE_QUEUE",
        "SERVER_IDLE_CHECK", "SLEEP_BPOOL_FLUSH", "SLEEP_DBSTARTUP", "SLEEP_DCOMSTARTUP",
        "SLEEP_MASTERDBREADY", "SLEEP_MASTERMDREADY", "SLEEP_MASTERUPGRADED", "SLEEP_MSDBSTARTUP",
        "SLEEP_SYSTEMTASK", "SLEEP_TASK", "SLEEP_TEMPDBSTARTUP", "SNI_HTTP_ACCEPT",
        "SOS_WORK_DISPATCHER", "SP_SERVER_DIAGNOSTICS_SLEEP", "SQLTRACE_BUFFER_FLUSH",
        "SQLTRACE_INCREMENTAL_FLUSH_SLEEP", "SQLTRACE_WAIT_ENTRIES", "STARTUP_DEPENDENCY_MANAGER",
        "VDI_CLIENT_OTHER", "WAIT_FOR_RESULTS", "WAITFOR", "WAITFOR_TASKSHUTDOWN",
        "WAIT_XTP_RECOVERY", "WAIT_XTP_HOST_WAIT", "WAIT_XTP_OFFLINE_CKPT_NEW_LOG", "WAIT_XTP_CKPT_CLOSE",
        "XE_DISPATCHER_JOIN", "XE_DISPATCHER_WAIT", "XE_LIVE_TARGET_TVF", "XE_TIMER_EVENT"
    };

    internal static string WaitsSql(bool includeBenign)
    {
        string filter = includeBenign
            ? ""
            : "WHERE wait_type NOT IN (N'" + string.Join("', N'", BenignWaits) + "')" + Environment.NewLine +
              "  AND wait_type NOT LIKE N'SLEEP[_]%'" + Environment.NewLine +
              "  AND wait_type NOT LIKE N'QDS[_]%'";

        return $@"
SELECT wait_type, waiting_tasks_count, wait_time_ms, signal_wait_time_ms, max_wait_time_ms
FROM sys.dm_os_wait_stats
{filter};
";
    }

    private static async Task<Dictionary<string, PerfDeltaTracker.WaitSample>> ReadWaitSamplesAsync(SqlDataReader reader, CancellationToken ct)
    {
        var samples = new Dictionary<string, PerfDeltaTracker.WaitSample>(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            string waitType = Str(reader, "wait_type");
            if (waitType == null) continue;

            samples[waitType] = new PerfDeltaTracker.WaitSample
            {
                WaitTimeMs = Long(reader, "wait_time_ms") ?? 0,
                SignalWaitTimeMs = Long(reader, "signal_wait_time_ms") ?? 0,
                WaitingTasks = Long(reader, "waiting_tasks_count") ?? 0
            };
        }

        return samples;
    }

    private static async Task ReadWaitsAsync(SqlConnection conn, PerfDeltaTracker tracker, PerfSnapshot snapshot, bool includeBenign, CancellationToken ct)
    {
        var current = new Dictionary<string, PerfDeltaTracker.WaitSample>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<PerfWaitRow>();

        using (var cmd = new SqlCommand(WaitsSql(includeBenign), conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                string waitType = Str(reader, "wait_type");
                if (waitType == null) continue;

                var sample = new PerfDeltaTracker.WaitSample
                {
                    WaitTimeMs = Long(reader, "wait_time_ms") ?? 0,
                    SignalWaitTimeMs = Long(reader, "signal_wait_time_ms") ?? 0,
                    WaitingTasks = Long(reader, "waiting_tasks_count") ?? 0
                };
                current[waitType] = sample;

                var delta = tracker.DeltaFor(waitType, sample);
                if (delta == null || delta.Value.WaitTimeMs <= 0) continue;

                rows.Add(new PerfWaitRow
                {
                    WaitType = waitType,
                    WaitTimeMsDelta = delta.Value.WaitTimeMs,
                    SignalWaitMsDelta = delta.Value.SignalWaitTimeMs,
                    WaitingTasksDelta = delta.Value.WaitingTasks,
                    WaitTimeMsTotal = sample.WaitTimeMs,
                    WaitingTasksTotal = sample.WaitingTasks,
                    MaxWaitTimeMs = Long(reader, "max_wait_time_ms") ?? 0
                });
            }
        }

        tracker.StoreWaits(current);

        long totalDelta = 0;
        foreach (var row in rows) totalDelta += row.WaitTimeMsDelta;
        foreach (var row in rows) row.PercentOfTotal = totalDelta > 0 ? row.WaitTimeMsDelta * 100d / totalDelta : 0;

        rows.Sort((a, b) => b.WaitTimeMsDelta.CompareTo(a.WaitTimeMsDelta));
        snapshot.Waits.AddRange(rows);
    }

    // =====================================================================================================
    // Top queries
    // =====================================================================================================

    /// <summary>
    /// Only columns present since SQL Server 2008 are selected — total_grant_kb and total_dop would restrict
    /// this to 2016+ for the sake of two columns.
    /// </summary>
    internal static string TopQueriesSql(PerfQueryMetric metric) => $@"
SELECT TOP (@top)
    qs.execution_count,
    qs.total_worker_time  / 1000.0                          AS total_cpu_ms,
    qs.total_elapsed_time / 1000.0                          AS total_duration_ms,
    qs.max_elapsed_time   / 1000.0                          AS max_duration_ms,
    qs.total_logical_reads,
    qs.total_logical_writes,
    qs.total_physical_reads,
    qs.creation_time,
    qs.last_execution_time,
    DB_NAME(CONVERT(int, pa.value))                         AS database_name,
    SUBSTRING(t.text, (qs.statement_start_offset / 2) + 1,
        ((CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(t.text)
                                       ELSE qs.statement_end_offset END - qs.statement_start_offset) / 2) + 1) AS statement_text,
    CONVERT(varchar(34), qs.query_hash, 1)                  AS query_hash,
    CONVERT(varchar(130), qs.plan_handle, 1)                AS plan_key
FROM sys.dm_exec_query_stats AS qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS t
OUTER APPLY sys.dm_exec_plan_attributes(qs.plan_handle) AS pa
WHERE pa.attribute = N'dbid'
ORDER BY {OrderByFor(metric)} DESC;
";

    private static string OrderByFor(PerfQueryMetric metric)
    {
        switch (metric)
        {
            case PerfQueryMetric.TotalCpu: return "qs.total_worker_time";
            case PerfQueryMetric.AvgDuration: return "(qs.total_elapsed_time / qs.execution_count)";
            case PerfQueryMetric.TotalDuration: return "qs.total_elapsed_time";
            case PerfQueryMetric.AvgLogicalReads: return "(qs.total_logical_reads / qs.execution_count)";
            case PerfQueryMetric.TotalLogicalReads: return "qs.total_logical_reads";
            case PerfQueryMetric.ExecutionCount: return "qs.execution_count";
            default: return "(qs.total_worker_time / qs.execution_count)";
        }
    }

    private static async Task ReadTopQueriesAsync(SqlConnection conn, PerfSnapshot snapshot, PerfQueryMetric metric, int top, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(TopQueriesSql(metric), conn) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@top", SqlDbType.Int).Value = top;

            using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                int ordinal = 0;
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    long executions = Long(reader, "execution_count") ?? 1;
                    if (executions <= 0) executions = 1;

                    double totalCpu = Double(reader, "total_cpu_ms") ?? 0;
                    double totalDuration = Double(reader, "total_duration_ms") ?? 0;
                    long totalReads = Long(reader, "total_logical_reads") ?? 0;

                    snapshot.Queries.Add(new PerfQueryRow
                    {
                        // plan_handle is unique per cached plan, but the same plan can appear once per
                        // statement, so the ordinal keeps the merge key unique.
                        Key = (Str(reader, "plan_key") ?? "?") + "|" + ordinal++,
                        DatabaseName = Str(reader, "database_name"),
                        ExecutionCount = executions,
                        TotalCpuMs = totalCpu,
                        AvgCpuMs = totalCpu / executions,
                        TotalDurationMs = totalDuration,
                        AvgDurationMs = totalDuration / executions,
                        MaxDurationMs = Double(reader, "max_duration_ms") ?? 0,
                        TotalLogicalReads = totalReads,
                        AvgLogicalReads = totalReads / executions,
                        TotalLogicalWrites = Long(reader, "total_logical_writes") ?? 0,
                        TotalPhysicalReads = Long(reader, "total_physical_reads") ?? 0,
                        CreationTime = Date(reader, "creation_time"),
                        LastExecutionTime = Date(reader, "last_execution_time"),
                        StatementText = Str(reader, "statement_text"),
                        QueryHash = Str(reader, "query_hash")
                    });
                }
            }
        }
    }

    // =====================================================================================================
    // File I/O
    // =====================================================================================================

    internal const string FileStatsSql = @"
SELECT
    vfs.database_id,
    vfs.file_id,
    DB_NAME(vfs.database_id) AS database_name,
    mf.name                  AS logical_name,
    mf.physical_name,
    mf.type_desc,
    vfs.num_of_reads,
    vfs.num_of_writes,
    vfs.io_stall_read_ms,
    vfs.io_stall_write_ms,
    vfs.num_of_bytes_read,
    vfs.num_of_bytes_written,
    vfs.size_on_disk_bytes
FROM sys.dm_io_virtual_file_stats(NULL, NULL) AS vfs
JOIN sys.master_files AS mf
  ON mf.database_id = vfs.database_id AND mf.file_id = vfs.file_id;
";

    private static async Task<Dictionary<string, PerfDeltaTracker.FileSample>> ReadFileSamplesAsync(SqlDataReader reader, CancellationToken ct)
    {
        var samples = new Dictionary<string, PerfDeltaTracker.FileSample>(StringComparer.Ordinal);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            string key = $"{Int(reader, "database_id")}|{Int(reader, "file_id")}";
            samples[key] = new PerfDeltaTracker.FileSample
            {
                Reads = Long(reader, "num_of_reads") ?? 0,
                Writes = Long(reader, "num_of_writes") ?? 0,
                ReadStallMs = Long(reader, "io_stall_read_ms") ?? 0,
                WriteStallMs = Long(reader, "io_stall_write_ms") ?? 0,
                BytesRead = Long(reader, "num_of_bytes_read") ?? 0,
                BytesWritten = Long(reader, "num_of_bytes_written") ?? 0
            };
        }

        return samples;
    }

    private static async Task ReadFileStatsAsync(SqlConnection conn, PerfDeltaTracker tracker, PerfSnapshot snapshot, CancellationToken ct)
    {
        var current = new Dictionary<string, PerfDeltaTracker.FileSample>(StringComparer.Ordinal);

        using (var cmd = new SqlCommand(FileStatsSql, conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                int databaseId = Int(reader, "database_id") ?? 0;
                int fileId = Int(reader, "file_id") ?? 0;
                string key = $"{databaseId}|{fileId}";

                var sample = new PerfDeltaTracker.FileSample
                {
                    Reads = Long(reader, "num_of_reads") ?? 0,
                    Writes = Long(reader, "num_of_writes") ?? 0,
                    ReadStallMs = Long(reader, "io_stall_read_ms") ?? 0,
                    WriteStallMs = Long(reader, "io_stall_write_ms") ?? 0,
                    BytesRead = Long(reader, "num_of_bytes_read") ?? 0,
                    BytesWritten = Long(reader, "num_of_bytes_written") ?? 0
                };
                current[key] = sample;

                var delta = tracker.DeltaFor(key, sample);
                if (delta == null) continue;

                // A file with no I/O this interval has no latency to report — leaving it null keeps the grid
                // honest instead of implying a perfect zero-millisecond disk.
                var row = new PerfFileRow
                {
                    DatabaseId = databaseId,
                    FileId = fileId,
                    DatabaseName = Str(reader, "database_name") ?? $"(database {databaseId})",
                    LogicalName = Str(reader, "logical_name"),
                    PhysicalName = Str(reader, "physical_name"),
                    FileType = Str(reader, "type_desc"),
                    ReadsDelta = delta.Value.Reads,
                    WritesDelta = delta.Value.Writes,
                    BytesReadDelta = delta.Value.BytesRead,
                    BytesWrittenDelta = delta.Value.BytesWritten,
                    SizeOnDiskBytes = Long(reader, "size_on_disk_bytes") ?? 0,
                    ReadLatencyMs = delta.Value.Reads > 0 ? delta.Value.ReadStallMs / (double)delta.Value.Reads : (double?)null,
                    WriteLatencyMs = delta.Value.Writes > 0 ? delta.Value.WriteStallMs / (double)delta.Value.Writes : (double?)null
                };

                if (row.ReadsDelta > 0 || row.WritesDelta > 0)
                    snapshot.Files.Add(row);
            }
        }

        tracker.StoreFiles(current);
        snapshot.Files.Sort((a, b) => (b.ReadsDelta + b.WritesDelta).CompareTo(a.ReadsDelta + a.WritesDelta));
    }

    // =====================================================================================================
    // Reader helpers
    // =====================================================================================================

    private static string Str(SqlDataReader reader, string name)
    {
        int i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
    }

    private static int? Int(SqlDataReader reader, string name)
    {
        int i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? (int?)null : Convert.ToInt32(reader.GetValue(i));
    }

    private static long? Long(SqlDataReader reader, string name)
    {
        int i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? (long?)null : Convert.ToInt64(reader.GetValue(i));
    }

    private static double? Double(SqlDataReader reader, string name)
    {
        int i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? (double?)null : Convert.ToDouble(reader.GetValue(i));
    }

    private static bool? Bool(SqlDataReader reader, string name)
    {
        int i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? (bool?)null : Convert.ToBoolean(reader.GetValue(i));
    }

    private static DateTime? Date(SqlDataReader reader, string name)
    {
        int i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? (DateTime?)null : Convert.ToDateTime(reader.GetValue(i));
    }
}
