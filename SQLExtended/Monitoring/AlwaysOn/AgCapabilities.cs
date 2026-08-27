using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SQLExtended.Monitoring.AlwaysOn;

/// <summary>
/// What the target instance actually exposes. The HADR DMVs gained columns across releases
/// (secondary_lag_seconds in 2016 SP2/2017, cluster_type_desc in 2017, the seeding DMVs in 2016,
/// read_write_routing_url in 2019, is_contained in 2022), so rather than branching on version numbers —
/// which SP and CU levels make unreliable — we ask the catalog whether each specific column or object
/// exists and build the queries from that.
///
/// Probed once per connection string and cached for the life of the tool window; a server does not
/// gain or lose columns without a restart, which drops the connection anyway.
///
/// <see cref="IsHealthSessionRunning"/> is the one exception to "capabilities do not change": an operator can
/// stop the AlwaysOn_health session at any time. It is re-read on every probe rather than every poll, which is
/// enough for the Diagnostics tab to say the Errors tab has nothing to read.
/// </summary>
/// <remarks>
/// The setters are <c>internal</c> rather than <c>private</c> so the diagnostic rules can be unit tested against
/// a hand-built capability set. Nothing outside <see cref="ProbeAsync"/> writes them in the product.
/// </remarks>
internal sealed class AgCapabilities
{
    public bool IsHadrEnabled { get; internal set; }
    public bool HasSecondaryLagSeconds { get; internal set; }
    public bool HasClusterTypeDesc { get; internal set; }
    public bool HasRequiredSyncSecondaries { get; internal set; }
    public bool HasIsDistributed { get; internal set; }
    public bool HasSeedingModeDesc { get; internal set; }
    public bool HasPhysicalSeedingStats { get; internal set; }
    public bool HasAutomaticSeeding { get; internal set; }
    public string ServerName { get; internal set; }

    /// <summary>The login this connection authenticates as, as the server resolves it — <c>SUSER_SNAME()</c>.</summary>
    public string LoginName { get; internal set; }

    // --- cluster / quorum ---
    public bool HasClusterView { get; internal set; }
    public bool HasClusterMembers { get; internal set; }
    public bool HasClusterNetworks { get; internal set; }
    public bool HasReplicaClusterNodes { get; internal set; }
    public bool HasReplicaClusterStates { get; internal set; }

    // --- listeners and routing ---
    public bool HasListeners { get; internal set; }
    public bool HasListenerIpAddresses { get; internal set; }
    public bool HasReadOnlyRoutingLists { get; internal set; }
    public bool HasReadWriteRoutingUrl { get; internal set; }

    // --- assorted optional columns ---
    public bool HasIsDatabaseJoined { get; internal set; }
    public bool HasIsContained { get; internal set; }
    public bool IsHealthSessionRunning { get; internal set; }

    /// <summary>Version and edition, shown on the Overview so the tab set makes sense in context.</summary>
    public string ProductVersion { get; internal set; }
    public string Edition { get; internal set; }

    // sys.all_columns rather than COL_LENGTH: all_columns covers system objects unambiguously, and getting a
    // false negative here would silently blank a column for every server rather than just old ones.
    private const string ProbeSql = @"
DECLARE @drs  int = OBJECT_ID('sys.dm_hadr_database_replica_states');
DECLARE @ag   int = OBJECT_ID('sys.availability_groups');
DECLARE @ar   int = OBJECT_ID('sys.availability_replicas');
DECLARE @dbcs int = OBJECT_ID('sys.dm_hadr_database_replica_cluster_states');

SELECT
    SERVERPROPERTY('ServerName')                     AS server_name,
    SUSER_SNAME()                                    AS login_name,
    CAST(SERVERPROPERTY('IsHadrEnabled') AS int)     AS is_hadr_enabled,
    CONVERT(nvarchar(64), SERVERPROPERTY('ProductVersion')) AS product_version,
    CONVERT(nvarchar(128), SERVERPROPERTY('Edition'))       AS edition,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @drs  AND name = 'secondary_lag_seconds')                       THEN 1 ELSE 0 END AS has_secondary_lag,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @ag   AND name = 'cluster_type_desc')                           THEN 1 ELSE 0 END AS has_cluster_type,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @ag   AND name = 'required_synchronized_secondaries_to_commit') THEN 1 ELSE 0 END AS has_required_sync,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @ag   AND name = 'is_distributed')                              THEN 1 ELSE 0 END AS has_is_distributed,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @ag   AND name = 'is_contained')                                THEN 1 ELSE 0 END AS has_is_contained,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @ar   AND name = 'seeding_mode_desc')                           THEN 1 ELSE 0 END AS has_seeding_mode,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @ar   AND name = 'read_write_routing_url')                      THEN 1 ELSE 0 END AS has_rw_routing_url,
    CASE WHEN EXISTS (SELECT 1 FROM sys.all_columns WHERE object_id = @dbcs AND name = 'is_database_joined')                          THEN 1 ELSE 0 END AS has_is_database_joined,
    CASE WHEN OBJECT_ID('sys.dm_hadr_physical_seeding_stats')                IS NOT NULL THEN 1 ELSE 0 END AS has_physical_seeding,
    CASE WHEN OBJECT_ID('sys.dm_hadr_automatic_seeding')                     IS NOT NULL THEN 1 ELSE 0 END AS has_auto_seeding,
    CASE WHEN OBJECT_ID('sys.dm_hadr_cluster')                               IS NOT NULL THEN 1 ELSE 0 END AS has_cluster,
    CASE WHEN OBJECT_ID('sys.dm_hadr_cluster_members')                       IS NOT NULL THEN 1 ELSE 0 END AS has_cluster_members,
    CASE WHEN OBJECT_ID('sys.dm_hadr_cluster_networks')                      IS NOT NULL THEN 1 ELSE 0 END AS has_cluster_networks,
    CASE WHEN OBJECT_ID('sys.dm_hadr_availability_replica_cluster_nodes')    IS NOT NULL THEN 1 ELSE 0 END AS has_replica_cluster_nodes,
    CASE WHEN OBJECT_ID('sys.dm_hadr_availability_replica_cluster_states')   IS NOT NULL THEN 1 ELSE 0 END AS has_replica_cluster_states,
    CASE WHEN OBJECT_ID('sys.availability_group_listeners')                  IS NOT NULL THEN 1 ELSE 0 END AS has_listeners,
    CASE WHEN OBJECT_ID('sys.availability_group_listener_ip_addresses')      IS NOT NULL THEN 1 ELSE 0 END AS has_listener_ips,
    CASE WHEN OBJECT_ID('sys.availability_read_only_routing_lists')          IS NOT NULL THEN 1 ELSE 0 END AS has_ro_routing,
    CASE WHEN EXISTS (SELECT 1 FROM sys.dm_xe_sessions WHERE name = N'AlwaysOn_health') THEN 1 ELSE 0 END   AS health_session_running;";

    public static async Task<AgCapabilities> ProbeAsync(string connectionString, CancellationToken ct)
    {
        var caps = new AgCapabilities();

        using (var conn = new SqlConnection(connectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            using (var cmd = new SqlCommand(ProbeSql, conn) { CommandTimeout = AgQueryService.CommandTimeoutSeconds })
            using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    return caps;

                caps.ServerName = Str(reader, "server_name");
                caps.LoginName = Str(reader, "login_name");
                caps.IsHadrEnabled = Flag(reader, "is_hadr_enabled");
                caps.ProductVersion = Str(reader, "product_version");
                caps.Edition = Str(reader, "edition");

                caps.HasSecondaryLagSeconds = Flag(reader, "has_secondary_lag");
                caps.HasClusterTypeDesc = Flag(reader, "has_cluster_type");
                caps.HasRequiredSyncSecondaries = Flag(reader, "has_required_sync");
                caps.HasIsDistributed = Flag(reader, "has_is_distributed");
                caps.HasIsContained = Flag(reader, "has_is_contained");
                caps.HasSeedingModeDesc = Flag(reader, "has_seeding_mode");
                caps.HasReadWriteRoutingUrl = Flag(reader, "has_rw_routing_url");
                caps.HasIsDatabaseJoined = Flag(reader, "has_is_database_joined");
                caps.HasPhysicalSeedingStats = Flag(reader, "has_physical_seeding");
                caps.HasAutomaticSeeding = Flag(reader, "has_auto_seeding");
                caps.HasClusterView = Flag(reader, "has_cluster");
                caps.HasClusterMembers = Flag(reader, "has_cluster_members");
                caps.HasClusterNetworks = Flag(reader, "has_cluster_networks");
                caps.HasReplicaClusterNodes = Flag(reader, "has_replica_cluster_nodes");
                caps.HasReplicaClusterStates = Flag(reader, "has_replica_cluster_states");
                caps.HasListeners = Flag(reader, "has_listeners");
                caps.HasListenerIpAddresses = Flag(reader, "has_listener_ips");
                caps.HasReadOnlyRoutingLists = Flag(reader, "has_ro_routing");
                caps.IsHealthSessionRunning = Flag(reader, "health_session_running");
            }
        }

        return caps;
    }

    private static string Str(SqlDataReader reader, string name)
    {
        int i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? null : System.Convert.ToString(reader.GetValue(i));
    }

    private static bool Flag(SqlDataReader reader, string name)
    {
        int i = reader.GetOrdinal(name);
        return !reader.IsDBNull(i) && System.Convert.ToInt32(reader.GetValue(i)) == 1;
    }

    /// <summary>
    /// Emits <c>expr AS alias</c> when the column exists and <c>NULL AS alias</c> when it does not, so the
    /// reader can always address every column by name regardless of the target version.
    /// </summary>
    public static string Column(bool present, string expression, string alias) => present ? $"{expression} AS {alias}" : $"NULL AS {alias}";
}
