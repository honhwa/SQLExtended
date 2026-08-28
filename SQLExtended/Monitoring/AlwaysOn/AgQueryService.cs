using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace SQLExtended.Monitoring.AlwaysOn;

/// <summary>
/// Reads Always On state out of the HADR DMVs. Everything here runs on a background thread against the window's
/// pinned connection, and needs only VIEW SERVER STATE.
///
/// Two deliberate choices:
///  * The connection is always forced to <c>master</c>. The connection it was pinned from may well name a
///    non-readable secondary's database, and connecting to that database would fail before we could read anything.
///  * Each section is collected in its own try/catch and records a warning rather than throwing. The HADR
///    DMVs vary across releases in ways <see cref="AgCapabilities"/> can only partly predict, and a
///    surprise on one view should cost one tab, not the whole dashboard.
/// </summary>
internal static class AgQueryService
{
    internal const int CommandTimeoutSeconds = 20;

    /// <summary>
    /// Normalises a connection string harvested from SSMS for monitoring use: master, short timeout, and an
    /// application name that shows up usefully in sys.dm_exec_sessions on the monitored server.
    /// </summary>
    public static string BuildMonitorConnectionString(string baseConnectionString)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = "master",
            ApplicationName = "SQLExtended AG Monitor",
            ConnectTimeout = 10
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// How long the first poll of a server waits between the two counter readings the throughput rates are
    /// derived from. Paid once per server, not per poll: without it every rate column on the Throughput tab
    /// would read as a dash until the second poll, which is the same choice the Performance dashboard makes.
    /// </summary>
    private const int BaselineSampleMs = 1000;

    /// <param name="progress">Reports each section as it starts, for the status line. Null on the timer polls.</param>
    /// <param name="onOverviewReady">
    /// Awaited once the three sections the Overview tab is built from have been read, before the tabs behind it are
    /// collected — see <see cref="MonitorPlan"/>. The group cards and the attention list are the reason this window
    /// was opened; on an instance where the seeding or counter DMVs are slow they used to wait on them for nothing.
    /// </param>
    public static async Task<AgSnapshot> CollectAsync(string connectionString, AgCapabilities caps, AgCounterTracker counters, AgThresholds thresholds,
                                                      IProgress<MonitorStep> progress, Func<AgSnapshot, Task> onOverviewReady, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var snapshot = new AgSnapshot { ServerName = caps.ServerName, LoginName = caps.LoginName, CollectedAtLocal = DateTime.Now };

        // The throughput rows hold this for their own tinting, so a null would surface as a crash in the grid
        // rather than here.
        thresholds = thresholds ?? new AgThresholds();

        if (!caps.IsHadrEnabled)
        {
            snapshot.UnavailableReason = "Always On Availability Groups is not enabled on this instance (SERVERPROPERTY('IsHadrEnabled') = 0).";
            return snapshot;
        }

        using (var conn = SqlConnectionFactory.Create(connectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);

            var plan = new MonitorPlan(progress, snapshot.Warnings.Add)
                .Add("availability groups", () => ReadGroupsAsync(conn, caps, snapshot, ct), primary: true)
                .Add("replica states", () => ReadReplicasAsync(conn, caps, snapshot, ct), primary: true)
                .Add("database replica states", () => ReadDatabasesAsync(conn, caps, snapshot, ct), primary: true)
                .AddIf(caps.HasClusterView, "cluster and quorum", () => ReadClusterAsync(conn, caps, snapshot, ct))
                .AddIf(caps.HasReplicaClusterNodes, "replica cluster nodes", () => ReadClusterNodesAsync(conn, caps, snapshot, ct))
                .AddIf(caps.HasListeners, "listeners", () => ReadListenersAsync(conn, caps, snapshot, ct))
                .AddIf(caps.HasReadOnlyRoutingLists, "read-only routing", () => ReadRoutingAsync(conn, caps, snapshot, ct))
                .Add("throughput counters", () => ReadCountersAsync(conn, counters, thresholds, snapshot, ct))
                .AddIf(caps.HasPhysicalSeedingStats, "physical seeding stats", () => ReadPhysicalSeedingAsync(conn, snapshot, ct))
                .AddIf(caps.HasAutomaticSeeding, "automatic seeding", () => ReadAutomaticSeedingAsync(conn, snapshot, ct));

            await plan.RunAsync(async () =>
            {
                // Everything the Overview shows is derived from the three sections just read, so it is final here
                // rather than provisional — the later sections fill in other tabs, not these numbers.
                Summarise(snapshot);
                if (onOverviewReady != null) await onOverviewReady(snapshot).ConfigureAwait(false);
            }).ConfigureAwait(false);

            snapshot.SectionsRead = plan.Ran;
            snapshot.SectionsFailed = plan.Failed;
        }

        if (snapshot.Groups.Count == 0 && snapshot.Warnings.Count == 0)
            snapshot.UnavailableReason = "Always On is enabled but this instance hosts no availability group replicas.";

        Summarise(snapshot);
        AgDiagnostics.Evaluate(snapshot, caps, thresholds);

        snapshot.Duration = DateTime.UtcNow - started;
        return snapshot;
    }

    /// <summary>
    /// The roll-ups the Overview is drawn from. Idempotent, because it is worked out twice: once as soon as the
    /// rows it reads are in — so the group cards can be shown while the rest is still being collected — and again
    /// at the end for the path where there was no early paint to do.
    /// </summary>
    private static void Summarise(AgSnapshot snapshot)
    {
        RollUpGroupCounts(snapshot);

        snapshot.LocalRole = null;
        foreach (var replica in snapshot.Replicas)
            if (replica.IsLocal) { snapshot.LocalRole = replica.Role; break; }
    }

    /// <summary>Fills the per-group counters the overview cards show, from the rows already collected.</summary>
    private static void RollUpGroupCounts(AgSnapshot snapshot)
    {
        foreach (var group in snapshot.Groups)
        {
            int replicas = 0, unhealthy = 0, warnings = 0;
            foreach (var replica in snapshot.Replicas)
            {
                if (!string.Equals(replica.AgName, group.Name, StringComparison.OrdinalIgnoreCase)) continue;
                replicas++;
                if (replica.IsUnhealthy) unhealthy++;
                else if (replica.IsWarning) warnings++;
            }

            var databases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var db in snapshot.Databases)
            {
                if (!string.Equals(db.AgName, group.Name, StringComparison.OrdinalIgnoreCase)) continue;
                databases.Add(db.DatabaseName ?? "");
                if (db.IsUnhealthy) unhealthy++;
                else if (db.IsWarning) warnings++;
            }

            group.ReplicaCount = replicas;
            group.DatabaseCount = databases.Count;
            group.UnhealthyCount = unhealthy;
            group.WarningCount = warnings;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Availability groups
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The group query. Exposed so the tool window's "Open as query" button can hand the user the exact
    /// T-SQL the grid was built from — the point of a diagnostic tool is that you can take its query away
    /// and keep digging.
    /// </summary>
    internal static string GroupsSql(AgCapabilities caps) => $@"
SELECT
    ag.group_id,
    ag.name,
    ags.primary_replica,
    ags.synchronization_health_desc,
    ags.primary_recovery_health_desc,
    ag.automated_backup_preference_desc,
    ag.failure_condition_level,
    ag.health_check_timeout,
    {AgCapabilities.Column(caps.HasClusterTypeDesc, "ag.cluster_type_desc", "cluster_type_desc")},
    {AgCapabilities.Column(caps.HasRequiredSyncSecondaries, "ag.required_synchronized_secondaries_to_commit", "required_sync_secondaries")},
    {AgCapabilities.Column(caps.HasIsDistributed, "ag.is_distributed", "is_distributed")}
FROM sys.availability_groups AS ag
LEFT JOIN sys.dm_hadr_availability_group_states AS ags ON ags.group_id = ag.group_id
ORDER BY ag.name;";

    private static async Task ReadGroupsAsync(SqlConnection conn, AgCapabilities caps, AgSnapshot snapshot, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(GroupsSql(caps), conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                snapshot.Groups.Add(new AgGroupRow
                {
                    GroupId = reader.GetGuid(reader.GetOrdinal("group_id")),
                    Name = Str(reader, "name"),
                    PrimaryReplica = Str(reader, "primary_replica"),
                    SynchronizationHealth = Str(reader, "synchronization_health_desc"),
                    PrimaryRecoveryHealth = Str(reader, "primary_recovery_health_desc"),
                    AutomatedBackupPreference = Str(reader, "automated_backup_preference_desc"),
                    FailureConditionLevel = Int(reader, "failure_condition_level"),
                    HealthCheckTimeout = Int(reader, "health_check_timeout"),
                    ClusterType = Str(reader, "cluster_type_desc") ?? "WSFC",
                    RequiredSynchronizedSecondaries = Int(reader, "required_sync_secondaries"),
                    IsDistributed = Bool(reader, "is_distributed") == true
                });
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Replicas
    // ---------------------------------------------------------------------------------------------

    internal static string ReplicasSql(AgCapabilities caps) => $@"
SELECT
    ag.name AS ag_name,
    ar.replica_server_name,
    ars.role_desc,
    ar.availability_mode_desc,
    ar.failover_mode_desc,
    ars.operational_state_desc,
    ars.connected_state_desc,
    ars.synchronization_health_desc,
    ars.recovery_health_desc,
    ar.secondary_role_allow_connections_desc,
    ar.backup_priority,
    ar.endpoint_url,
    ar.session_timeout,
    ar.read_only_routing_url,
    {AgCapabilities.Column(caps.HasReadWriteRoutingUrl, "ar.read_write_routing_url", "read_write_routing_url")},
    ars.is_local,
    ars.last_connect_error_number,
    ars.last_connect_error_description,
    ars.last_connect_error_timestamp,
    {AgCapabilities.Column(caps.HasSeedingModeDesc, "ar.seeding_mode_desc", "seeding_mode_desc")}
FROM sys.availability_replicas AS ar
JOIN sys.availability_groups AS ag ON ag.group_id = ar.group_id
LEFT JOIN sys.dm_hadr_availability_replica_states AS ars ON ars.replica_id = ar.replica_id
ORDER BY ag.name, CASE WHEN ars.role_desc = 'PRIMARY' THEN 0 ELSE 1 END, ar.replica_server_name;";

    private static async Task ReadReplicasAsync(SqlConnection conn, AgCapabilities caps, AgSnapshot snapshot, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(ReplicasSql(caps), conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                snapshot.Replicas.Add(new AgReplicaRow
                {
                    AgName = Str(reader, "ag_name"),
                    ReplicaServerName = Str(reader, "replica_server_name"),
                    Role = Str(reader, "role_desc") ?? "RESOLVING",
                    AvailabilityMode = Str(reader, "availability_mode_desc"),
                    FailoverMode = Str(reader, "failover_mode_desc"),
                    OperationalState = Str(reader, "operational_state_desc"),
                    ConnectedState = Str(reader, "connected_state_desc"),
                    SynchronizationHealth = Str(reader, "synchronization_health_desc"),
                    RecoveryHealth = Str(reader, "recovery_health_desc"),
                    ReadableSecondary = Str(reader, "secondary_role_allow_connections_desc"),
                    BackupPriority = Int(reader, "backup_priority"),
                    EndpointUrl = Str(reader, "endpoint_url"),
                    SessionTimeoutSeconds = Int(reader, "session_timeout"),
                    ReadOnlyRoutingUrl = Str(reader, "read_only_routing_url"),
                    ReadWriteRoutingUrl = Str(reader, "read_write_routing_url"),
                    IsLocal = Bool(reader, "is_local") == true,
                    LastConnectErrorNumber = Int(reader, "last_connect_error_number"),
                    LastConnectErrorDescription = Str(reader, "last_connect_error_description"),
                    LastConnectErrorTimestamp = Date(reader, "last_connect_error_timestamp"),
                    SeedingMode = Str(reader, "seeding_mode_desc")
                });
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Database replica states — the queue and lag detail
    // ---------------------------------------------------------------------------------------------

    // LSNs are numeric(25,0); converting server-side keeps them out of decimal handling on this end, where
    // they are only ever displayed and compared as opaque strings.
    internal static string DatabasesSql(AgCapabilities caps) => $@"
SELECT
    ag.name AS ag_name,
    ar.replica_server_name,
    dbcs.database_name,
    ar.availability_mode_desc,
    drs.is_primary_replica,
    drs.is_local,
    drs.synchronization_state_desc,
    drs.synchronization_health_desc,
    drs.database_state_desc,
    drs.suspend_reason_desc,
    drs.is_suspended,
    dbcs.is_failover_ready,
    {AgCapabilities.Column(caps.HasIsDatabaseJoined, "dbcs.is_database_joined", "is_database_joined")},
    drs.log_send_queue_size,
    drs.log_send_rate,
    drs.redo_queue_size,
    drs.redo_rate,
    drs.filestream_send_rate,
    drs.last_commit_time,
    CONVERT(varchar(40), drs.end_of_log_lsn)    AS end_of_log_lsn,
    CONVERT(varchar(40), drs.last_hardened_lsn) AS last_hardened_lsn,
    CONVERT(varchar(40), drs.last_redone_lsn)   AS last_redone_lsn,
    {AgCapabilities.Column(caps.HasSecondaryLagSeconds, "drs.secondary_lag_seconds", "secondary_lag_seconds")}
FROM sys.dm_hadr_database_replica_states AS drs
JOIN sys.availability_replicas AS ar ON ar.replica_id = drs.replica_id
JOIN sys.availability_groups AS ag ON ag.group_id = drs.group_id
LEFT JOIN sys.dm_hadr_database_replica_cluster_states AS dbcs
       ON dbcs.replica_id = drs.replica_id AND dbcs.group_database_id = drs.group_database_id
ORDER BY ag.name, dbcs.database_name, CASE WHEN drs.is_primary_replica = 1 THEN 0 ELSE 1 END, ar.replica_server_name;";

    private static async Task ReadDatabasesAsync(SqlConnection conn, AgCapabilities caps, AgSnapshot snapshot, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(DatabasesSql(caps), conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                snapshot.Databases.Add(new AgDatabaseRow
                {
                    AgName = Str(reader, "ag_name"),
                    ReplicaServerName = Str(reader, "replica_server_name"),
                    DatabaseName = Str(reader, "database_name") ?? "(unknown)",
                    AvailabilityMode = Str(reader, "availability_mode_desc"),
                    IsPrimaryReplica = Bool(reader, "is_primary_replica") == true,
                    IsLocal = Bool(reader, "is_local") == true,
                    SynchronizationState = Str(reader, "synchronization_state_desc"),
                    SynchronizationHealth = Str(reader, "synchronization_health_desc"),
                    DatabaseState = Str(reader, "database_state_desc"),
                    SuspendReason = Str(reader, "suspend_reason_desc"),
                    IsSuspended = Bool(reader, "is_suspended"),
                    IsFailoverReady = Bool(reader, "is_failover_ready"),
                    IsDatabaseJoined = Bool(reader, "is_database_joined"),
                    LogSendQueueKb = Long(reader, "log_send_queue_size"),
                    LogSendRateKbSec = Long(reader, "log_send_rate"),
                    RedoQueueKb = Long(reader, "redo_queue_size"),
                    RedoRateKbSec = Long(reader, "redo_rate"),
                    FilestreamSendRateKbSec = Long(reader, "filestream_send_rate"),
                    SecondaryLagSeconds = Long(reader, "secondary_lag_seconds"),
                    LastCommitTime = Date(reader, "last_commit_time"),
                    EndOfLogLsn = Str(reader, "end_of_log_lsn"),
                    LastHardenedLsn = Str(reader, "last_hardened_lsn"),
                    LastRedoneLsn = Str(reader, "last_redone_lsn")
                });
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Cluster and quorum
    //
    // Quorum is the piece the replica-state DMVs cannot tell you about, and losing it takes every group on the
    // cluster offline at once — a healthy-looking replica grid on an instance that is one vote from a total
    // outage is the most dangerous view this window could present.
    // ---------------------------------------------------------------------------------------------

    private const string ClusterCoreSql = @"
SELECT cluster_name, quorum_type_desc, quorum_state_desc
FROM sys.dm_hadr_cluster;";

    private const string ClusterMembersSql = @"
SELECT member_name, member_type_desc, member_state_desc, number_of_quorum_votes
FROM sys.dm_hadr_cluster_members
ORDER BY member_type_desc, member_name;";

    private const string ClusterNetworksSql = @"
SELECT member_name, network_subnet_ip, network_subnet_prefix_length, network_subnet_ipv4_mask, is_public, is_ipv4
FROM sys.dm_hadr_cluster_networks
ORDER BY member_name, network_subnet_ip;";

    /// <summary>
    /// One command, up to three result sets — the members and networks views are only appended when the probe
    /// found them, and <see cref="ReadClusterAsync"/> advances the reader on the same conditions.
    /// </summary>
    internal static string ClusterSql(AgCapabilities caps)
    {
        string sql = ClusterCoreSql;
        if (caps.HasClusterMembers) sql += Environment.NewLine + ClusterMembersSql;
        if (caps.HasClusterNetworks) sql += Environment.NewLine + ClusterNetworksSql;
        return sql;
    }

    private static async Task ReadClusterAsync(SqlConnection conn, AgCapabilities caps, AgSnapshot snapshot, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(ClusterSql(caps), conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            // No row at all is normal for CLUSTER_TYPE = NONE (a read-scale group), so this stays null rather
            // than becoming a warning.
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                snapshot.Cluster = new AgClusterInfo
                {
                    ClusterName = Str(reader, "cluster_name"),
                    QuorumType = Str(reader, "quorum_type_desc"),
                    QuorumState = Str(reader, "quorum_state_desc")
                };
            }

            if (caps.HasClusterMembers)
            {
                await reader.NextResultAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    snapshot.ClusterMembers.Add(new AgClusterMemberRow
                    {
                        MemberName = Str(reader, "member_name"),
                        MemberType = Str(reader, "member_type_desc"),
                        MemberState = Str(reader, "member_state_desc"),
                        QuorumVotes = Int(reader, "number_of_quorum_votes")
                    });
                }
            }

            if (caps.HasClusterNetworks)
            {
                await reader.NextResultAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    snapshot.ClusterNetworks.Add(new AgClusterNetworkRow
                    {
                        MemberName = Str(reader, "member_name"),
                        NetworkSubnetIp = Str(reader, "network_subnet_ip"),
                        PrefixLength = Int(reader, "network_subnet_prefix_length"),
                        NetworkSubnetMask = Str(reader, "network_subnet_ipv4_mask"),
                        IsPublic = Bool(reader, "is_public"),
                        IsIpv4 = Bool(reader, "is_ipv4")
                    });
                }
            }
        }
    }

    // sys.dm_hadr_availability_replica_cluster_nodes joins on the group *name*, not an id — it is a
    // cluster-level view and predates the group existing in sys.availability_groups on every node.
    internal static string ClusterNodesSql(AgCapabilities caps) => $@"
SELECT
    ISNULL(ag.name, arcn.group_name) AS ag_name,
    arcn.replica_server_name,
    arcn.node_name,
    {AgCapabilities.Column(caps.HasReplicaClusterStates, "arcs.join_state_desc", "join_state_desc")}
FROM sys.dm_hadr_availability_replica_cluster_nodes AS arcn
LEFT JOIN sys.availability_groups AS ag ON ag.name = arcn.group_name{(caps.HasReplicaClusterStates ? @"
LEFT JOIN sys.dm_hadr_availability_replica_cluster_states AS arcs
       ON arcs.group_id = ag.group_id AND arcs.replica_server_name = arcn.replica_server_name" : "")}
ORDER BY ag_name, arcn.replica_server_name, arcn.node_name;";

    private static async Task ReadClusterNodesAsync(SqlConnection conn, AgCapabilities caps, AgSnapshot snapshot, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(ClusterNodesSql(caps), conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                snapshot.ClusterNodes.Add(new AgClusterNodeRow
                {
                    AgName = Str(reader, "ag_name"),
                    ReplicaServerName = Str(reader, "replica_server_name"),
                    NodeName = Str(reader, "node_name"),
                    JoinState = Str(reader, "join_state_desc")
                });
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Listeners and read-only routing
    // ---------------------------------------------------------------------------------------------

    // One row per listener IP rather than per listener: a multi-subnet listener's individual IPs go offline
    // independently, and the offline one is the whole finding.
    internal static string ListenersSql(AgCapabilities caps) => $@"
SELECT
    ag.name AS ag_name,
    agl.dns_name,
    agl.port,
    agl.is_conformant,
    agl.ip_configuration_string_from_cluster,
    {(caps.HasListenerIpAddresses ? @"lip.ip_address,
    lip.ip_subnet_mask,
    lip.is_dhcp,
    lip.network_subnet_ip,
    lip.state" : @"NULL AS ip_address,
    NULL AS ip_subnet_mask,
    NULL AS is_dhcp,
    NULL AS network_subnet_ip,
    NULL AS state")}
FROM sys.availability_group_listeners AS agl
JOIN sys.availability_groups AS ag ON ag.group_id = agl.group_id{(caps.HasListenerIpAddresses ? @"
LEFT JOIN sys.availability_group_listener_ip_addresses AS lip ON lip.listener_id = agl.listener_id" : "")}
ORDER BY ag.name, agl.dns_name{(caps.HasListenerIpAddresses ? ", lip.ip_address" : "")};";

    private static async Task ReadListenersAsync(SqlConnection conn, AgCapabilities caps, AgSnapshot snapshot, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(ListenersSql(caps), conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                snapshot.Listeners.Add(new AgListenerRow
                {
                    AgName = Str(reader, "ag_name"),
                    DnsName = Str(reader, "dns_name"),
                    Port = Int(reader, "port"),
                    IsConformant = Bool(reader, "is_conformant"),
                    IpConfigurationFromCluster = Str(reader, "ip_configuration_string_from_cluster"),
                    IpAddress = Str(reader, "ip_address"),
                    IpSubnetMask = Str(reader, "ip_subnet_mask"),
                    IsDhcp = Bool(reader, "is_dhcp"),
                    NetworkSubnetIp = Str(reader, "network_subnet_ip"),
                    State = Int(reader, "state")
                });
            }
        }
    }

    // The self-join is the shape of the DMV: each row pairs a source replica with one of its routing targets,
    // in priority order. The target's own routing URL comes along because a target without one routes nowhere.
    internal static string RoutingSql(AgCapabilities caps) => $@"
SELECT
    ag.name AS ag_name,
    src.replica_server_name AS source_replica,
    rl.routing_priority,
    tgt.replica_server_name AS target_replica,
    tgt.secondary_role_allow_connections_desc AS target_readable,
    tgtstate.role_desc AS target_role,
    tgt.read_only_routing_url,
    {AgCapabilities.Column(caps.HasReadWriteRoutingUrl, "tgt.read_write_routing_url", "read_write_routing_url")}
FROM sys.availability_read_only_routing_lists AS rl
JOIN sys.availability_replicas AS src ON src.replica_id = rl.replica_id
JOIN sys.availability_replicas AS tgt ON tgt.replica_id = rl.read_only_replica_id
JOIN sys.availability_groups AS ag ON ag.group_id = src.group_id
LEFT JOIN sys.dm_hadr_availability_replica_states AS tgtstate ON tgtstate.replica_id = tgt.replica_id
ORDER BY ag.name, src.replica_server_name, rl.routing_priority, tgt.replica_server_name;";

    private static async Task ReadRoutingAsync(SqlConnection conn, AgCapabilities caps, AgSnapshot snapshot, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(RoutingSql(caps), conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                snapshot.Routing.Add(new AgRoutingRow
                {
                    AgName = Str(reader, "ag_name"),
                    SourceReplica = Str(reader, "source_replica"),
                    RoutingPriority = Int(reader, "routing_priority") ?? 0,
                    TargetReplica = Str(reader, "target_replica"),
                    TargetReadableSecondary = Str(reader, "target_readable"),
                    TargetRole = Str(reader, "target_role"),
                    ReadOnlyRoutingUrl = Str(reader, "read_only_routing_url"),
                    ReadWriteRoutingUrl = Str(reader, "read_write_routing_url")
                });
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Throughput and commit latency, from the AG performance counters
    // ---------------------------------------------------------------------------------------------

    // LIKE rather than '=' on object_name: a named instance reports 'MSSQL$INSTANCE:Database Replica'. The
    // ms_ticks read comes first so the interval belongs to the same round trip as the counters it scales.
    internal const string CountersSql = @"
SELECT ms_ticks FROM sys.dm_os_sys_info;

SELECT
    RTRIM(object_name)   AS object_name,
    RTRIM(instance_name) AS instance_name,
    RTRIM(counter_name)  AS counter_name,
    cntr_value,
    cntr_type
FROM sys.dm_os_performance_counters
WHERE object_name LIKE N'%Database Replica%'
   OR object_name LIKE N'%Availability Replica%';";

    private const string DatabaseReplicaObject = "Database Replica";
    private const string AvailabilityReplicaObject = "Availability Replica";

    /// <summary>
    /// Reads the AG counter objects and turns the cumulative ones into rates.
    ///
    /// On the first poll of a server there is no baseline, so the read happens twice
    /// <see cref="BaselineSampleMs"/> apart — otherwise every rate column would be a dash until the second poll
    /// and the tab would look broken on arrival. That second read is paid once per server, not per poll.
    /// </summary>
    private static async Task ReadCountersAsync(SqlConnection conn, AgCounterTracker tracker, AgThresholds thresholds, AgSnapshot snapshot, CancellationToken ct)
    {
        if (tracker.NeedsBaseline)
        {
            await SampleCountersAsync(conn, tracker, snapshot, ct).ConfigureAwait(false);
            await Task.Delay(BaselineSampleMs, ct).ConfigureAwait(false);
        }

        var reading = await SampleCountersAsync(conn, tracker, snapshot, ct).ConfigureAwait(false);
        BuildCounterRows(reading, snapshot, thresholds);
    }

    private sealed class CounterSample
    {
        public string ObjectName;
        public string InstanceName;
        public string CounterName;
        public long Value;
        public int Type;
    }

    /// <summary>
    /// One set of counter readings plus the rate computed for each cumulative one. The rates travel with the
    /// samples rather than in a field because this method awaits — a static or thread-static cache would be
    /// resumed on whatever thread the continuation lands on, which is not necessarily the one that filled it.
    /// </summary>
    private sealed class CounterReading
    {
        public readonly List<CounterSample> Samples = new List<CounterSample>();
        public readonly Dictionary<string, double?> Rates = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);

        public double? RateOf(CounterSample sample) =>
            Rates.TryGetValue(AgCounterTracker.KeyFor(sample.ObjectName, sample.InstanceName, sample.CounterName), out var rate) ? rate : null;
    }

    /// <summary>Reads one set of counters, stores it as the new baseline, and returns it with rates attached.</summary>
    private static async Task<CounterReading> SampleCountersAsync(SqlConnection conn, AgCounterTracker tracker, AgSnapshot snapshot, CancellationToken ct)
    {
        var reading = new CounterReading();
        var samples = reading.Samples;
        var baseline = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long msTicks = 0;

        using (var cmd = new SqlCommand(CountersSql, conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
                msTicks = Long(reader, "ms_ticks") ?? 0;

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var sample = new CounterSample
                {
                    ObjectName = AgCounterTracker.Trim(Str(reader, "object_name")),
                    InstanceName = AgCounterTracker.Trim(Str(reader, "instance_name")),
                    CounterName = AgCounterTracker.Trim(Str(reader, "counter_name")),
                    Value = Long(reader, "cntr_value") ?? 0,
                    Type = Int(reader, "cntr_type") ?? 0
                };

                samples.Add(sample);
                baseline[AgCounterTracker.KeyFor(sample.ObjectName, sample.InstanceName, sample.CounterName)] = sample.Value;
            }
        }

        snapshot.CounterIntervalSeconds = tracker.IntervalSecondsFrom(msTicks);

        // Rates are resolved against the *previous* baseline, so they are read before the new one is stored.
        foreach (var sample in samples)
        {
            if (!AgCounterTracker.IsCumulative(sample.Type)) continue;
            string key = AgCounterTracker.KeyFor(sample.ObjectName, sample.InstanceName, sample.CounterName);
            reading.Rates[key] = tracker.RateFor(key, sample.Value, snapshot.CounterIntervalSeconds);
        }

        tracker.Store(baseline, msTicks);
        return reading;
    }

    /// <summary>
    /// Projects the flat counter list onto the two grids. Counter names are matched case-insensitively and
    /// anything unrecognised is ignored, so a release that adds counters costs nothing here.
    /// </summary>
    private static void BuildCounterRows(CounterReading reading, AgSnapshot snapshot, AgThresholds thresholds)
    {
        var throughput = new Dictionary<string, AgThroughputRow>(StringComparer.OrdinalIgnoreCase);
        var transport = new Dictionary<string, AgTransportRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var sample in reading.Samples)
        {
            // The counter objects carry a "_Total" instance on some releases; it double-counts the real rows.
            if (string.IsNullOrEmpty(sample.InstanceName) || string.Equals(sample.InstanceName, "_Total", StringComparison.OrdinalIgnoreCase))
                continue;

            if (sample.ObjectName.EndsWith(DatabaseReplicaObject, StringComparison.OrdinalIgnoreCase))
            {
                if (!throughput.TryGetValue(sample.InstanceName, out var row))
                    throughput[sample.InstanceName] = row = new AgThroughputRow { DatabaseName = sample.InstanceName, CommitDelayWarningMs = thresholds.CommitDelayWarningMs };

                ApplyDatabaseCounter(row, sample, reading);
            }
            else if (sample.ObjectName.EndsWith(AvailabilityReplicaObject, StringComparison.OrdinalIgnoreCase))
            {
                if (!transport.TryGetValue(sample.InstanceName, out var row))
                    transport[sample.InstanceName] = row = new AgTransportRow { Instance = sample.InstanceName };

                ApplyReplicaCounter(row, sample, reading);
            }
        }

        foreach (var row in throughput.Values) snapshot.Throughput.Add(row);
        foreach (var row in transport.Values) snapshot.Transport.Add(row);

        snapshot.Throughput.Sort((a, b) => string.Compare(a.DatabaseName, b.DatabaseName, StringComparison.OrdinalIgnoreCase));
        snapshot.Transport.Sort((a, b) => string.Compare(a.Instance, b.Instance, StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyDatabaseCounter(AgThroughputRow row, CounterSample sample, CounterReading reading)
    {
        string name = sample.CounterName;

        // Gauges: already a level in KB, so they are used as read.
        if (AgCounterTracker.Is(name, "Log Send Queue")) row.LogSendQueueKb = sample.Value;
        else if (AgCounterTracker.Is(name, "Recovery Queue")) row.RecoveryQueueKb = sample.Value;
        else if (AgCounterTracker.Is(name, "Redo Bytes Remaining")) row.RedoBytesRemainingKb = sample.Value;
        else if (AgCounterTracker.Is(name, "Log remaining for undo")) row.LogRemainingForUndoKb = sample.Value;
        else if (AgCounterTracker.Is(name, "Total Log requiring undo")) row.TotalLogRequiringUndoKb = sample.Value;

        // Rates.
        else if (AgCounterTracker.Is(name, "Transaction Delay")) row.TransactionDelayMsPerSec = CommitWaitMsPerSecond(sample.Value, sample.Type, reading.RateOf(sample));
        else if (AgCounterTracker.Is(name, "Mirrored Write Transactions/sec")) row.MirroredWriteTransactionsPerSec = reading.RateOf(sample);
        else if (AgCounterTracker.Is(name, "Log Bytes Received/sec")) row.LogBytesReceivedPerSec = reading.RateOf(sample);
        else if (AgCounterTracker.Is(name, "Redone Bytes/sec")) row.RedoneBytesPerSec = reading.RateOf(sample);
        else if (AgCounterTracker.Is(name, "File Bytes Received/sec")) row.FileBytesReceivedPerSec = reading.RateOf(sample);
    }

    /// <summary>
    /// Transaction Delay's value in milliseconds of commit wait per second.
    ///
    /// The counter's name reads like a level and it is not one: sys.dm_os_performance_counters reports it as a
    /// running total of milliseconds waited since the counters started, so it has to be differenced like every
    /// other cumulative counter here. Used raw it divides a whole-uptime total by a per-second commit rate, which
    /// reads as tens of seconds per commit on a perfectly healthy AG and grows for as long as the instance stays
    /// up — the shape of the bug this method exists to prevent recurring.
    ///
    /// <c>cntr_type</c> decides, not the counter name: a release that changed it to a genuine level would then
    /// still be handled, and no guess is being made here about which it is. Internal so the decision is testable
    /// without a server.
    /// </summary>
    internal static double? CommitWaitMsPerSecond(long counterValue, int counterType, double? rate) =>
        AgCounterTracker.IsCumulative(counterType) ? rate : counterValue;

    private static void ApplyReplicaCounter(AgTransportRow row, CounterSample sample, CounterReading reading)
    {
        string name = sample.CounterName;

        if (AgCounterTracker.Is(name, "Bytes Sent to Replica/sec")) row.BytesSentToReplicaPerSec = reading.RateOf(sample);
        else if (AgCounterTracker.Is(name, "Bytes Sent to Transport/sec")) row.BytesSentToTransportPerSec = reading.RateOf(sample);
        else if (AgCounterTracker.Is(name, "Bytes Received from Replica/sec")) row.BytesReceivedFromReplicaPerSec = reading.RateOf(sample);
        else if (AgCounterTracker.Is(name, "Sends to Replica/sec")) row.SendsToReplicaPerSec = reading.RateOf(sample);
        else if (AgCounterTracker.Is(name, "Receives from Replica/sec")) row.ReceivesFromReplicaPerSec = reading.RateOf(sample);
        else if (AgCounterTracker.Is(name, "Resent Messages/sec")) row.ResentMessagesPerSec = reading.RateOf(sample);
        else if (AgCounterTracker.Is(name, "Flow Control/sec")) row.FlowControlPerSec = reading.RateOf(sample);
        else if (AgCounterTracker.Is(name, "Flow Control Time (ms/sec)")) row.FlowControlTimeMsPerSec = reading.RateOf(sample);
    }

    // ---------------------------------------------------------------------------------------------
    // Seeding
    // ---------------------------------------------------------------------------------------------

    internal const string PhysicalSeedingSql = @"
SELECT
    ps.local_database_name,
    ps.remote_machine_name,
    ps.role_desc,
    ps.internal_state_desc,
    ps.transfer_rate_bytes_per_second,
    ps.transferred_size_bytes,
    ps.database_size_bytes,
    ps.start_time_utc,
    ps.end_time_utc,
    ps.estimate_time_complete_utc,
    ps.total_disk_io_wait_time_ms,
    ps.total_network_wait_time_ms,
    ps.is_compression_enabled,
    ps.failure_message
FROM sys.dm_hadr_physical_seeding_stats AS ps
ORDER BY ps.start_time_utc DESC;";

    private static async Task ReadPhysicalSeedingAsync(SqlConnection conn, AgSnapshot snapshot, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(PhysicalSeedingSql, conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                snapshot.Seeding.Add(new AgSeedingRow
                {
                    LocalDatabaseName = Str(reader, "local_database_name"),
                    RemoteMachineName = Str(reader, "remote_machine_name"),
                    Role = Str(reader, "role_desc"),
                    InternalState = Str(reader, "internal_state_desc"),
                    TransferRateBytesPerSecond = Long(reader, "transfer_rate_bytes_per_second") ?? 0,
                    TransferredBytes = Long(reader, "transferred_size_bytes") ?? 0,
                    DatabaseSizeBytes = Long(reader, "database_size_bytes") ?? 0,
                    StartTimeUtc = Date(reader, "start_time_utc"),
                    EndTimeUtc = Date(reader, "end_time_utc"),
                    EstimateCompleteUtc = Date(reader, "estimate_time_complete_utc"),
                    TotalDiskIoWaitMs = Long(reader, "total_disk_io_wait_time_ms") ?? 0,
                    TotalNetworkWaitMs = Long(reader, "total_network_wait_time_ms") ?? 0,
                    IsCompressionEnabled = Bool(reader, "is_compression_enabled") == true,
                    FailureMessage = Str(reader, "failure_message")
                });
            }
        }
    }

    // dm_hadr_database_replica_cluster_states is keyed (replica_id, group_database_id), so a plain join would
    // fan a seeding row out once per replica. TOP 1 via OUTER APPLY just resolves the name.
    internal const string AutoSeedingSql = @"
SELECT
    ag.name AS ag_name,
    d.database_name,
    aseed.start_time,
    aseed.completion_time,
    aseed.current_state,
    aseed.performed_seeding,
    aseed.is_source,
    aseed.failure_state_desc,
    aseed.error_code,
    aseed.number_of_attempts
FROM sys.dm_hadr_automatic_seeding AS aseed
LEFT JOIN sys.availability_groups AS ag ON ag.group_id = aseed.ag_id
OUTER APPLY (
    SELECT TOP (1) dbcs.database_name
    FROM sys.dm_hadr_database_replica_cluster_states AS dbcs
    WHERE dbcs.group_database_id = aseed.ag_db_id
) AS d
ORDER BY aseed.start_time DESC;";

    private static async Task ReadAutomaticSeedingAsync(SqlConnection conn, AgSnapshot snapshot, CancellationToken ct)
    {
        using (var cmd = new SqlCommand(AutoSeedingSql, conn) { CommandTimeout = CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                snapshot.AutoSeeding.Add(new AgAutoSeedRow
                {
                    AgName = Str(reader, "ag_name"),
                    DatabaseName = Str(reader, "database_name") ?? "(unknown)",
                    StartTime = Date(reader, "start_time"),
                    CompletionTime = Date(reader, "completion_time"),
                    CurrentState = Str(reader, "current_state"),
                    PerformedSeeding = Bool(reader, "performed_seeding") == true,
                    IsSource = Bool(reader, "is_source") == true,
                    FailureState = Str(reader, "failure_state_desc"),
                    ErrorCode = Int(reader, "error_code"),
                    NumberOfAttempts = Int(reader, "number_of_attempts") ?? 0
                });
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // AlwaysOn_health extended events
    // ---------------------------------------------------------------------------------------------

    private const string HealthEventsBodySql = @"
DECLARE @file nvarchar(4000);

SELECT @file = CAST(t.target_data AS xml).value('(EventFileTarget/File/@name)[1]', 'nvarchar(4000)')
FROM sys.dm_xe_sessions AS s
JOIN sys.dm_xe_session_targets AS t ON t.event_session_address = s.address
WHERE s.name = N'AlwaysOn_health' AND t.target_name = N'event_file';

IF @file IS NULL
BEGIN
    RAISERROR(N'The AlwaysOn_health extended-event session is not running on this instance.', 16, 1);
END
ELSE
BEGIN
    SELECT TOP (@top)
        e.x.value('(event/@timestamp)[1]', 'datetime2(3)')                                        AS event_time_utc,
        e.x.value('(event/@name)[1]', 'nvarchar(128)')                                            AS event_name,
        e.x.value('(event/data[@name=""error_number""]/value)[1]', 'int')                          AS error_number,
        e.x.value('(event/data[@name=""severity""]/value)[1]', 'int')                             AS severity,
        e.x.value('(event/data[@name=""state""]/value)[1]', 'int')                                AS error_state,
        COALESCE(
            e.x.value('(event/data[@name=""message""]/value)[1]', 'nvarchar(4000)'),
            e.x.value('(event/data[@name=""current_state""]/text)[1]', 'nvarchar(4000)'),
            e.x.value('(event/data[@name=""state""]/text)[1]', 'nvarchar(4000)'),
            e.x.value('(event/data[@name=""sync_state""]/text)[1]', 'nvarchar(4000)'))            AS message,
        COALESCE(
            e.x.value('(event/data[@name=""availability_group_name""]/value)[1]', 'nvarchar(256)'),
            e.x.value('(event/action[@name=""availability_group""]/value)[1]', 'nvarchar(256)'))   AS ag_name,
        e.x.value('(event/data[@name=""database_name""]/value)[1]', 'nvarchar(256)')               AS database_name,
        COALESCE(
            e.x.value('(event/data[@name=""availability_replica_name""]/value)[1]', 'nvarchar(256)'),
            e.x.value('(event/data[@name=""target_availability_replica_name""]/value)[1]', 'nvarchar(256)')) AS replica_name
    FROM sys.fn_xe_file_target_read_file(@file, NULL, NULL, NULL) AS f
    CROSS APPLY (SELECT CAST(f.event_data AS xml) AS x) AS e
    ORDER BY event_time_utc DESC;
END";

    /// <summary>The events query with @top declared inline, so "Open as query" produces a runnable batch.</summary>
    internal static string HealthEventsSql(int topRows) => $"DECLARE @top int = {topRows};{HealthEventsBodySql}";

    /// <summary>
    /// Reads recent events from the AlwaysOn_health session's event file. This is the error history SSMS's own
    /// dashboard makes you dig for — replica state changes, lease expiry, redo blocking, and raw error_reported
    /// entries.
    ///
    /// Only the session's *current* rollover file is read. Parsing the whole 5-file default set means casting
    /// ~25 MB to XML server-side, which is far too expensive to sit on a refresh timer — hence the Errors tab
    /// loads on demand rather than on the poll.
    /// </summary>
    public static async Task<List<AgEventRow>> ReadHealthEventsAsync(string connectionString, int topRows, CancellationToken ct)
    {
        var rows = new List<AgEventRow>();

        using (var conn = SqlConnectionFactory.Create(connectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            using (var cmd = new SqlCommand(HealthEventsBodySql, conn) { CommandTimeout = 60 })
            {
                cmd.Parameters.Add("@top", SqlDbType.Int).Value = topRows;
                using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        rows.Add(new AgEventRow
                        {
                            EventTimeUtc = DateTime.SpecifyKind(Date(reader, "event_time_utc") ?? DateTime.UtcNow, DateTimeKind.Utc),
                            EventName = Str(reader, "event_name"),
                            ErrorNumber = Int(reader, "error_number"),
                            Severity = Int(reader, "severity"),
                            ErrorState = Int(reader, "error_state"),
                            Message = Str(reader, "message"),
                            AgName = Str(reader, "ag_name"),
                            DatabaseName = Str(reader, "database_name"),
                            ReplicaServerName = Str(reader, "replica_name")
                        });
                    }
                }
            }
        }

        return rows;
    }

    // ---------------------------------------------------------------------------------------------
    // Reader helpers — every column is addressed by name so capability-substituted NULLs still resolve.
    // ---------------------------------------------------------------------------------------------

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
