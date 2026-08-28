using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SQLExtended.Monitoring.Jobs;

/// <summary>
/// Reads SQL Server Agent state out of msdb. Everything here runs on a background thread against the window's
/// pinned connection.
///
/// Two deliberate differences from the other two monitoring dashboards:
///  * The connection is forced to <c>msdb</c>, not <c>master</c> — every table read here lives there.
///  * The permission needed is <c>SQLAgentReaderRole</c> (or sysadmin), not VIEW SERVER STATE. A login with
///    neither sees only the jobs it owns, and a short list that looks complete is worse than no list, so
///    <see cref="CollectAsync"/> probes role membership and records a warning when it is missing.
///
/// The three result sets are collected in one round trip and stitched together on job_id: sysjobs supplies
/// the metadata, sysjobactivity the live state, sysjobhistory the last-run and average figures. Each is read
/// inside its own try/catch so an unexpected difference in one table costs a few columns rather than the grid.
/// </summary>
internal static class JobQueryService
{
    internal const int CommandTimeoutSeconds = 20;

    /// <summary>
    /// Normalises a connection string harvested from SSMS for jobs monitoring: msdb, short timeout, and an
    /// application name that shows up usefully in sys.dm_exec_sessions on the monitored server.
    /// </summary>
    public static string BuildMonitorConnectionString(string baseConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "msdb",
            ApplicationName = "SQLExtended Agent Jobs",
            ConnectTimeout = 10
        };
        return builder.ConnectionString;
    }


    // Integer date/duration decoding lives in JobValueParser so the tests can link it without SqlClient.
    private static DateTime? ToDateTime(int runDate, int runTime) => JobValueParser.ToDateTime(runDate, runTime);
    private static int DurationToSeconds(int runDuration) => JobValueParser.DurationToSeconds(runDuration);

    // -------------------------------------------------------------------------------------------------
    // Collection
    // -------------------------------------------------------------------------------------------------

    /// <param name="hiddenCategories">
    /// Categories to flag as hidden. They are still fetched — the grid filters them out through its
    /// CollectionView, so the toolbar toggle costs no round trip and the status line can report the count.
    /// </param>
    /// <param name="progress">Reports each section as it starts, for the status line. Null on the timer polls.</param>
    /// <param name="onJobsReady">
    /// Awaited once the job list and its current activity are in, before the run history is read — see
    /// <see cref="MonitorPlan"/>. <c>sysjobhistory</c> is the largest table in msdb on a busy instance and the
    /// slowest read here by a distance, and it fills in two columns of a grid that is otherwise complete.
    /// </param>
    public static async Task<JobsSnapshot> CollectAsync(string connectionString, IReadOnlyCollection<string> hiddenCategories, int averageSampleRuns,
                                                        IProgress<MonitorStep> progress, Func<JobsSnapshot, Task> onJobsReady, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var snapshot = new JobsSnapshot { CollectedAtLocal = DateTime.Now };
        var byId = new Dictionary<Guid, JobRow>();

        using (var conn = SqlConnectionFactory.Create(connectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);

            if (!await ReadProbeAsync(conn, snapshot, ct).ConfigureAwait(false))
                return Finish(snapshot, started);

            // Each section gets its own command rather than sharing one multi-statement batch. A permission
            // error on any one msdb table (a login outside SQLAgentReaderRole can hit several) would take the
            // rest of a batch's result sets with it, and losing the whole grid over one missing grant is
            // exactly what the per-section try/catch is supposed to prevent. Four commands on an already-open
            // connection cost nothing worth measuring.
            var plan = new MonitorPlan(progress, snapshot.Warnings.Add)
                .Add("jobs", () => ReadJobsAsync(conn, byId, hiddenCategories, ct), primary: true)
                .Add("job activity", () => ReadActivityAsync(conn, byId, snapshot, ct), primary: true)
                .Add("run history", () => ReadHistorySummaryAsync(conn, byId, averageSampleRuns, ct));

            await plan.RunAsync(async () =>
            {
                SortJobs(snapshot, byId);
                if (onJobsReady != null) await onJobsReady(snapshot).ConfigureAwait(false);
            }).ConfigureAwait(false);

            snapshot.SectionsRead = plan.Ran;
            snapshot.SectionsFailed = plan.Failed;
        }

        SortJobs(snapshot, byId);
        return Finish(snapshot, started);
    }

    /// <summary>
    /// Publishes the collected rows in name order. Idempotent — it is called once as soon as the grid has enough
    /// to be shown and again at the end, and the later sections enrich the same row instances rather than
    /// replacing them, so re-publishing costs nothing and keeps the two calls honest.
    /// </summary>
    private static void SortJobs(JobsSnapshot snapshot, Dictionary<Guid, JobRow> byId)
    {
        snapshot.Jobs.Clear();
        snapshot.Jobs.AddRange(byId.Values.OrderBy(j => j.Name, StringComparer.OrdinalIgnoreCase));
    }

    private static JobsSnapshot Finish(JobsSnapshot snapshot, DateTime started)
    {
        snapshot.Duration = DateTime.UtcNow - started;
        return snapshot;
    }

    private static async Task<bool> ReadProbeAsync(SqlConnection conn, JobsSnapshot snapshot, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(ProbeSql, conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                snapshot.UnavailableReason = "The server did not respond to the Agent probe.";
                return false;
            }

            snapshot.ServerName = reader["server_name"] as string;
            snapshot.LoginName = reader["login_name"] as string;

            if (Convert.ToInt32(reader["has_agent_tables"], CultureInfo.InvariantCulture) == 0)
            {
                snapshot.UnavailableReason = "SQL Server Agent is not installed on this instance (msdb.dbo.sysjobs does not exist). "
                                           + "Azure SQL Database and some editions do not provide Agent.";
                return false;
            }

            bool sysadmin = Convert.ToInt32(reader["is_sysadmin"], CultureInfo.InvariantCulture) == 1;
            bool agentReader = Convert.ToInt32(reader["is_agent_reader"], CultureInfo.InvariantCulture) == 1;
            bool agentOperator = Convert.ToInt32(reader["is_agent_operator"], CultureInfo.InvariantCulture) == 1;

            // Without one of these msdb silently returns only the jobs this login owns. A short list that looks
            // complete is the worst outcome here, so say so rather than letting it pass as "few jobs on this box".
            if (!sysadmin && !agentReader && !agentOperator)
            {
                snapshot.Warnings.Add("this login is not sysadmin and not a member of SQLAgentReaderRole or SQLAgentOperatorRole in msdb, "
                                    + "so only jobs it owns are visible");
            }
        }

        return true;
    }

    private static async Task ReadJobsAsync(SqlConnection conn, Dictionary<Guid, JobRow> byId, IReadOnlyCollection<string> hiddenCategories, CancellationToken ct)
    {
        var hidden = new HashSet<string>(hiddenCategories ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        using (var cmd = new SqlCommand(JobsSql, conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var jobId = reader.GetGuid(reader.GetOrdinal("job_id"));
                string category = reader["category_name"] as string;

                byId[jobId] = new JobRow
                {
                    JobId = jobId,
                    Name = reader["name"] as string,
                    IsEnabled = Convert.ToInt32(reader["enabled"], CultureInfo.InvariantCulture) == 1,
                    Category = category,
                    IsHiddenCategory = category != null && hidden.Contains(category),
                    Owner = reader["owner_name"] as string,
                    Description = reader["description"] as string,
                    DateCreated = reader["date_created"] as DateTime?,
                    StepCount = Convert.ToInt32(reader["step_count"], CultureInfo.InvariantCulture),
                    NotifyLevelEmail = Convert.ToInt32(reader["notify_level_email"], CultureInfo.InvariantCulture),
                    NotifyOperator = reader["operator_name"] as string,
                    NotifyEmailAddress = reader["operator_email"] as string
                };
            }
        }
    }

    private static async Task ReadActivityAsync(SqlConnection conn, Dictionary<Guid, JobRow> byId, JobsSnapshot snapshot, CancellationToken ct)
    {
        int rows = 0;

        using (var cmd = new SqlCommand(ActivitySql, conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows++;
                var jobId = reader.GetGuid(reader.GetOrdinal("job_id"));
                if (!byId.TryGetValue(jobId, out var job)) continue;

                job.StartExecutionDate = reader["start_execution_date"] as DateTime?;
                job.StopExecutionDate = reader["stop_execution_date"] as DateTime?;
                job.NextRunDate = reader["next_scheduled_run_date"] as DateTime?;
                job.CurrentStepId = reader["last_executed_step_id"] as int?;
                job.CurrentStepName = reader["step_name"] as string;
                job.ElapsedSeconds = reader["elapsed_seconds"] as int?;
            }
        }

        // Agent writes a sysjobactivity row per job as soon as it starts a session, so no rows at all while
        // jobs exist means it has never run here. Without saying that, the blank Status and Next run columns
        // look like a bug in the dashboard rather than a stopped service.
        if (rows == 0 && byId.Count > 0)
            snapshot.Warnings.Add("SQL Server Agent has no session recorded on this instance — Status, Current step and Next run stay blank until it starts");
    }

    private static async Task ReadHistorySummaryAsync(SqlConnection conn, Dictionary<Guid, JobRow> byId, int averageSampleRuns, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(HistorySummarySql, conn) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@avgSamples", SqlDbType.Int).Value = Math.Max(1, averageSampleRuns);

            using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var jobId = reader.GetGuid(reader.GetOrdinal("job_id"));
                    if (!byId.TryGetValue(jobId, out var job)) continue;

                    job.LastRunOutcome = ToOutcome(reader["last_run_status"]);
                    job.LastRunDate = ToDateTime(AsInt(reader["last_run_date"]), AsInt(reader["last_run_time"]));
                    job.LastRunDurationSeconds = reader["last_run_duration"] == DBNull.Value ? (int?)null : DurationToSeconds(AsInt(reader["last_run_duration"]));
                    job.LastRunMessage = reader["last_message"] as string;

                    // Averaged over successful runs only — a job that failed after two seconds would otherwise
                    // drag the "how long does this normally take" number somewhere that answers no question.
                    job.AverageDurationSeconds = reader["avg_duration_seconds"] == DBNull.Value
                        ? (double?)null
                        : Convert.ToDouble(reader["avg_duration_seconds"], CultureInfo.InvariantCulture);
                }
            }
        }
    }

    private static int AsInt(object value) => value == DBNull.Value || value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static JobRunOutcome ToOutcome(object value)
    {
        if (value == DBNull.Value || value == null) return JobRunOutcome.Unknown;
        int status = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return Enum.IsDefined(typeof(JobRunOutcome), status) ? (JobRunOutcome)status : JobRunOutcome.Unknown;
    }

    // -------------------------------------------------------------------------------------------------
    // On-demand detail for the selected job. Neither of these sits on the refresh timer: sysjobhistory is
    // the largest table in msdb on a busy instance and reading it every few seconds for a row the user may
    // not be looking at is not a trade worth making.
    // -------------------------------------------------------------------------------------------------

    public static async Task<List<JobStepRow>> GetStepsAsync(string connectionString, Guid jobId, CancellationToken ct)
    {
        var steps = new List<JobStepRow>();

        using (var conn = SqlConnectionFactory.Create(connectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            using (var cmd = new SqlCommand(StepsSql, conn) { CommandTimeout = CommandTimeoutSeconds })
            {
                cmd.Parameters.Add("@jobId", SqlDbType.UniqueIdentifier).Value = jobId;

                using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        steps.Add(new JobStepRow
                        {
                            StepId = Convert.ToInt32(reader["step_id"], CultureInfo.InvariantCulture),
                            StepName = reader["step_name"] as string,
                            Subsystem = reader["subsystem"] as string,
                            DatabaseName = reader["database_name"] as string,
                            ProxyName = reader["proxy_name"] as string,
                            OnSuccessAction = StepAction(AsInt(reader["on_success_action"]), reader["on_success_step_id"]),
                            OnFailAction = StepAction(AsInt(reader["on_fail_action"]), reader["on_fail_step_id"]),
                            RetryAttempts = AsInt(reader["retry_attempts"]),
                            RetryIntervalMinutes = AsInt(reader["retry_interval"]),
                            Command = reader["command"] as string,
                            LastRunOutcome = ToOutcome(reader["last_run_outcome"]),
                            LastRunDate = ToDateTime(AsInt(reader["last_run_date"]), AsInt(reader["last_run_time"])),
                            LastRunDurationSeconds = DurationToSeconds(AsInt(reader["last_run_duration"]))
                        });
                    }
                }
            }
        }

        return steps;
    }

    /// <summary>sysjobsteps.on_success_action / on_fail_action codes.</summary>
    private static string StepAction(int action, object targetStepId)
    {
        switch (action)
        {
            case 1: return "Quit with success";
            case 2: return "Quit with failure";
            case 3: return "Go to next step";
            case 4: return "Go to step " + AsInt(targetStepId).ToString(CultureInfo.CurrentCulture);
            default: return action.ToString(CultureInfo.CurrentCulture);
        }
    }

    public static async Task<List<JobHistoryRow>> GetHistoryAsync(string connectionString, Guid jobId, int days, int maxRows, CancellationToken ct)
    {
        var history = new List<JobHistoryRow>();

        // sysjobhistory.run_date is an integer, so the cut-off has to be one too rather than a date parameter.
        int minRunDate = int.Parse(DateTime.Today.AddDays(-Math.Max(1, days)).ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        using (var conn = SqlConnectionFactory.Create(connectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            using (var cmd = new SqlCommand(HistorySql, conn) { CommandTimeout = CommandTimeoutSeconds })
            {
                cmd.Parameters.Add("@jobId", SqlDbType.UniqueIdentifier).Value = jobId;
                cmd.Parameters.Add("@minRunDate", SqlDbType.Int).Value = minRunDate;
                cmd.Parameters.Add("@top", SqlDbType.Int).Value = Math.Max(1, maxRows);

                using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        history.Add(new JobHistoryRow
                        {
                            StepId = AsInt(reader["step_id"]),
                            StepName = reader["step_name"] as string,
                            RunStatus = ToOutcome(reader["run_status"]),
                            RunDate = ToDateTime(AsInt(reader["run_date"]), AsInt(reader["run_time"])),
                            DurationSeconds = DurationToSeconds(AsInt(reader["run_duration"])),
                            RetriesAttempted = AsInt(reader["retries_attempted"]),
                            ServerName = reader["server"] as string,
                            Message = reader["message"] as string
                        });
                    }
                }
            }
        }

        return history;
    }

    // -------------------------------------------------------------------------------------------------
    // SQL. Exposed so the toolbar's "Open as query" can hand the user exactly what the tab ran.
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server identity, whether Agent exists at all, and whether this login can see every job. Deliberately
    /// touches nothing under msdb.dbo — this has to succeed for the "you can only see your own jobs" warning
    /// to be reportable at all.
    /// </summary>
    internal const string ProbeSql = @"
SET NOCOUNT ON;
SELECT
    CONVERT(nvarchar(128), SERVERPROPERTY('ServerName'))                    AS server_name,
    SUSER_SNAME()                                                           AS login_name,
    CASE WHEN OBJECT_ID('msdb.dbo.sysjobs') IS NULL THEN 0 ELSE 1 END       AS has_agent_tables,
    CONVERT(int, ISNULL(IS_SRVROLEMEMBER('sysadmin'), 0))                   AS is_sysadmin,
    CONVERT(int, ISNULL(IS_ROLEMEMBER('SQLAgentReaderRole'), 0))            AS is_agent_reader,
    CONVERT(int, ISNULL(IS_ROLEMEMBER('SQLAgentOperatorRole'), 0))          AS is_agent_operator;";

    /// <summary>
    /// Job metadata. The operator join is what supplies the notification e-mail column; notify_level_email
    /// comes with it because an operator on level 0 is configured but never actually mailed.
    /// </summary>
    internal const string JobsSql = @"
SET NOCOUNT ON;
SELECT
    j.job_id,
    j.name,
    j.enabled,
    j.description,
    j.date_created,
    j.notify_level_email,
    SUSER_SNAME(j.owner_sid)                                                AS owner_name,
    c.name                                                                  AS category_name,
    o.name                                                                  AS operator_name,
    o.email_address                                                         AS operator_email,
    (SELECT COUNT(*) FROM msdb.dbo.sysjobsteps AS s WHERE s.job_id = j.job_id) AS step_count
FROM msdb.dbo.sysjobs AS j
LEFT JOIN msdb.dbo.syscategories AS c ON c.category_id = j.category_id
LEFT JOIN msdb.dbo.sysoperators  AS o ON o.id = j.notify_email_operator_id
ORDER BY j.name;";

    /// <summary>
    /// Live state for the current Agent session. This is the same source Job Activity Monitor uses, and it is
    /// why the dashboard needs no schedule decoding: next_scheduled_run_date is already a datetime. Elapsed is
    /// computed server-side against GETDATE() so a client clock that differs cannot produce a negative.
    /// </summary>
    internal const string ActivitySql = @"
SET NOCOUNT ON;
SELECT
    ja.job_id,
    ja.start_execution_date,
    ja.stop_execution_date,
    ja.next_scheduled_run_date,
    ja.last_executed_step_id,
    s.step_name,
    CASE WHEN ja.start_execution_date IS NOT NULL AND ja.stop_execution_date IS NULL
         THEN DATEDIFF(second, ja.start_execution_date, GETDATE()) END      AS elapsed_seconds
FROM msdb.dbo.sysjobactivity AS ja
LEFT JOIN msdb.dbo.sysjobsteps AS s
       ON s.job_id = ja.job_id AND s.step_id = ja.last_executed_step_id
WHERE ja.session_id = (SELECT MAX(session_id) FROM msdb.dbo.syssessions);";

    /// <summary>
    /// Last run and average duration. step_id = 0 is the job-level summary row Agent writes after the last
    /// step, so one row per execution rather than one per step. Dates and durations stay as the integers Agent
    /// stores; they are decoded client-side (see <see cref="JobValueParser"/>).
    /// </summary>
    internal const string HistorySummarySql = @"
SET NOCOUNT ON;
WITH job_runs AS (
    SELECT
        h.job_id,
        h.run_status,
        h.run_date,
        h.run_time,
        h.run_duration,
        h.message,
        ROW_NUMBER() OVER (PARTITION BY h.job_id ORDER BY h.instance_id DESC) AS rn
    FROM msdb.dbo.sysjobhistory AS h
    WHERE h.step_id = 0
)
SELECT
    r.job_id,
    MAX(CASE WHEN r.rn = 1 THEN r.run_status   END)                         AS last_run_status,
    MAX(CASE WHEN r.rn = 1 THEN r.run_date     END)                         AS last_run_date,
    MAX(CASE WHEN r.rn = 1 THEN r.run_time     END)                         AS last_run_time,
    MAX(CASE WHEN r.rn = 1 THEN r.run_duration END)                         AS last_run_duration,
    MAX(CASE WHEN r.rn = 1 THEN r.message      END)                         AS last_message,
    AVG(CASE WHEN r.run_status = 1
             THEN CONVERT(float, r.run_duration / 10000 * 3600 + r.run_duration / 100 % 100 * 60 + r.run_duration % 100)
        END)                                                                AS avg_duration_seconds
FROM job_runs AS r
WHERE r.rn <= @avgSamples
GROUP BY r.job_id;";

    internal const string StepsSql = @"
SET NOCOUNT ON;
SELECT
    s.step_id,
    s.step_name,
    s.subsystem,
    s.database_name,
    p.name AS proxy_name,
    s.on_success_action,
    s.on_success_step_id,
    s.on_fail_action,
    s.on_fail_step_id,
    s.retry_attempts,
    s.retry_interval,
    s.command,
    s.last_run_outcome,
    s.last_run_date,
    s.last_run_time,
    s.last_run_duration
FROM msdb.dbo.sysjobsteps AS s
LEFT JOIN msdb.dbo.sysproxies AS p ON p.proxy_id = s.proxy_id
WHERE s.job_id = @jobId
ORDER BY s.step_id;";

    internal const string HistorySql = @"
SET NOCOUNT ON;
SELECT TOP (@top)
    h.step_id,
    h.step_name,
    h.run_status,
    h.run_date,
    h.run_time,
    h.run_duration,
    h.retries_attempted,
    h.server,
    h.message
FROM msdb.dbo.sysjobhistory AS h
WHERE h.job_id = @jobId
  AND h.run_date >= @minRunDate
ORDER BY h.instance_id DESC;";

    /// <summary>
    /// The four collection queries as one script, with the parameter declared, for "Open as query". The
    /// dashboard runs them as separate commands so one permission failure cannot take the others with it, but
    /// what the user gets handed should run as-is in a single window.
    /// </summary>
    public static string CollectSqlForDisplay(int averageSampleRuns) =>
        new StringBuilder()
            .AppendLine("DECLARE @avgSamples int = " + Math.Max(1, averageSampleRuns).ToString(CultureInfo.InvariantCulture) + ";")
            .AppendLine(ProbeSql)
            .AppendLine(JobsSql)
            .AppendLine(ActivitySql)
            .AppendLine(HistorySummarySql)
            .ToString();

    public static string StepsSqlForDisplay(Guid jobId) =>
        "DECLARE @jobId uniqueidentifier = '" + jobId.ToString() + "';" + Environment.NewLine + StepsSql;

    public static string HistorySqlForDisplay(Guid jobId, int days, int maxRows) =>
        "DECLARE @jobId uniqueidentifier = '" + jobId.ToString() + "';" + Environment.NewLine
        + "DECLARE @minRunDate int = " + DateTime.Today.AddDays(-Math.Max(1, days)).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ";" + Environment.NewLine
        + "DECLARE @top int = " + Math.Max(1, maxRows).ToString(CultureInfo.InvariantCulture) + ";" + Environment.NewLine
        + HistorySql;
}
