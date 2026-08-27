using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace SQLExtended.Monitoring.Performance;

/// <summary>
/// Reads the Server info tab: what the instance says about itself, the host it runs on, the instance-level
/// configuration, and where its build sits in the servicing and support timeline.
///
/// <para><b>Collected once per pinned server and on an explicit Refresh, never on the poll timer</b> — see
/// <see cref="PerfServerInfo"/>. That is what makes the capability probe below affordable.</para>
///
/// <para><b>Optional DMVs and columns are probed, not branched on by version.</b> A batch binds as a whole, so
/// one statement naming <c>sys.dm_os_host_info</c> against SQL Server 2016 does not lose that one row — it
/// fails the entire command and empties the tab. This is the same trap the replication monitor hit with
/// <c>MSmerge_history</c>, and the same fix: probe <c>sys.all_objects</c>/<c>sys.all_columns</c>, then
/// substitute <c>NULL AS &lt;alias&gt;</c> for anything absent so the reader can always address every column by
/// name. <c>SERVERPROPERTY</c> needs none of this — it returns NULL for a property the release does not know,
/// which is why the identity block can name new properties freely.</para>
/// </summary>
internal static class PerfServerInfoQuery
{
    /// <summary>Groups, in the order they are shown. The grid is not sorted, so insertion order is the layout.</summary>
    private const string GroupInstance = "Instance";
    private const string GroupVersion = "Version";
    private const string GroupHost = "Host";
    private const string GroupService = "Service";
    private const string GroupStorage = "Storage";
    private const string GroupConfiguration = "Configuration";

    /// <summary>
    /// Instance-level settings worth showing. Anything absent from <c>sys.configurations</c> on a given release
    /// simply returns no row, so new and retired settings can both be listed without a version check.
    /// </summary>
    private static readonly string[] ConfigurationNames =
    {
        "max server memory (MB)", "min server memory (MB)", "max degree of parallelism",
        "cost threshold for parallelism", "max worker threads", "optimize for ad hoc workloads",
        "priority boost", "lightweight pooling", "remote admin connections", "backup compression default",
        "fill factor (%)", "clr enabled", "xp_cmdshell", "Ad Hoc Distributed Queries",
        "network packet size (B)", "remote login timeout (s)", "remote query timeout (s)",
        "default trace enabled", "common criteria compliance enabled", "tempdb metadata memory-optimized",
        "automatic soft-NUMA disabled", "ADR cleaner retry timeout (min)"
    };

    // ---------------------------------------------------------------------------------------------------
    // Capability probe
    // ---------------------------------------------------------------------------------------------------

    /// <summary>Which optional objects and columns this instance actually has.</summary>
    internal sealed class Capabilities
    {
        private readonly HashSet<string> _objects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A capability set that claims everything, so <see cref="Sql"/> can render the full statement for the
        /// tab's "Open as query" button and the shape of the SQL stays reviewable without an instance.
        /// </summary>
        public static Capabilities All { get; } = new Capabilities { _assumeAll = true };

        private bool _assumeAll;

        public bool HasObject(string name) => _assumeAll || _objects.Contains(name);
        public bool HasColumn(string view, string column) => _assumeAll || _columns.Contains(view + "." + column);

        public void AddObject(string name) => _objects.Add(name);
        public void AddColumn(string view, string column) => _columns.Add(view + "." + column);

        /// <summary>
        /// The column if the instance has it, otherwise a typed NULL under the same alias. Typed because a bare
        /// NULL literal is an int, and the reader addresses every column by name either way.
        /// </summary>
        public string Column(string view, string column, string type, string expression = null)
        {
            return HasColumn(view, column)
                ? (expression ?? column) + " AS " + column
                : "CONVERT(" + type + ", NULL) AS " + column;
        }
    }

    private const string ProbeSql = @"
-- Which optional DMVs exist, and which optional columns the ones that do exist carry.
SELECT o.name
FROM sys.all_objects AS o
WHERE o.schema_id = SCHEMA_ID('sys')
  AND o.name IN (N'dm_os_host_info', N'dm_os_windows_info', N'dm_server_services', N'dm_tcp_listener_states',
                 N'dm_server_memory_dumps');

SELECT o.name AS object_name, c.name AS column_name
FROM sys.all_columns AS c
JOIN sys.all_objects AS o ON o.object_id = c.object_id
WHERE o.schema_id = SCHEMA_ID('sys')
  AND o.name IN (N'dm_os_sys_info', N'dm_server_services', N'dm_os_host_info');
";

    private static async Task<Capabilities> ProbeAsync(SqlConnection conn, CancellationToken ct)
    {
        var capabilities = new Capabilities();

        using (var cmd = new SqlCommand(ProbeSql, conn) { CommandTimeout = PerfQueryService.CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                capabilities.AddObject(reader.GetString(0));

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                capabilities.AddColumn(reader.GetString(0), reader.GetString(1));
        }

        return capabilities;
    }

    // ---------------------------------------------------------------------------------------------------
    // SQL
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Identity and edition. Every value comes from SERVERPROPERTY, which returns NULL for a property the
    /// release does not recognise — so newer properties cost a null rather than an error.
    /// </summary>
    private const string IdentitySql = @"
SELECT
    CONVERT(nvarchar(256), SERVERPROPERTY('ServerName'))                    AS server_name,
    CONVERT(nvarchar(256), SERVERPROPERTY('MachineName'))                   AS machine_name,
    CONVERT(nvarchar(256), SERVERPROPERTY('InstanceName'))                  AS instance_name,
    CONVERT(nvarchar(256), SERVERPROPERTY('ComputerNamePhysicalNetBIOS'))   AS netbios_name,
    CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion'))                AS product_version,
    CONVERT(nvarchar(128), SERVERPROPERTY('ProductLevel'))                  AS product_level,
    CONVERT(nvarchar(128), SERVERPROPERTY('ProductUpdateLevel'))            AS product_update_level,
    CONVERT(nvarchar(128), SERVERPROPERTY('ProductUpdateReference'))        AS product_update_reference,
    CONVERT(nvarchar(128), SERVERPROPERTY('ProductBuildType'))              AS product_build_type,
    CONVERT(nvarchar(256), SERVERPROPERTY('Edition'))                       AS edition,
    CONVERT(int,           SERVERPROPERTY('EngineEdition'))                 AS engine_edition,
    CONVERT(nvarchar(256), SERVERPROPERTY('Collation'))                     AS collation,
    CONVERT(int,           SERVERPROPERTY('IsClustered'))                   AS is_clustered,
    CONVERT(int,           SERVERPROPERTY('IsHadrEnabled'))                 AS is_hadr_enabled,
    CONVERT(int,           SERVERPROPERTY('IsFullTextInstalled'))           AS is_fulltext_installed,
    CONVERT(int,           SERVERPROPERTY('IsIntegratedSecurityOnly'))      AS integrated_security_only,
    CONVERT(int,           SERVERPROPERTY('FilestreamEffectiveLevel'))      AS filestream_level,
    CONVERT(nvarchar(256), SERVERPROPERTY('BuildClrVersion'))               AS clr_version,
    CONVERT(nvarchar(512), SERVERPROPERTY('InstanceDefaultDataPath'))       AS default_data_path,
    CONVERT(nvarchar(512), SERVERPROPERTY('InstanceDefaultLogPath'))        AS default_log_path,
    CONVERT(nvarchar(512), SERVERPROPERTY('InstanceDefaultBackupPath'))     AS default_backup_path,
    CONVERT(nvarchar(max), @@VERSION)                                       AS version_string;
";

    private static string SysInfoSql(Capabilities c) => $@"
SELECT
    si.cpu_count,
    si.hyperthread_ratio,
    si.ms_ticks,
    si.sqlserver_start_time,
    DATEDIFF(second, si.sqlserver_start_time, GETDATE())                    AS uptime_seconds,
    {c.Column("dm_os_sys_info", "scheduler_count", "int", "si.scheduler_count")},
    {c.Column("dm_os_sys_info", "max_workers_count", "int", "si.max_workers_count")},
    {c.Column("dm_os_sys_info", "socket_count", "int", "si.socket_count")},
    {c.Column("dm_os_sys_info", "cores_per_socket", "int", "si.cores_per_socket")},
    {c.Column("dm_os_sys_info", "numa_node_count", "int", "si.numa_node_count")},
    {c.Column("dm_os_sys_info", "physical_memory_kb", "bigint", "si.physical_memory_kb")},
    {c.Column("dm_os_sys_info", "committed_kb", "bigint", "si.committed_kb")},
    {c.Column("dm_os_sys_info", "committed_target_kb", "bigint", "si.committed_target_kb")},
    {c.Column("dm_os_sys_info", "virtual_machine_type_desc", "nvarchar(60)", "si.virtual_machine_type_desc")},
    {c.Column("dm_os_sys_info", "softnuma_configuration_desc", "nvarchar(60)", "si.softnuma_configuration_desc")},
    {c.Column("dm_os_sys_info", "affinity_type_desc", "nvarchar(60)", "si.affinity_type_desc")},
    {c.Column("dm_os_sys_info", "container_type_desc", "nvarchar(60)", "si.container_type_desc")}
FROM sys.dm_os_sys_info AS si;
";

    /// <summary>
    /// The OS. <c>sys.dm_os_host_info</c> is 2017 and later (and the only one of the two that knows about Linux);
    /// <c>sys.dm_os_windows_info</c> covers the releases before it. When neither is present the tab falls back to
    /// the OS text inside @@VERSION, which every release carries.
    /// </summary>
    private static string HostInfoSql(Capabilities c)
    {
        if (c.HasObject("dm_os_host_info"))
        {
            return $@"
SELECT
    hi.host_platform,
    hi.host_distribution,
    hi.host_release,
    hi.host_service_pack_level,
    {c.Column("dm_os_host_info", "host_sku", "int", "hi.host_sku")},
    hi.os_language_version
FROM sys.dm_os_host_info AS hi;
";
        }

        if (c.HasObject("dm_os_windows_info"))
        {
            // Its documented column set is exactly windows_release, windows_service_pack_level, windows_sku and
            // os_language_version — there is no distribution or SKU *name* column, so that alias is a typed NULL
            // rather than an invented one. Naming a column this view does not have would not cost one row: the
            // batch binds as a whole, so it would empty the entire tab on every release before 2017.
            return @"
SELECT
    CONVERT(nvarchar(256), N'Windows')          AS host_platform,
    CONVERT(nvarchar(256), NULL)                AS host_distribution,
    CONVERT(nvarchar(256), wi.windows_release)  AS host_release,
    CONVERT(nvarchar(256), wi.windows_service_pack_level) AS host_service_pack_level,
    CONVERT(int, wi.windows_sku)                AS host_sku,
    CONVERT(int, wi.os_language_version)        AS os_language_version
FROM sys.dm_os_windows_info AS wi;
";
        }

        // Nothing to read, but the reader still expects a result set in this position.
        return @"
SELECT CONVERT(nvarchar(256), NULL) AS host_platform, CONVERT(nvarchar(256), NULL) AS host_distribution,
       CONVERT(nvarchar(256), NULL) AS host_release, CONVERT(nvarchar(256), NULL) AS host_service_pack_level,
       CONVERT(int, NULL) AS host_sku, CONVERT(int, NULL) AS os_language_version
WHERE 1 = 0;
";
    }

    /// <summary>
    /// <c>value</c> is what was configured and <c>value_in_use</c> is what the engine is running with. They
    /// differ on a setting that has been changed but not yet taken effect, which the tab flags — a max-memory
    /// change nobody restarted for is invisible everywhere else.
    /// </summary>
    private static string ConfigurationSql() => @"
SELECT
    cfg.name,
    CONVERT(bigint, cfg.value)          AS configured_value,
    CONVERT(bigint, cfg.value_in_use)   AS running_value,
    cfg.is_dynamic,
    cfg.description
FROM sys.configurations AS cfg
WHERE cfg.name IN (N'" + string.Join("', N'", ConfigurationNames) + @"')
ORDER BY cfg.name;
";

    /// <summary>
    /// The service account and startup type, plus instant file initialization where the release reports it —
    /// the one performance-relevant host setting that cannot be read any other way from T-SQL.
    /// </summary>
    private static string ServicesSql(Capabilities c)
    {
        if (!c.HasObject("dm_server_services"))
        {
            return @"
SELECT CONVERT(nvarchar(256), NULL) AS servicename, CONVERT(nvarchar(256), NULL) AS startup_type_desc,
       CONVERT(nvarchar(256), NULL) AS status_desc, CONVERT(nvarchar(256), NULL) AS service_account,
       CONVERT(nvarchar(256), NULL) AS cluster_nodename, CONVERT(bit, NULL) AS instant_file_initialization_enabled
WHERE 1 = 0;
";
        }

        return $@"
SELECT
    svc.servicename,
    svc.startup_type_desc,
    svc.status_desc,
    svc.service_account,
    {c.Column("dm_server_services", "cluster_nodename", "nvarchar(256)", "svc.cluster_nodename")},
    {c.Column("dm_server_services", "instant_file_initialization_enabled", "nvarchar(1)", "svc.instant_file_initialization_enabled")}
FROM sys.dm_server_services AS svc;
";
    }

    /// <summary>
    /// The port the instance listens on. Taken from the listener states rather than this session's
    /// <c>local_tcp_port</c>, which is NULL whenever the connection came in over shared memory or a named pipe —
    /// exactly the case when SSMS is running on the server itself.
    /// </summary>
    private static string ListenerSql(Capabilities c)
    {
        if (!c.HasObject("dm_tcp_listener_states"))
        {
            return @"
SELECT TOP (1) CONVERT(int, c.local_tcp_port) AS port
FROM sys.dm_exec_connections AS c
WHERE c.session_id = @@SPID AND c.local_tcp_port IS NOT NULL;
";
        }

        return @"
SELECT TOP (1) CONVERT(int, ls.port) AS port
FROM sys.dm_tcp_listener_states AS ls
WHERE ls.type_desc = N'TSQL' AND ls.state_desc = N'ONLINE'
ORDER BY ls.is_ipv4 DESC, ls.port;
";
    }

    /// <summary>
    /// tempdb's file layout. The count and whether the data files are evenly sized are the two things anyone
    /// asks about tempdb first, and neither is visible anywhere else on this dashboard.
    /// </summary>
    private const string TempdbSql = @"
SELECT
    COUNT(*)                                        AS data_file_count,
    SUM(CONVERT(bigint, mf.size)) * 8 / 1024        AS total_mb,
    COUNT(DISTINCT mf.size)                         AS distinct_sizes,
    SUM(CASE WHEN mf.growth = 0 THEN 1 ELSE 0 END)  AS fixed_size_files,
    SUM(CASE WHEN mf.is_percent_growth = 1 THEN 1 ELSE 0 END) AS percent_growth_files
FROM sys.master_files AS mf
WHERE mf.database_id = DB_ID(N'tempdb') AND mf.type_desc = N'ROWS';
";

    /// <summary>
    /// Memory dumps the engine has written. Nothing else on this dashboard says the instance has crashed or hit an
    /// assertion — a dump is written and the service carries on, so unless someone was watching the error log at
    /// the time it leaves no trace anywhere a DBA routinely looks.
    ///
    /// <para>Ordered newest first and capped, because the row that matters is the most recent one and a server
    /// that dumps repeatedly can accumulate hundreds. The count is taken separately so the cap never understates
    /// how many there are.</para>
    ///
    /// <para>The view is 2008 R2 SP1 and later and is absent on Azure SQL Database, so it is probed like the rest;
    /// when it is missing this still returns a result set with the same columns and no rows, or every later read
    /// would shift up by one and the tempdb figures would be read as dump rows.</para>
    /// </summary>
    private static string MemoryDumpsSql(Capabilities c)
    {
        if (!c.HasObject("dm_server_memory_dumps"))
        {
            return @"
SELECT CONVERT(nvarchar(4000), NULL) AS filename, CONVERT(datetime, NULL) AS creation_time,
       CONVERT(decimal(18,1), NULL) AS size_mb, CONVERT(int, NULL) AS dump_count
WHERE 1 = 0;
";
        }

        return @"
SELECT TOP (20)
    md.filename,
    md.creation_time,
    CONVERT(decimal(18,1), md.size_in_bytes / 1048576.0)     AS size_mb,
    (SELECT COUNT(*) FROM sys.dm_server_memory_dumps)        AS dump_count
FROM sys.dm_server_memory_dumps AS md
ORDER BY md.creation_time DESC;
";
    }

    /// <summary>The whole statement, for the tab's "Open as query" button.</summary>
    internal static string Sql(Capabilities capabilities) =>
        IdentitySql + SysInfoSql(capabilities) + HostInfoSql(capabilities) + ConfigurationSql()
        + ServicesSql(capabilities) + ListenerSql(capabilities) + TempdbSql + MemoryDumpsSql(capabilities);

    // ---------------------------------------------------------------------------------------------------
    // Collection
    // ---------------------------------------------------------------------------------------------------

    /// <param name="recentDumpDays">
    /// From <c>SQLExtendedSettings.PerfRecentDumpDays</c>, read on the UI thread and passed down because this runs on
    /// a worker and <c>SQLExtendedSettings.Current</c> must not be faulted in from one.
    /// </param>
    public static async Task<PerfServerInfo> CollectAsync(SqlConnection conn, DateTime asOf, int recentDumpDays, CancellationToken ct)
    {
        var capabilities = await ProbeAsync(conn, ct).ConfigureAwait(false);
        var info = new PerfServerInfo { CollectedAtLocal = DateTime.Now };

        using (var cmd = new SqlCommand(Sql(capabilities), conn) { CommandTimeout = PerfQueryService.CommandTimeoutSeconds })
        using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            int? cpuCount = null;

            if (await reader.ReadAsync(ct).ConfigureAwait(false)) ReadIdentity(reader, info);

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false)) cpuCount = ReadSysInfo(reader, info);

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false)) ReadHostInfo(reader, info);

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            var configurations = new List<ConfigurationRow>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                configurations.Add(new ConfigurationRow
                {
                    Name = Str(reader, "name"),
                    Configured = Long(reader, "configured_value"),
                    Running = Long(reader, "running_value"),
                    Description = Str(reader, "description")
                });
            }

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) ReadService(reader, info);

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
                Add(info, GroupInstance, "TCP port", Text(Int(reader, "port")));

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false)) ReadTempdb(reader, info);

            await reader.NextResultAsync(ct).ConfigureAwait(false);
            await ReadMemoryDumpsAsync(reader, info, asOf, capabilities.HasObject("dm_server_memory_dumps"), recentDumpDays, ct).ConfigureAwait(false);

            // Last, because two of the warnings it raises depend on the host's core count.
            AddConfiguration(info, configurations, cpuCount);
        }

        ApplyBuildMatch(info, asOf);
        SortByGroup(info);
        return info;
    }

    /// <summary>
    /// Groups the properties for display. They are added in the order the result sets arrive, which interleaves
    /// them — the instance's uptime comes from the second read and the release's support dates are worked out
    /// after the reader closes, so an unsorted grid puts three separate runs of "Instance" rows down the page.
    /// The grid is deliberately left unsorted, so this order is the layout.
    /// </summary>
    private static readonly string[] GroupOrder =
    {
        GroupInstance, GroupVersion, GroupHost, GroupService, GroupStorage, GroupConfiguration
    };

    private static void SortByGroup(PerfServerInfo info)
    {
        var sorted = new List<PerfServerPropertyRow>(info.Properties.Count);

        foreach (var group in GroupOrder)
            foreach (var row in info.Properties)
                if (row.Group == group) sorted.Add(row);

        // A group not named above keeps its rows rather than losing them — the count has to come out the same.
        foreach (var row in info.Properties)
            if (Array.IndexOf(GroupOrder, row.Group) < 0) sorted.Add(row);

        info.Properties.Clear();
        info.Properties.AddRange(sorted);
    }

    private sealed class ConfigurationRow
    {
        public string Name;
        public long? Configured;
        public long? Running;
        public string Description;
    }

    // ---------------------------------------------------------------------------------------------------
    // Sections
    // ---------------------------------------------------------------------------------------------------

    private static void ReadIdentity(SqlDataReader reader, PerfServerInfo info)
    {
        info.ServerName = Str(reader, "server_name");
        info.ProductVersion = Str(reader, "product_version");
        info.ProductLevel = Str(reader, "product_level");
        info.ProductUpdateLevel = Str(reader, "product_update_level");
        info.Edition = Str(reader, "edition");
        info.EngineEdition = Int(reader, "engine_edition");

        // @@VERSION is several lines; the first is the product and the rest names the OS. Kept whole for the
        // tooltip because it is the one string every version of SQL Server answers the question with.
        info.VersionString = Collapse(Str(reader, "version_string"));

        string instanceName = Str(reader, "instance_name");

        Add(info, GroupInstance, "Server name", info.ServerName, "SERVERPROPERTY('ServerName') — the instance's own name for itself, which can differ from the name you connected to.");
        Add(info, GroupInstance, "Machine name", Str(reader, "machine_name"));
        Add(info, GroupInstance, "Instance", string.IsNullOrEmpty(instanceName) ? "MSSQLSERVER (default instance)" : instanceName);

        string netbios = Str(reader, "netbios_name");
        Add(info, GroupInstance, "Physical NetBIOS name", netbios,
            "On a failover cluster instance this is the node currently running the instance, not the virtual network name.");

        Add(info, GroupInstance, "Clustered", YesNo(Int(reader, "is_clustered")));
        Add(info, GroupInstance, "Always On enabled", YesNo(Int(reader, "is_hadr_enabled")));
        // Left out rather than guessed when the property returns NULL: "mixed" is a statement about the
        // instance's security posture and the wrong one is worse than no row.
        int? windowsOnly = Int(reader, "integrated_security_only");
        Add(info, GroupInstance, "Authentication",
            windowsOnly == null ? null : (windowsOnly == 1 ? "Windows only" : "SQL Server and Windows (mixed)"));
        Add(info, GroupInstance, "Collation", Str(reader, "collation"));
        Add(info, GroupInstance, "Full-text installed", YesNo(Int(reader, "is_fulltext_installed")));
        Add(info, GroupInstance, "FILESTREAM level", FilestreamLevel(Int(reader, "filestream_level")));

        Add(info, GroupVersion, "Product version", info.ProductVersion);
        Add(info, GroupVersion, "Product level", info.ProductLevel, "RTM, or SPn on a release that had service packs.");
        Add(info, GroupVersion, "Update level", info.ProductUpdateLevel, "The cumulative update the instance reports, where the release reports one.");
        Add(info, GroupVersion, "Update KB", Str(reader, "product_update_reference"));
        Add(info, GroupVersion, "Build type", BuildType(Str(reader, "product_build_type")));
        Add(info, GroupVersion, "Edition", info.Edition);
        Add(info, GroupVersion, "Engine edition", info.EngineEditionDescription);
        Add(info, GroupVersion, "CLR version", Str(reader, "clr_version"));
        Add(info, GroupVersion, "Version string", info.VersionString, info.VersionString);

        Add(info, GroupStorage, "Default data path", Str(reader, "default_data_path"));
        Add(info, GroupStorage, "Default log path", Str(reader, "default_log_path"));
        Add(info, GroupStorage, "Default backup path", Str(reader, "default_backup_path"));
    }

    private static int? ReadSysInfo(SqlDataReader reader, PerfServerInfo info)
    {
        int? cpuCount = Int(reader, "cpu_count");
        info.StartTime = Date(reader, "sqlserver_start_time");
        info.UptimeSeconds = Long(reader, "uptime_seconds");

        Add(info, GroupInstance, "Started", info.StartTime == null ? null : info.StartTime.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture));
        Add(info, GroupInstance, "Uptime", Duration(info.UptimeSeconds), "Measured on the server, as at the time this tab was collected.");

        int? socketCount = Int(reader, "socket_count");
        int? coresPerSocket = Int(reader, "cores_per_socket");
        int? hyperthreadRatio = Int(reader, "hyperthread_ratio");

        Add(info, GroupHost, "Logical CPUs", Text(cpuCount));
        Add(info, GroupHost, "Sockets", Text(socketCount));
        Add(info, GroupHost, "Cores per socket", Text(coresPerSocket));
        Add(info, GroupHost, "Hyperthread ratio", Text(hyperthreadRatio),
            "Logical processors per physical core, as SQL Server sees it — used with the socket and core counts to work out licensing.");
        Add(info, GroupHost, "NUMA nodes", Text(Int(reader, "numa_node_count")));
        Add(info, GroupHost, "Soft-NUMA", Str(reader, "softnuma_configuration_desc"));
        Add(info, GroupHost, "Affinity", Str(reader, "affinity_type_desc"));
        Add(info, GroupHost, "Schedulers", Text(Int(reader, "scheduler_count")));
        Add(info, GroupHost, "Max worker threads", Text(Int(reader, "max_workers_count")),
            "The limit in effect, which is derived from the core count when the 'max worker threads' setting is 0.");
        Add(info, GroupHost, "Physical memory", Memory(Long(reader, "physical_memory_kb")));
        Add(info, GroupHost, "Memory committed", Memory(Long(reader, "committed_kb")));
        Add(info, GroupHost, "Memory target", Memory(Long(reader, "committed_target_kb")),
            "What SQL Server would like to commit — well above the committed figure means it is under memory pressure.");
        Add(info, GroupHost, "Virtualisation", Str(reader, "virtual_machine_type_desc"));
        Add(info, GroupHost, "Container", Str(reader, "container_type_desc"));

        return cpuCount;
    }

    private static void ReadHostInfo(SqlDataReader reader, PerfServerInfo info)
    {
        Add(info, GroupHost, "OS platform", Str(reader, "host_platform"));
        Add(info, GroupHost, "OS", Str(reader, "host_distribution"));
        Add(info, GroupHost, "OS release", Str(reader, "host_release"));
        Add(info, GroupHost, "OS service pack", Str(reader, "host_service_pack_level"));
        Add(info, GroupHost, "OS language", Text(Int(reader, "os_language_version")));
    }

    private static void ReadService(SqlDataReader reader, PerfServerInfo info)
    {
        string name = Str(reader, "servicename") ?? "Service";

        // sys.dm_server_services returns a row per service (engine, Agent, full-text), so the service name is
        // part of the property name rather than a column — the grid is name/value.
        Add(info, GroupService, name + " — status", Str(reader, "status_desc"));
        Add(info, GroupService, name + " — startup", Str(reader, "startup_type_desc"));
        Add(info, GroupService, name + " — account", Str(reader, "service_account"));

        string clusterNode = Str(reader, "cluster_nodename");
        if (!string.IsNullOrEmpty(clusterNode))
            Add(info, GroupService, name + " — cluster node", clusterNode);

        string ifi = Str(reader, "instant_file_initialization_enabled");
        if (!string.IsNullOrEmpty(ifi))
        {
            bool enabled = ifi.Trim().StartsWith("Y", StringComparison.OrdinalIgnoreCase);
            Add(info, GroupService, name + " — instant file initialization", enabled ? "Yes" : "No",
                "Without it every data file growth is zero-filled first, which stalls writes for the duration. "
              + "Granted by the 'Perform volume maintenance tasks' privilege on the service account.",
                warning: !enabled);
        }
    }

    private static void ReadTempdb(SqlDataReader reader, PerfServerInfo info)
    {
        int? fileCount = Int(reader, "data_file_count");
        int? distinctSizes = Int(reader, "distinct_sizes");

        Add(info, GroupStorage, "tempdb data files", Text(fileCount));
        Add(info, GroupStorage, "tempdb total size", fileCount == null ? null : Text(Long(reader, "total_mb")) + " MB");

        // Unevenly sized tempdb data files defeat proportional fill: the largest file takes most of the
        // allocations, so the extra files stop spreading allocation contention the way they were added to.
        Add(info, GroupStorage, "tempdb files evenly sized", distinctSizes == null ? null : (distinctSizes == 1 ? "Yes" : "No"),
            "Proportional fill sends allocations to the file with the most free space, so mismatched sizes leave one file taking most of them.",
            warning: distinctSizes > 1);

        Add(info, GroupStorage, "tempdb files with percentage growth", Text(Int(reader, "percent_growth_files")),
            "Percentage growth on tempdb makes each growth event larger than the last.",
            warning: Int(reader, "percent_growth_files") > 0);
    }

    /// <summary>
    /// Records what the engine has dumped and flags it when it is recent. Every dump is a crash, an assertion or a
    /// non-yielding scheduler that SQL Server wrote a file about and then carried on from, so nothing else on this
    /// dashboard — or in SSMS — will mention it unless someone reads the error log for the right day.
    ///
    /// <para>The newest dump's name is given in the hint rather than the value: it is a full path and would push
    /// everything else out of the column, but it is the thing to hand to whoever asks which dump.</para>
    /// </summary>
    /// <param name="available">
    /// Whether the instance actually has the view. Without it there is no row at all rather than a "None", which
    /// on Azure SQL Database or a pre-2008 R2 SP1 release would be an answer this cannot give.
    /// </param>
    /// <param name="recentDays">
    /// How recent a dump has to be to be flagged, from <c>SQLExtendedSettings.PerfRecentDumpDays</c> and read on the
    /// UI thread by the control. Zero or less lists the dumps without flagging any of them.
    /// </param>
    private static async Task ReadMemoryDumpsAsync(SqlDataReader reader, PerfServerInfo info, DateTime asOf, bool available, int recentDays, CancellationToken ct)
    {
        int total = 0;
        DateTime? newest = null;
        long? newestSizeMb = null;
        string newestFile = null;
        int recent = 0;

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // The count comes from the view rather than from these rows, which are capped — see MemoryDumpsSql.
            total = Int(reader, "dump_count") ?? 0;

            var created = Date(reader, "creation_time");
            if (created == null) continue;

            if (recentDays > 0 && created.Value >= asOf.AddDays(-recentDays)) recent++;

            // Rows arrive newest first, so the first one with a time is the one to describe.
            if (newest != null) continue;

            newest = created;
            newestSizeMb = Long(reader, "size_mb");
            newestFile = Str(reader, "filename");
        }

        if (total == 0)
        {
            if (available) Add(info, GroupService, "Memory dumps", "None");
            return;
        }

        string value = total.ToString("N0", CultureInfo.CurrentCulture) + (total == 1 ? " dump" : " dumps");
        if (newest != null)
        {
            value += ", newest " + newest.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            if (newestSizeMb != null) value += " (" + newestSizeMb.Value.ToString("N0", CultureInfo.CurrentCulture) + " MB)";
        }

        string hint = "sys.dm_server_memory_dumps. SQL Server writes one of these on a crash, an assertion or a non-yielding scheduler and then carries on, "
                    + "so nothing else reports it after the fact. Check the SQL Server error log around that time for what caused it."
                    + (newestFile == null ? "" : "  Newest: " + newestFile);

        Add(info, GroupService, "Memory dumps", value, hint, warning: recent > 0);

        if (recent > 0 && recent < total)
        {
            Add(info, GroupService, "Memory dumps in the last " + recentDays + " days", recent.ToString("N0", CultureInfo.CurrentCulture),
                "Recent dumps are the ones worth acting on; the total above includes historical ones this instance has kept. "
                + "The window is set by \"Flag memory dumps newer than\" in SQLExtended settings.",
                warning: true);
        }
    }

    /// <summary>
    /// Turns the configuration rows into properties, decoding the values whose raw form misleads and flagging
    /// the handful that are worth a second look. Kept deliberately short: this is a facts tab, not a rules
    /// engine, and a screenful of amber would make the real findings invisible.
    /// </summary>
    private static void AddConfiguration(PerfServerInfo info, List<ConfigurationRow> rows, int? cpuCount)
    {
        foreach (var row in rows)
        {
            string value = Text(row.Running);
            string hint = row.Description;
            bool warning = false;

            switch (row.Name)
            {
                case "max server memory (MB)":
                    // 2147483647 MB is the shipped default and means "no limit": SQL Server will take memory
                    // until Windows pushes back, which on a shared host is felt as everything else starving.
                    if (row.Running == 2147483647)
                    {
                        value = "2147483647 MB (default — no limit)";
                        warning = true;
                        hint = "SQL Server will grow its buffer pool until the OS is under pressure. Cap it below the host's physical memory.";
                    }
                    else
                    {
                        value = Text(row.Running) + " MB";
                    }
                    break;

                case "min server memory (MB)":
                    value = Text(row.Running) + " MB";
                    break;

                case "max degree of parallelism":
                    if (row.Running == 0)
                    {
                        value = "0 (unlimited)";
                        // Unlimited MAXDOP on a wide host lets one query take every scheduler.
                        warning = cpuCount > 8;
                        hint = "0 lets a query use every scheduler. On a host this wide that is usually worth capping.";
                    }
                    break;

                case "cost threshold for parallelism":
                    if (row.Running == 5)
                    {
                        value = "5 (default)";
                        warning = true;
                        hint = "The default has not changed since 1998. It is low enough that trivial queries go parallel.";
                    }
                    break;

                case "priority boost":
                    warning = row.Running == 1;
                    if (warning) hint = "Raising SQL Server's thread priority above the OS's own is documented as unsupported outside a specific cluster scenario.";
                    break;

                case "lightweight pooling":
                    warning = row.Running == 1;
                    if (warning) hint = "Fibre mode disables CLR, backup-to-URL and several other features, and rarely helps.";
                    break;

                case "xp_cmdshell":
                    warning = row.Running == 1;
                    if (warning) hint = "Enabled. Anything that can run xp_cmdshell runs it as the SQL Server service account.";
                    break;

                case "optimize for ad hoc workloads":
                case "backup compression default":
                case "clr enabled":
                case "remote admin connections":
                case "default trace enabled":
                case "common criteria compliance enabled":
                case "automatic soft-NUMA disabled":
                case "tempdb metadata memory-optimized":
                    value = OnOff(row.Running);
                    break;
            }

            // A setting changed but not restarted into is invisible everywhere else, and it means the server is
            // not running the configuration whoever changed it believes it is.
            if (row.Configured != null && row.Running != null && row.Configured != row.Running)
            {
                value += " — configured " + Text(row.Configured) + ", not yet in effect";
                warning = true;
                hint = "This setting has been changed but has not taken effect. It needs a restart of the instance.";
            }

            Add(info, GroupConfiguration, row.Name, value, hint, warning);
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // Build matching
    // ---------------------------------------------------------------------------------------------------

    private static void ApplyBuildMatch(PerfServerInfo info, DateTime asOf)
    {
        info.Build = SqlBuildCatalog.Lookup(info.ProductVersion, asOf);

        var release = info.Build.Release;
        if (release == null) return;

        Add(info, GroupVersion, "Release", release.Name
            + (string.IsNullOrEmpty(release.Codename) ? "" : " (codename " + release.Codename + ")"));
        Add(info, GroupVersion, "Released", IsoDate(release.Released));
        Add(info, GroupVersion, "Mainstream support ends", IsoDate(release.MainstreamSupportEnd));
        Add(info, GroupVersion, "Extended support ends", IsoDate(release.ExtendedSupportEnd));

        // The list is not exhaustive, so anything below the server's build is "at least this level" rather than
        // an identification — the wording has to say which of the two it is.
        var best = info.Build.Best;
        if (best != null)
        {
            Add(info, GroupVersion, "Servicing level",
                info.Build.Exact != null ? best.Display : best.Display + " or later (this exact build is not listed)",
                best.Description);
            Add(info, GroupVersion, "Build released", IsoDate(best.Released));
        }

        if (info.IsAzureManaged) return;

        foreach (var build in release.Builds)
        {
            if (build.Version > info.Build.Version) info.NewerBuilds.Add(build);
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // Formatting helpers
    // ---------------------------------------------------------------------------------------------------

    private static void Add(PerfServerInfo info, string group, string name, string value, string hint = null, bool warning = false)
    {
        // A property the release does not report is left out rather than shown blank — a grid of empty rows
        // reads as "this failed" when the truth is "this version has no such thing".
        if (string.IsNullOrWhiteSpace(value)) return;

        info.Properties.Add(new PerfServerPropertyRow
        {
            Group = group,
            Name = name,
            Value = value,
            Hint = hint,
            IsWarning = warning
        });
    }

    private static string Text(int? value) => value?.ToString("N0", CultureInfo.CurrentCulture);
    private static string Text(long? value) => value?.ToString("N0", CultureInfo.CurrentCulture);
    private static string YesNo(int? value) => value == null ? null : (value == 1 ? "Yes" : "No");
    private static string OnOff(long? value) => value == null ? null : (value == 1 ? "Enabled" : "Disabled");
    private static string IsoDate(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Memory(long? kb)
    {
        if (kb == null) return null;
        double gb = kb.Value / 1024d / 1024d;
        return gb >= 1
            ? gb.ToString("N1", CultureInfo.CurrentCulture) + " GB"
            : (kb.Value / 1024d).ToString("N0", CultureInfo.CurrentCulture) + " MB";
    }

    internal static string Duration(long? seconds)
    {
        if (seconds == null || seconds < 0) return null;

        var span = TimeSpan.FromSeconds(seconds.Value);
        if (span.TotalDays >= 1)
            return string.Format(CultureInfo.CurrentCulture, "{0:N0}d {1:00}h {2:00}m", Math.Floor(span.TotalDays), span.Hours, span.Minutes);

        return string.Format(CultureInfo.CurrentCulture, "{0:00}h {1:00}m", span.Hours, span.Minutes);
    }

    private static string FilestreamLevel(int? level)
    {
        switch (level)
        {
            case 0: return "Disabled";
            case 1: return "T-SQL access only";
            case 2: return "T-SQL and local file system access";
            case 3: return "T-SQL and remote file system access";
            default: return null;
        }
    }

    private static string BuildType(string type)
    {
        if (string.IsNullOrEmpty(type)) return null;

        switch (type.Trim())
        {
            case "OD": return "OD (on-demand hotfix, for one customer's issue)";
            case "GDR": return "GDR (general distribution — security and critical fixes only)";
            case "NULL": return null;
            default: return type.Trim();
        }
    }

    private static string Collapse(string value) =>
        string.IsNullOrEmpty(value) ? value : System.Text.RegularExpressions.Regex.Replace(value, @"\s+", " ").Trim();

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

    private static DateTime? Date(SqlDataReader reader, string name)
    {
        int i = reader.GetOrdinal(name);
        return reader.IsDBNull(i) ? (DateTime?)null : Convert.ToDateTime(reader.GetValue(i));
    }
}
