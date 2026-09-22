using System;
using System.Text;

namespace SQLExtended.Monitoring.AlwaysOn;

/// <summary>
/// Where the section queries read availability-group state from: the HADR views directly, or per-poll temp copies
/// of them.
///
/// <para>Reading any one of these views is quick. <b>Joining them is not.</b> None of them is a table — each is a
/// view over an internal function, carrying no statistics and a fixed cardinality guess, and nothing stops the
/// optimiser putting one on the inner side of a nested loop and calling it again for every outer row. With a
/// handful of groups nobody notices; at fifty-odd groups the Databases tab's four-way join takes minutes while
/// each of its four views on its own returns instantly, which is exactly the shape of the complaint that started
/// this: single DMV fast, joined DMVs unusable. <c>SELECT * INTO #copy</c> per view forces exactly one execution
/// of each and leaves the joins to run between heaps. That is the whole fix —
/// https://dba.stackexchange.com/questions/131621 is the report it came from, and KB3173038 is Microsoft's own
/// narrower version of it for <c>sys.dm_hadr_availability_replica_states</c>.</para>
///
/// <para>The copies are made once per poll, on that poll's own connection, and die with it: temp tables are
/// session-scoped and the connection is opened and closed inside <see cref="AgQueryService.CollectAsync"/>, so
/// there is no cache to invalidate and no way to paint a previous poll's numbers. Two side effects worth having:
/// the views that several sections each joined — <c>sys.availability_groups</c> was read by seven of the nine —
/// are now read once per poll rather than once per section, and every tab is built from one consistent read of
/// the state rather than nine staggered ones.</para>
///
/// <para>An instance of this class is the switch, not a copy of anything: a poll starts on
/// <see cref="Views"/> and is moved onto the copies by <see cref="UseCopies"/> once the copy batch has actually
/// succeeded. That ordering is the fallback — if the batch throws, its warning stands alone and every section
/// behind it reads the views directly, which is slow but is precisely the behaviour these queries had before.</para>
/// </summary>
internal sealed class AgCatalog
{
    private bool _readsCopies;

    /// <summary>A poll's switch, starting on the views. <see cref="UseCopies"/> moves it once the copies exist.</summary>
    public AgCatalog() : this(false) { }

    private AgCatalog(bool readsCopies) { _readsCopies = readsCopies; }

    /// <summary>The views, named as the sections named them before the copies existed. Also the source side of the copy batch.</summary>
    private static AgCatalog Views { get; } = new AgCatalog(false);

    /// <summary>The copies. Public for the "Open as query" path, which wants this shape without a poll to hang it off.</summary>
    public static AgCatalog Copies { get; } = new AgCatalog(true);

    /// <summary>
    /// Points every section behind the caller at the temp copies. Called once per poll, and only after the copy
    /// batch has returned — see the class remarks for why the order matters.
    /// </summary>
    public void UseCopies() => _readsCopies = true;

    // Every copy name starts with CopyPrefix, which is what Standalone looks for to decide whether a batch it is
    // about to hand the user needs the copy statements in front of it.
    private const string CopyPrefix = "#ag_";

    public string Groups => _readsCopies ? CopyPrefix + "groups" : "sys.availability_groups";
    public string Replicas => _readsCopies ? CopyPrefix + "replicas" : "sys.availability_replicas";
    public string GroupStates => _readsCopies ? CopyPrefix + "group_states" : "sys.dm_hadr_availability_group_states";
    public string ReplicaStates => _readsCopies ? CopyPrefix + "replica_states" : "sys.dm_hadr_availability_replica_states";
    public string DatabaseStates => _readsCopies ? CopyPrefix + "database_states" : "sys.dm_hadr_database_replica_states";
    public string DatabaseClusterStates => _readsCopies ? CopyPrefix + "database_cluster_states" : "sys.dm_hadr_database_replica_cluster_states";
    public string ReplicaClusterNodes => _readsCopies ? CopyPrefix + "replica_cluster_nodes" : "sys.dm_hadr_availability_replica_cluster_nodes";
    public string ReplicaClusterStates => _readsCopies ? CopyPrefix + "replica_cluster_states" : "sys.dm_hadr_availability_replica_cluster_states";

    /// <summary>
    /// The batch that makes the copies. It drops first, so it can be run twice in one session — the "Open as
    /// query" path hands it to the user, who will press F5 more than once.
    /// </summary>
    /// <remarks>
    /// The two cluster views are copied only when the capability probe found them. Everything else here is
    /// referenced unguarded by the sections already, so a copy of it cannot be the thing that is missing — but a
    /// release without one of the cluster views would fail the whole batch and drop the poll back onto the views,
    /// costing every tab the speed-up to protect one tab that was never going to be populated.
    /// </remarks>
    public static string PrologueSql(AgCapabilities caps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- One copy of each HADR view per poll, joined as heaps from here on: joining the views themselves");
        sb.AppendLine("-- lets the optimiser re-execute one of them per outer row. See dba.stackexchange.com/q/131621.");
        AppendCopy(sb, c => c.Groups);
        AppendCopy(sb, c => c.Replicas);
        AppendCopy(sb, c => c.GroupStates);
        AppendCopy(sb, c => c.ReplicaStates);
        AppendCopy(sb, c => c.DatabaseStates);
        AppendCopy(sb, c => c.DatabaseClusterStates);
        if (caps == null || caps.HasReplicaClusterNodes) AppendCopy(sb, c => c.ReplicaClusterNodes);
        if (caps == null || caps.HasReplicaClusterStates) AppendCopy(sb, c => c.ReplicaClusterStates);
        return sb.ToString();
    }

    // Reading both halves of the pair off the two instances keeps each view named in exactly one place — the
    // property above — rather than once there and again in a list here, where the two would eventually disagree.
    private static void AppendCopy(StringBuilder sb, Func<AgCatalog, string> name)
    {
        string copy = name(Copies), view = name(Views);
        sb.AppendLine($"IF OBJECT_ID('tempdb..{copy}') IS NOT NULL DROP TABLE {copy};");
        sb.AppendLine($"SELECT * INTO {copy} FROM {view};");
    }

    /// <summary>
    /// Prefixes a batch with the copy statements when it reads the copies, so the T-SQL the window hands back
    /// runs on its own. The point of the "Open as query" button is that you can take the query away and keep
    /// digging; a batch referencing <c>#ag_*</c> tables that nothing created would fail on the first F5.
    /// </summary>
    public static string Standalone(string sql, AgCapabilities caps)
        => sql != null && sql.IndexOf(CopyPrefix, StringComparison.Ordinal) >= 0 ? PrologueSql(caps) + Environment.NewLine + sql : sql;
}
