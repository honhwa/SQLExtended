using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SQLExtended.Monitoring.Replication;

/// <summary>
/// What replication role this instance plays, and which of the distribution database's tables and columns are
/// actually there.
///
/// Replication has no equivalent of SERVERPROPERTY('IsHadrEnabled'), so the role has to be inferred from
/// sys.databases: <c>is_distributor</c> marks the distribution database, <c>is_published</c> /
/// <c>is_merge_published</c> mark publisher databases, <c>is_subscribed</c> marks subscriber databases. An
/// instance can be all three at once, which is why these are independent flags rather than an enum.
///
/// The column probes exist for the same reason as the Always On ones: the <c>MS*_history</c> tables have gained
/// columns across releases (<c>current_delivery_latency</c> alongside the older <c>delivery_latency</c>), and
/// asking the catalog is more reliable than branching on a version number that SP and CU levels blur.
/// </summary>
/// <remarks>
/// The setters are <c>internal</c> rather than <c>private</c> so the diagnostic rules can be unit tested against
/// a hand-built capability set. Nothing outside <see cref="ProbeAsync"/> writes them in the product.
/// </remarks>
internal sealed class ReplCapabilities
{
    public string ServerName { get; internal set; }

    /// <summary>The login this connection authenticates as, as the server resolves it — <c>SUSER_SNAME()</c>.</summary>
    public string LoginName { get; internal set; }
    public string ProductVersion { get; internal set; }
    public string Edition { get; internal set; }

    public int PublishedDatabaseCount { get; internal set; }
    public int MergePublishedDatabaseCount { get; internal set; }
    public int SubscribedDatabaseCount { get; internal set; }

    /// <summary>The local distribution database, or null when this instance is not a distributor.</summary>
    public string DistributionDatabase { get; internal set; }

    public bool IsDistributor => !string.IsNullOrEmpty(DistributionDatabase);
    public bool IsPublisher => PublishedDatabaseCount > 0 || MergePublishedDatabaseCount > 0;
    public bool IsSubscriber => SubscribedDatabaseCount > 0;

    /// <summary>
    /// Whether there is anything at all to show. False means replication is not configured on this instance —
    /// which is not an error, and is what the dashboard says instead of showing eight empty grids.
    /// </summary>
    public bool HasAnyRole => IsDistributor || IsPublisher || IsSubscriber;

    // --- distribution database contents, probed in that database's context ---
    public bool HasPublications { get; internal set; }
    public bool HasSubscriptions { get; internal set; }
    public bool HasLogReaderAgents { get; internal set; }
    public bool HasDistributionAgents { get; internal set; }
    public bool HasSnapshotAgents { get; internal set; }
    public bool HasMergeAgents { get; internal set; }

    /// <summary>
    /// Whether MSmerge_history is there. It is a separate flag from <see cref="HasMergeAgents"/> because it is a
    /// separate table from the one the merge agents' sessions come from, and it is the only place a merge agent's
    /// comment and error id exist at all — MSmerge_sessions carries neither.
    /// </summary>
    public bool HasMergeHistory { get; internal set; }
    public bool HasReplErrors { get; internal set; }
    public bool HasTracerTokens { get; internal set; }
    public bool HasDistributionStatusView { get; internal set; }

    public bool HasCurrentDeliveryLatency { get; internal set; }
    public bool HasCurrentDeliveryRate { get; internal set; }
    public bool HasRetentionPeriodUnit { get; internal set; }
    public bool HasMergeSessionConflictCounts { get; internal set; }

    // The full column set of each agent-history table, so the agent queries can substitute a typed NULL for
    // anything a given release does not have. Probed as a set rather than as named flags because these tables
    // have turned out to *lose* columns as well as gain them: on SQL Server 2025 the Agents section failed with
    // "Invalid column name 'comments'. Invalid column name 'error_id'.", and because binding fails for the whole
    // batch that cost the entire tab rather than two columns.
    private readonly Dictionary<string, HashSet<string>> _tableColumns = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <c>dbo.[table]</c> has a column of that name. A table that was never probed — because this is not
    /// a distributor, or a hand-built capability set in a test — answers <c>true</c>, so the generated SQL is
    /// unchanged from what it would have been without the probe. Only a positively-absent column is substituted.
    /// </summary>
    public bool HasColumn(string table, string column) =>
        !_tableColumns.TryGetValue(table, out var columns) || columns.Contains(column);

    /// <summary>Whether msdb was readable, which decides if the agent-job columns can be filled.</summary>
    public bool CanReadJobs { get; internal set; }

    // Run against master. HAS_DBACCESS keeps a database this login cannot touch from being counted as a role we
    // will then fail to read.
    private const string RoleProbeSql = @"
SELECT
    SERVERPROPERTY('ServerName')                             AS server_name,
    SUSER_SNAME()                                            AS login_name,
    CONVERT(nvarchar(64), SERVERPROPERTY('ProductVersion'))  AS product_version,
    CONVERT(nvarchar(128), SERVERPROPERTY('Edition'))        AS edition,
    (SELECT COUNT(*) FROM sys.databases WHERE is_published = 1)       AS published_count,
    (SELECT COUNT(*) FROM sys.databases WHERE is_merge_published = 1) AS merge_published_count,
    (SELECT COUNT(*) FROM sys.databases WHERE is_subscribed = 1)      AS subscribed_count,
    (SELECT TOP (1) name FROM sys.databases WHERE is_distributor = 1 AND HAS_DBACCESS(name) = 1 ORDER BY database_id) AS distribution_db,
    CASE WHEN HAS_DBACCESS('msdb') = 1 AND OBJECT_ID('msdb.dbo.sysjobs') IS NOT NULL THEN 1 ELSE 0 END AS can_read_jobs;";

    // Run in the distribution database. OBJECT_ID resolves unqualified names against the current database, which
    // is exactly what is wanted here — these tables only exist in a distribution database.
    private const string DistributionProbeSql = @"
DECLARE @dh int = OBJECT_ID('dbo.MSdistribution_history');
DECLARE @pb int = OBJECT_ID('dbo.MSpublications');
DECLARE @ms int = OBJECT_ID('dbo.MSmerge_sessions');

SELECT
    CASE WHEN @pb IS NOT NULL                                       THEN 1 ELSE 0 END AS has_publications,
    CASE WHEN OBJECT_ID('dbo.MSsubscriptions')        IS NOT NULL   THEN 1 ELSE 0 END AS has_subscriptions,
    CASE WHEN OBJECT_ID('dbo.MSlogreader_agents')     IS NOT NULL   THEN 1 ELSE 0 END AS has_logreader_agents,
    CASE WHEN OBJECT_ID('dbo.MSdistribution_agents')  IS NOT NULL   THEN 1 ELSE 0 END AS has_distribution_agents,
    CASE WHEN OBJECT_ID('dbo.MSsnapshot_agents')      IS NOT NULL   THEN 1 ELSE 0 END AS has_snapshot_agents,
    CASE WHEN OBJECT_ID('dbo.MSmerge_agents')         IS NOT NULL   THEN 1 ELSE 0 END AS has_merge_agents,
    CASE WHEN OBJECT_ID('dbo.MSmerge_history')        IS NOT NULL   THEN 1 ELSE 0 END AS has_merge_history,
    CASE WHEN OBJECT_ID('dbo.MSrepl_errors')          IS NOT NULL   THEN 1 ELSE 0 END AS has_repl_errors,
    CASE WHEN OBJECT_ID('dbo.MStracer_tokens')        IS NOT NULL
          AND OBJECT_ID('dbo.MStracer_history')       IS NOT NULL   THEN 1 ELSE 0 END AS has_tracer_tokens,
    CASE WHEN OBJECT_ID('dbo.MSdistribution_status')  IS NOT NULL   THEN 1 ELSE 0 END AS has_distribution_status,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @dh AND name = 'current_delivery_latency') THEN 1 ELSE 0 END AS has_current_latency,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @dh AND name = 'current_delivery_rate')    THEN 1 ELSE 0 END AS has_current_rate,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @pb AND name = 'retention_period_unit')    THEN 1 ELSE 0 END AS has_retention_unit,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @ms AND name = 'upload_conflicts')         THEN 1 ELSE 0 END AS has_merge_conflicts;

-- Second result set: every column of every agent-history table, so the agent queries can be built against what
-- is actually there. CROSS APPLY drops a table that does not exist, which leaves it unprobed rather than
-- recorded as having no columns at all.
SELECT t.table_name, c.name AS column_name
FROM (VALUES (N'MSlogreader_history'), (N'MSdistribution_history'), (N'MSsnapshot_history'), (N'MSmerge_sessions'), (N'MSmerge_history')) AS t(table_name)
CROSS APPLY (SELECT name FROM sys.all_columns WHERE object_id = OBJECT_ID(N'dbo.' + t.table_name)) AS c;";

    public static async Task<ReplCapabilities> ProbeAsync(string masterConnectionString, CancellationToken ct)
    {
        var caps = new ReplCapabilities();

        using (var conn = SqlConnectionFactory.Create(masterConnectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            using (var cmd = new SqlCommand(RoleProbeSql, conn) { CommandTimeout = ReplQueryService.CommandTimeoutSeconds })
            using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return caps;

                caps.ServerName = Str(reader, "server_name");
                caps.LoginName = Str(reader, "login_name");
                caps.ProductVersion = Str(reader, "product_version");
                caps.Edition = Str(reader, "edition");
                caps.PublishedDatabaseCount = Int(reader, "published_count");
                caps.MergePublishedDatabaseCount = Int(reader, "merge_published_count");
                caps.SubscribedDatabaseCount = Int(reader, "subscribed_count");
                caps.DistributionDatabase = Str(reader, "distribution_db");
                caps.CanReadJobs = Int(reader, "can_read_jobs") == 1;
            }
        }

        if (caps.IsDistributor)
            await caps.ProbeDistributionAsync(masterConnectionString, ct).ConfigureAwait(false);

        return caps;
    }

    private async Task ProbeDistributionAsync(string masterConnectionString, CancellationToken ct)
    {
        string connectionString = ReplQueryService.BuildMonitorConnectionString(masterConnectionString, DistributionDatabase);

        using (var conn = SqlConnectionFactory.Create(connectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            using (var cmd = new SqlCommand(DistributionProbeSql, conn) { CommandTimeout = ReplQueryService.CommandTimeoutSeconds })
            using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return;

                HasPublications = Int(reader, "has_publications") == 1;
                HasSubscriptions = Int(reader, "has_subscriptions") == 1;
                HasLogReaderAgents = Int(reader, "has_logreader_agents") == 1;
                HasDistributionAgents = Int(reader, "has_distribution_agents") == 1;
                HasSnapshotAgents = Int(reader, "has_snapshot_agents") == 1;
                HasMergeAgents = Int(reader, "has_merge_agents") == 1;
                HasMergeHistory = Int(reader, "has_merge_history") == 1;
                HasReplErrors = Int(reader, "has_repl_errors") == 1;
                HasTracerTokens = Int(reader, "has_tracer_tokens") == 1;
                HasDistributionStatusView = Int(reader, "has_distribution_status") == 1;
                HasCurrentDeliveryLatency = Int(reader, "has_current_latency") == 1;
                HasCurrentDeliveryRate = Int(reader, "has_current_rate") == 1;
                HasRetentionPeriodUnit = Int(reader, "has_retention_unit") == 1;
                HasMergeSessionConflictCounts = Int(reader, "has_merge_conflicts") == 1;

                if (await reader.NextResultAsync(ct).ConfigureAwait(false))
                    await ReadTableColumnsAsync(reader, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ReadTableColumnsAsync(SqlDataReader reader, CancellationToken ct)
    {
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            string table = Str(reader, "table_name");
            string column = Str(reader, "column_name");
            if (table == null || column == null) continue;

            if (!_tableColumns.TryGetValue(table, out var columns))
                _tableColumns[table] = columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            columns.Add(column);
        }
    }

    /// <summary>A one-line description of the roles found, for the Overview and the status bar.</summary>
    public string DescribeRoles()
    {
        if (!HasAnyRole) return "no replication role";

        var parts = new System.Collections.Generic.List<string>();
        if (IsDistributor) parts.Add("distributor (" + DistributionDatabase + ")");
        if (IsPublisher) parts.Add($"publisher ({PublishedDatabaseCount + MergePublishedDatabaseCount} database(s))");
        if (IsSubscriber) parts.Add($"subscriber ({SubscribedDatabaseCount} database(s))");
        return string.Join(", ", parts);
    }

    private static string Str(SqlDataReader reader, string name)
    {
        int i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
    }

    private static int Int(SqlDataReader reader, string name)
    {
        int i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? 0 : Convert.ToInt32(reader.GetValue(i));
    }

    /// <summary>
    /// Emits <c>expr AS alias</c> when the column exists and a fallback expression when it does not, so the
    /// reader can always address every column by name regardless of the target version.
    /// </summary>
    public static string Column(bool present, string expression, string fallback, string alias) =>
        present ? $"{expression} AS {alias}" : $"{fallback} AS {alias}";
}
