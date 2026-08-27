using System;
using System.Collections.Generic;
using System.Linq;

namespace SQLExtended.Monitoring.AlwaysOn;

/// <summary>
/// The thresholds the diagnostic rules judge against. Defaults are deliberately conservative — a rule that
/// fires on a healthy production system trains people to ignore the tab.
/// </summary>
internal sealed class AgThresholds
{
    /// <summary>Estimated data loss (send queue ÷ send rate) that counts as degraded, then as broken.</summary>
    public double RpoWarningSeconds { get; set; } = 60d;
    public double RpoCriticalSeconds { get; set; } = 300d;

    /// <summary>secondary_lag_seconds above this is a warning. Only meaningful where the column exists.</summary>
    public double SecondaryLagWarningSeconds { get; set; } = 60d;

    /// <summary>Queue sizes, in KB, above which a queue is called out on its own rather than only via the RPO estimate.</summary>
    public long SendQueueWarningKb { get; set; } = 100_000L;
    public long RedoQueueWarningKb { get; set; } = 100_000L;

    /// <summary>Added commit latency per transaction, in milliseconds, before synchronous commit is called expensive.</summary>
    public double CommitDelayWarningMs { get; set; } = 20d;
}

/// <summary>
/// Turns a collected <see cref="AgSnapshot"/> into a ranked list of findings.
///
/// This exists because the grids show state and state is not the same as a verdict. The worst Always On
/// conditions are combinations no single grid reveals: an automatic-failover pair whose secondary is merely
/// SYNCHRONIZING reads as healthy on both the Replicas and Databases tabs, and means the cluster cannot fail
/// over right now. A group whose synchronized secondary count has dropped below
/// required_synchronized_secondaries_to_commit is about to start refusing commits on the primary, and nothing
/// in any DMV column says so in words.
///
/// Two rules govern every check here:
///  * NULL is never a finding. Several *_state_desc columns are populated only for replicas local to the queried
///    instance, so from a secondary the primary's operational state is NULL — that means "not visible from
///    here", and firing on it would put a critical row against a perfectly healthy primary on every poll.
///  * SYNCHRONOUS_COMMIT is what makes SYNCHRONIZING interesting. On an asynchronous replica it is the normal
///    steady state, and flagging it would bury the real findings.
///
/// Pure and side-effect free apart from filling <see cref="AgSnapshot.Issues"/>, which also makes it the one
/// piece of the dashboard that could be unit tested without a server.
/// </summary>
internal static class AgDiagnostics
{
    public static void Evaluate(AgSnapshot snapshot, AgCapabilities caps, AgThresholds thresholds)
    {
        if (snapshot == null || !snapshot.IsAvailable) return;
        thresholds = thresholds ?? new AgThresholds();

        var issues = snapshot.Issues;
        issues.Clear();

        CheckCluster(snapshot, issues);
        CheckReplicas(snapshot, issues);
        CheckClusterNodes(snapshot, issues);
        CheckDatabases(snapshot, thresholds, issues);
        CheckFailoverReadiness(snapshot, issues);
        CheckCommitQuorum(snapshot, caps, issues);
        CheckListeners(snapshot, issues);
        CheckRouting(snapshot, issues);
        CheckSeeding(snapshot, issues);
        CheckThroughput(snapshot, thresholds, issues);
        CheckConfiguration(snapshot, caps, issues);

        // Worst first, then by area so repeated findings of a kind stay together.
        issues.Sort((a, b) =>
        {
            int bySeverity = a.Severity.CompareTo(b.Severity);
            if (bySeverity != 0) return bySeverity;
            int byArea = string.Compare(a.Area, b.Area, StringComparison.OrdinalIgnoreCase);
            return byArea != 0 ? byArea : string.Compare(a.Subject, b.Subject, StringComparison.OrdinalIgnoreCase);
        });

        AddAllClear(snapshot, issues);
    }

    private static void Add(List<AgIssueRow> issues, AgIssueSeverity severity, string area, string subject, string detail, string recommendation) =>
        issues.Add(new AgIssueRow { Severity = severity, Area = area, Subject = subject, Detail = detail, Recommendation = recommendation });

    // -------------------------------------------------------------------------------------------------
    // Cluster and quorum
    // -------------------------------------------------------------------------------------------------

    private static void CheckCluster(AgSnapshot snapshot, List<AgIssueRow> issues)
    {
        var cluster = snapshot.Cluster;
        if (cluster != null && !string.IsNullOrWhiteSpace(cluster.QuorumState) && !cluster.IsQuorumHealthy)
        {
            Add(issues, AgIssueSeverity.Critical, "Cluster", cluster.ClusterName ?? "(cluster)",
                $"Quorum state is {cluster.QuorumState} (quorum type {cluster.QuorumType ?? "unknown"}).",
                "Every group on this cluster goes offline if quorum is lost. Restore the down members or adjust the quorum configuration (witness / node votes) before doing anything else.");
        }

        foreach (var member in snapshot.ClusterMembers)
        {
            if (!member.IsUnhealthy) continue;

            bool votes = member.QuorumVotes.GetValueOrDefault() > 0;
            Add(issues, votes ? AgIssueSeverity.Critical : AgIssueSeverity.Warning, "Cluster", member.MemberName,
                $"Cluster member is {member.MemberState} and holds {member.QuorumVotes.GetValueOrDefault()} quorum vote(s).",
                votes
                    ? "A voting member that is down brings the cluster closer to losing quorum. Bring the node or witness back, or remove its vote until it returns."
                    : "The member is down but holds no vote, so quorum is unaffected. It still cannot host a replica.");
        }
    }

    private static void CheckClusterNodes(AgSnapshot snapshot, List<AgIssueRow> issues)
    {
        foreach (var node in snapshot.ClusterNodes)
        {
            string subject = $"{node.AgName} / {node.ReplicaServerName}";

            if (node.IsUnhealthy)
            {
                Add(issues, AgIssueSeverity.Critical, "Cluster", subject,
                    $"Replica is NOT_JOINED to the cluster group (node {node.NodeName}).",
                    "The replica is configured but is not participating. Check that the SQL Server service is running with Always On enabled on that node and that the cluster group is online.");
            }
            else if (node.IsWarning)
            {
                Add(issues, AgIssueSeverity.Warning, "Cluster", subject,
                    $"Join state is {node.JoinState} (node {node.NodeName}).",
                    "The replica is joined but cannot take part in a failover in this state. It is protecting data, not availability.");
            }
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Replicas
    // -------------------------------------------------------------------------------------------------

    private static void CheckReplicas(AgSnapshot snapshot, List<AgIssueRow> issues)
    {
        foreach (var group in snapshot.Groups)
        {
            // No primary at all: every replica is RESOLVING. Clients cannot connect to the group.
            bool anyPrimary = snapshot.Replicas.Any(r => string.Equals(r.AgName, group.Name, StringComparison.OrdinalIgnoreCase)
                                                      && AgReplicaRow.IsState(r.Role, "PRIMARY"));
            bool anyRoleKnown = snapshot.Replicas.Any(r => string.Equals(r.AgName, group.Name, StringComparison.OrdinalIgnoreCase)
                                                        && !string.IsNullOrWhiteSpace(r.Role));

            if (anyRoleKnown && !anyPrimary && string.IsNullOrEmpty(group.PrimaryReplica))
            {
                Add(issues, AgIssueSeverity.Critical, "Replica", group.Name,
                    "The group has no primary replica — every replica reports RESOLVING.",
                    "The group is offline to clients. This normally means quorum was lost or a failover is stuck; check the cluster first, then the Errors tab for the lease and role-change events.");
            }
        }

        foreach (var replica in snapshot.Replicas)
        {
            string subject = $"{replica.AgName} / {replica.ReplicaServerName}";

            if (AgReplicaRow.IsBadState(replica.ConnectedState, "CONNECTED"))
            {
                string error = string.IsNullOrWhiteSpace(replica.LastConnectErrorDescription)
                    ? ""
                    : $" Last connect error: {replica.LastConnectErrorDescription.Trim()}";

                Add(issues, AgIssueSeverity.Critical, "Replica", subject,
                    $"Replica is {replica.ConnectedState}.{error}",
                    "Nothing is being sent to this replica, so its data loss grows for as long as it stays down. Check the SQL Server service, the endpoint (" + (replica.EndpointUrl ?? "endpoint_url not reported") + ") and the firewall between the nodes.");
            }

            if (AgReplicaRow.IsState(replica.SynchronizationHealth, "NOT_HEALTHY"))
            {
                Add(issues, AgIssueSeverity.Critical, "Replica", subject,
                    "Synchronization health is NOT_HEALTHY.",
                    "At least one database on this replica is not synchronizing. The Databases tab shows which, and the suspend reason if it was suspended.");
            }
            else if (AgReplicaRow.IsState(replica.SynchronizationHealth, "PARTIALLY_HEALTHY"))
            {
                Add(issues, AgIssueSeverity.Warning, "Replica", subject,
                    "Synchronization health is PARTIALLY_HEALTHY.",
                    "Some databases are behind their target synchronization state — usually a synchronous replica sitting in SYNCHRONIZING. Check the Databases tab.");
            }

            if (AgReplicaRow.IsState(replica.OperationalState, "FAILED", "FAILED_NO_QUORUM"))
            {
                Add(issues, AgIssueSeverity.Critical, "Replica", subject,
                    $"Operational state is {replica.OperationalState}.",
                    "The replica has failed rather than being merely disconnected. FAILED_NO_QUORUM means the node cannot see the cluster.");
            }
            else if (AgReplicaRow.IsState(replica.OperationalState, "PENDING", "PENDING_FAILOVER", "ONLINE_IN_PROGRESS"))
            {
                Add(issues, AgIssueSeverity.Warning, "Replica", subject,
                    $"Operational state is {replica.OperationalState} — the replica is mid-transition.",
                    "Transitional on its own. If it stays here across several polls, treat it as stuck and check the Errors tab.");
            }

            if (AgReplicaRow.IsBadState(replica.RecoveryHealth, "ONLINE"))
            {
                Add(issues, AgIssueSeverity.Warning, "Replica", subject,
                    $"Recovery health is {replica.RecoveryHealth}.",
                    "One or more databases on the replica are not ONLINE — recovering, restoring, or in a non-ONLINE database state.");
            }
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Databases
    // -------------------------------------------------------------------------------------------------

    private static void CheckDatabases(AgSnapshot snapshot, AgThresholds thresholds, List<AgIssueRow> issues)
    {
        foreach (var db in snapshot.Databases)
        {
            string subject = $"{db.AgName} / {db.DatabaseName} on {db.ReplicaServerName}";
            bool isSynchronous = AgReplicaRow.IsState(db.AvailabilityMode, "SYNCHRONOUS_COMMIT");

            if (db.IsSuspended == true)
            {
                Add(issues, AgIssueSeverity.Critical, "Database", subject,
                    $"Data movement is suspended{(string.IsNullOrWhiteSpace(db.SuspendReason) ? "" : $" ({db.SuspendReason})")}.",
                    "Nothing is being sent or redone for this database, and the primary's log cannot be truncated while it stays suspended. Resume it once the underlying cause is fixed: ALTER DATABASE … SET HADR RESUME.");
            }

            if (db.IsDatabaseJoined == false)
            {
                Add(issues, AgIssueSeverity.Warning, "Database", subject,
                    "The database is in the availability group's configuration but is not joined on this replica.",
                    "It is not being protected here at all. Join it (ALTER DATABASE … SET HADR AVAILABILITY GROUP) or reseed it.");
            }

            if (AgReplicaRow.IsState(db.SynchronizationState, "NOT_SYNCHRONIZING"))
            {
                Add(issues, AgIssueSeverity.Critical, "Database", subject,
                    "Synchronization state is NOT_SYNCHRONIZING.",
                    "The database is not receiving log. Check the replica's connected state first; if the replica is connected, the Errors tab usually names the reason.");
            }
            else if (isSynchronous && AgReplicaRow.IsState(db.SynchronizationState, "SYNCHRONIZING") && !db.IsPrimaryReplica)
            {
                Add(issues, AgIssueSeverity.Warning, "Database", subject,
                    "A synchronous-commit secondary is SYNCHRONIZING rather than SYNCHRONIZED.",
                    "Until it catches up there is no zero-data-loss failover target for this database, and the primary is paying commit latency for a replica that cannot honour it.");
            }

            if (AgReplicaRow.IsBadState(db.DatabaseState, "ONLINE"))
            {
                Add(issues, AgIssueSeverity.Critical, "Database", subject,
                    $"Database state is {db.DatabaseState}.",
                    "The database itself is not online on this replica, which is a different problem from replication being behind.");
            }

            // RPO. Est. data loss is the send queue drained at the current send rate — what an unplanned
            // failover would lose right now.
            double? rpo = db.EstimatedDataLossSeconds;
            if (rpo != null && !db.IsPrimaryReplica)
            {
                if (rpo > thresholds.RpoCriticalSeconds)
                {
                    Add(issues, AgIssueSeverity.Critical, "Data loss", subject,
                        $"Estimated data loss is {Describe(rpo.Value)} ({FormatKb(db.LogSendQueueKb)} of send queue at {FormatKb(db.LogSendRateKbSec)}/s).",
                        $"Past the {Describe(thresholds.RpoCriticalSeconds)} threshold. Failing over now would lose roughly that much work. Look at network throughput to this replica and at whether the primary is generating log faster than the link can carry.");
                }
                else if (rpo > thresholds.RpoWarningSeconds)
                {
                    Add(issues, AgIssueSeverity.Warning, "Data loss", subject,
                        $"Estimated data loss is {Describe(rpo.Value)}.",
                        $"Above the {Describe(thresholds.RpoWarningSeconds)} warning threshold but still moving. Worth watching the send-queue trend on the Databases tab.");
                }
            }

            if (db.SecondaryLagSeconds.GetValueOrDefault() > thresholds.SecondaryLagWarningSeconds && !db.IsPrimaryReplica)
            {
                Add(issues, AgIssueSeverity.Warning, "Data loss", subject,
                    $"Secondary lag is {Describe(db.SecondaryLagSeconds.Value)} behind the primary.",
                    "This is the server's own measure of how far behind the secondary is, and it is what a read-intent workload on this replica is seeing.");
            }

            // A queue that is not draining. Rate zero with a non-empty queue is the interesting shape — a big
            // queue that is moving fast is a busy system, not a broken one.
            if (db.LogSendQueueKb.GetValueOrDefault() > thresholds.SendQueueWarningKb && db.LogSendRateKbSec.GetValueOrDefault() <= 0 && !db.IsPrimaryReplica)
            {
                Add(issues, AgIssueSeverity.Warning, "Queue", subject,
                    $"Send queue is {FormatKb(db.LogSendQueueKb)} and the send rate is zero.",
                    "Log is queued on the primary and nothing is going out. Check the replica's connected state and the transport counters on the Throughput tab (flow control, resent messages).");
            }

            if (db.RedoQueueKb.GetValueOrDefault() > thresholds.RedoQueueWarningKb && db.RedoRateKbSec.GetValueOrDefault() <= 0 && !db.IsPrimaryReplica)
            {
                Add(issues, AgIssueSeverity.Warning, "Queue", subject,
                    $"Redo queue is {FormatKb(db.RedoQueueKb)} and the redo rate is zero.",
                    "Log has arrived but is not being applied. The classic cause is a long-running read query on this readable secondary blocking the redo thread; look for a session holding a schema stability lock there.");
            }
            else if (db.RedoQueueKb.GetValueOrDefault() > thresholds.RedoQueueWarningKb && !db.IsPrimaryReplica)
            {
                Add(issues, AgIssueSeverity.Warning, "Queue", subject,
                    $"Redo queue is {FormatKb(db.RedoQueueKb)}, draining at {FormatKb(db.RedoRateKbSec)}/s ({Describe(db.EstimatedRecoverySeconds)} to catch up).",
                    "The data is safe — it has been hardened here — but a failover would have to finish this redo before the database comes online, so this is recovery time, not data loss.");
            }
        }
    }

    /// <summary>
    /// Whether each group could actually fail over automatically at this instant.
    ///
    /// This is the check that cannot be made from any single grid: it needs the replica's failover mode and
    /// availability mode, plus every one of its databases' synchronization state and failover readiness, at once.
    /// </summary>
    private static void CheckFailoverReadiness(AgSnapshot snapshot, List<AgIssueRow> issues)
    {
        foreach (var group in snapshot.Groups)
        {
            var autoSecondaries = snapshot.Replicas
                .Where(r => string.Equals(r.AgName, group.Name, StringComparison.OrdinalIgnoreCase)
                         && AgReplicaRow.IsState(r.FailoverMode, "AUTOMATIC")
                         && AgReplicaRow.IsState(r.AvailabilityMode, "SYNCHRONOUS_COMMIT")
                         && !AgReplicaRow.IsState(r.Role, "PRIMARY"))
                .ToList();

            if (autoSecondaries.Count == 0) continue;

            var ready = new List<string>();
            var notReady = new List<string>();

            foreach (var replica in autoSecondaries)
            {
                var databases = snapshot.Databases
                    .Where(d => string.Equals(d.AgName, group.Name, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(d.ReplicaServerName, replica.ReplicaServerName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // No database rows for a replica means this vantage point cannot see them (we are on a
                // secondary), not that the replica is unready. Stay silent rather than guess.
                if (databases.Count == 0) continue;

                bool allReady = databases.All(d => AgReplicaRow.IsState(d.SynchronizationState, "SYNCHRONIZED") && d.IsFailoverReady != false);
                (allReady ? ready : notReady).Add(replica.ReplicaServerName);
            }

            if (notReady.Count == 0) continue;

            if (ready.Count == 0)
            {
                Add(issues, AgIssueSeverity.Critical, "Failover", group.Name,
                    $"No automatic-failover partner is ready: {string.Join(", ", notReady)}.",
                    "The group is configured for automatic failover but could not complete one right now — a primary failure would need a manual, potentially data-losing failover. Get the synchronous secondaries back to SYNCHRONIZED.");
            }
            else
            {
                Add(issues, AgIssueSeverity.Warning, "Failover", group.Name,
                    $"Automatic failover is possible ({string.Join(", ", ready)} ready) but {string.Join(", ", notReady)} is not.",
                    "One partner is still ready, so automatic failover would work. The unready replica is not currently protecting anything.");
            }
        }
    }

    /// <summary>
    /// required_synchronized_secondaries_to_commit: when fewer synchronized secondaries are available than the
    /// group requires, the primary stops accepting commits. This is a total outage that shows up nowhere as a
    /// health state — every replica can read HEALTHY while the application is timing out on every write.
    /// </summary>
    private static void CheckCommitQuorum(AgSnapshot snapshot, AgCapabilities caps, List<AgIssueRow> issues)
    {
        if (caps == null || !caps.HasRequiredSyncSecondaries) return;

        foreach (var group in snapshot.Groups)
        {
            int required = group.RequiredSynchronizedSecondaries.GetValueOrDefault();
            if (required <= 0) continue;

            var syncSecondaries = snapshot.Replicas
                .Where(r => string.Equals(r.AgName, group.Name, StringComparison.OrdinalIgnoreCase)
                         && AgReplicaRow.IsState(r.AvailabilityMode, "SYNCHRONOUS_COMMIT")
                         && !AgReplicaRow.IsState(r.Role, "PRIMARY"))
                .ToList();

            // Count a secondary as available when it is connected and every database row we can see for it is
            // synchronized. Where we cannot see its databases (from another secondary), fall back to its own
            // synchronization health rather than assuming the worst.
            int available = 0;
            foreach (var replica in syncSecondaries)
            {
                if (AgReplicaRow.IsBadState(replica.ConnectedState, "CONNECTED")) continue;

                var databases = snapshot.Databases
                    .Where(d => string.Equals(d.AgName, group.Name, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(d.ReplicaServerName, replica.ReplicaServerName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                bool synchronized = databases.Count > 0
                    ? databases.All(d => AgReplicaRow.IsState(d.SynchronizationState, "SYNCHRONIZED"))
                    : AgReplicaRow.IsState(replica.SynchronizationHealth, "HEALTHY");

                if (synchronized) available++;
            }

            if (available < required)
            {
                Add(issues, AgIssueSeverity.Critical, "Commit quorum", group.Name,
                    $"The group requires {required} synchronized secondary replica(s) to commit and {available} is available.",
                    "The primary refuses commits while this is true — writes fail even though the replicas look healthy. Either restore the synchronized secondaries or lower REQUIRED_SYNCHRONIZED_SECONDARIES_TO_COMMIT.");
            }
            else if (available == required)
            {
                Add(issues, AgIssueSeverity.Information, "Commit quorum", group.Name,
                    $"Exactly {required} synchronized secondary replica(s) available, the minimum required to commit.",
                    "Working, with no margin: losing one more synchronized secondary stops writes on the primary.");
            }
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Listeners and routing
    // -------------------------------------------------------------------------------------------------

    private static void CheckListeners(AgSnapshot snapshot, List<AgIssueRow> issues)
    {
        foreach (var listener in snapshot.Listeners)
        {
            string subject = $"{listener.AgName} / {listener.DnsName}";

            if (listener.IsUnhealthy)
            {
                Add(issues, AgIssueSeverity.Critical, "Listener", subject,
                    $"Listener IP {listener.IpAddress ?? "(unknown)"} is {listener.StateDescription}.",
                    "Clients connecting through the listener fail even though replication is fine. Bring the cluster IP resource online; in a multi-subnet listener only the IP for the current primary's subnet should be online, so check that this is not the expected offline one.");
            }
            else if (listener.IsConformant == false)
            {
                Add(issues, AgIssueSeverity.Warning, "Listener", subject,
                    "The listener is not conformant with the cluster's IP configuration.",
                    $"SQL Server and the cluster disagree about this listener's IPs (cluster reports: {listener.IpConfigurationFromCluster ?? "not reported"}). Recreate the listener from SQL Server rather than editing the cluster resource directly.");
            }
        }

        foreach (var group in snapshot.Groups)
        {
            bool hasListener = snapshot.Listeners.Any(l => string.Equals(l.AgName, group.Name, StringComparison.OrdinalIgnoreCase));
            if (hasListener) continue;

            // Information, not a warning: a listener is normal but not required, and plenty of groups are
            // reached by node name on purpose. Nothing is broken right now, so it does not earn amber.
            Add(issues, AgIssueSeverity.Information, "Listener", group.Name,
                "The group has no listener.",
                "Clients have to connect to a node name directly, so a failover breaks them until they are reconfigured. Read-intent routing also needs a listener to work.");
        }
    }

    private static void CheckRouting(AgSnapshot snapshot, List<AgIssueRow> issues)
    {
        foreach (var route in snapshot.Routing)
        {
            string subject = $"{route.AgName} / {route.SourceReplica} → {route.TargetReplica}";

            if (string.IsNullOrWhiteSpace(route.ReadOnlyRoutingUrl))
            {
                Add(issues, AgIssueSeverity.Warning, "Routing", subject,
                    $"Routing target {route.TargetReplica} has no read_only_routing_url.",
                    "The routing list looks configured but this entry can never receive a routed connection. Set READ_ONLY_ROUTING_URL on that replica.");
            }
            else if (AgReplicaRow.IsState(route.TargetReadableSecondary, "NO"))
            {
                Add(issues, AgIssueSeverity.Warning, "Routing", subject,
                    $"Routing target {route.TargetReplica} does not allow read-only connections in the secondary role.",
                    "Read-intent connections routed here are refused. Set ALLOW_CONNECTIONS = READ_ONLY (or ALL) on that replica.");
            }
        }

        // A readable secondary that no routing list points at: read-intent connections land on the primary
        // instead, silently, and the secondary's whole purpose goes unused.
        foreach (var replica in snapshot.Replicas)
        {
            if (AgReplicaRow.IsState(replica.Role, "PRIMARY")) continue;
            if (!AgReplicaRow.IsState(replica.ReadableSecondary, "ALL", "READ_ONLY")) continue;

            bool isRoutingTarget = snapshot.Routing.Any(r => string.Equals(r.AgName, replica.AgName, StringComparison.OrdinalIgnoreCase)
                                                          && string.Equals(r.TargetReplica, replica.ReplicaServerName, StringComparison.OrdinalIgnoreCase));
            if (isRoutingTarget) continue;

            Add(issues, AgIssueSeverity.Information, "Routing", $"{replica.AgName} / {replica.ReplicaServerName}",
                "Replica accepts read-only connections but is not in any read-only routing list.",
                "ApplicationIntent=ReadOnly connections through the listener will not be sent here — they stay on the primary. Add it to the primary's READ_ONLY_ROUTING_LIST if that was the intent.");
        }
    }

    // -------------------------------------------------------------------------------------------------
    // Seeding, throughput, configuration
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Seeding findings, from two DMVs that both keep finished work around.
    ///
    /// <c>sys.dm_hadr_automatic_seeding</c> is a <em>history</em> table, not a current-state view: one row per
    /// attempt, kept for the life of the group (it is memory-resident, so only a restart clears it). A seed that
    /// failed once and succeeded on the retry leaves both rows behind, so firing on every failed row reports a
    /// problem that was fixed months ago and goes on reporting it until the instance bounces — which on this tab
    /// is indistinguishable from a database that is unprotected right now, and is exactly how a diagnostics tab
    /// gets ignored.
    ///
    /// So only the newest attempt per database is judged, and even that is demoted to Information once the
    /// database is demonstrably seeded: the attempt itself says COMPLETED, or every secondary reports the database
    /// joined. A warning here has to mean "this database is not on that replica now".
    /// </summary>
    private static void CheckSeeding(AgSnapshot snapshot, List<AgIssueRow> issues)
    {
        foreach (var attempt in LatestSeedAttemptPerDatabase(snapshot.AutoSeeding))
        {
            if (!attempt.IsFailed) continue;

            string subject = $"{attempt.AgName} / {attempt.DatabaseName}";
            string failure = attempt.FailureState ?? "unknown";
            if (attempt.ErrorCode.GetValueOrDefault() != 0) failure += $" (error {attempt.ErrorCode})";
            string started = attempt.StartTime != null ? $", started {attempt.StartTime:yyyy-MM-dd HH:mm}" : "";

            bool? joined = IsJoinedOnEverySecondary(snapshot, attempt);

            if (attempt.IsCompleted || joined == true)
            {
                Add(issues, AgIssueSeverity.Information, "Seeding", subject,
                    $"A seeding attempt failed ({failure}) after {attempt.NumberOfAttempts} attempt(s){started}, but the database is seeded and joined now.",
                    "Nothing to do — this is history, not a current problem. sys.dm_hadr_automatic_seeding keeps every attempt until the instance restarts, so a failure a later attempt fixed stays listed on the Seeding tab. The database being joined on every secondary is the proof the seed landed; the Databases tab shows it.");
                continue;
            }

            Add(issues, AgIssueSeverity.Warning, "Seeding", subject,
                $"The most recent seeding attempt failed: {failure} after {attempt.NumberOfAttempts} attempt(s){started}."
                    + (joined == null ? " Whether the database is joined on the secondaries could not be seen from this connection." : ""),
                "The database is not seeded to that replica. Check, in order: it already exists on the target; the primary's data and log directories have no counterpart there; free disk space; the replica is not SEEDING_MODE = AUTOMATIC; the group has not been granted CREATE ANY DATABASE on the secondary (ALTER AVAILABILITY GROUP … GRANT CREATE ANY DATABASE, run there, not on the primary); or the database uses something seeding cannot carry — TDE without the certificate restored, or memory-optimized or FILESTREAM filegroups.");
        }

        // dm_hadr_physical_seeding_stats keeps recent transfers too, and end_time_utc is what separates one still
        // running from one that has finished. A failure message on a finished transfer is the same stale reading as
        // above — the transfer reported a problem, retried, and completed.
        foreach (var seed in snapshot.Seeding.Where(s => !string.IsNullOrWhiteSpace(s.FailureMessage)))
        {
            string subject = $"{seed.LocalDatabaseName} → {seed.RemoteMachineName}";

            if (seed.EndTimeUtc != null)
            {
                Add(issues, AgIssueSeverity.Information, "Seeding", subject,
                    $"A seeding transfer that has since finished reported: {seed.FailureMessage.Trim()}",
                    "The transfer is no longer running, so this is the record of a completed attempt rather than a live failure. Confirm the database is joined on that replica from the Databases tab; if it is, there is nothing to do.");
                continue;
            }

            Add(issues, AgIssueSeverity.Warning, "Seeding", subject,
                "In-flight seeding reported: " + seed.FailureMessage.Trim(),
                "The transfer is still running and has reported a failure. Watch the percentage on the Seeding tab; if it stops advancing the seed has stalled.");
        }
    }

    /// <summary>
    /// The newest attempt per (group, database). Rows with no start time sort last — a null cannot be compared, and
    /// letting one win over a stamped row would re-raise a failure the next attempt already fixed.
    /// </summary>
    private static IEnumerable<AgAutoSeedRow> LatestSeedAttemptPerDatabase(IEnumerable<AgAutoSeedRow> attempts) =>
        attempts.GroupBy(a => $"{a.AgName}|{a.DatabaseName}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(a => a.StartTime ?? DateTime.MinValue).First());

    /// <summary>
    /// Whether the database is joined on every secondary in its group — the evidence that a failed seeding attempt
    /// has since been made good.
    ///
    /// Null when it cannot be told, and an unknown must never silence the finding: sys.dm_hadr_automatic_seeding
    /// names no replica, so with no database rows for the group, or on a release without is_database_joined (the
    /// column is then substituted as NULL), there is nothing to check the attempt against. One secondary explicitly
    /// *not* joined answers false whatever the others say — that replica is the one the attempt could have been for.
    /// </summary>
    private static bool? IsJoinedOnEverySecondary(AgSnapshot snapshot, AgAutoSeedRow attempt)
    {
        var rows = snapshot.Databases.Where(d => !d.IsPrimaryReplica
                                              && string.Equals(d.AgName, attempt.AgName, StringComparison.OrdinalIgnoreCase)
                                              && string.Equals(d.DatabaseName, attempt.DatabaseName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (rows.Count == 0) return null;
        if (rows.Any(d => d.IsDatabaseJoined == false)) return false;
        if (rows.Any(d => d.IsDatabaseJoined == null)) return null;
        return true;
    }

    private static void CheckThroughput(AgSnapshot snapshot, AgThresholds thresholds, List<AgIssueRow> issues)
    {
        foreach (var row in snapshot.Throughput)
        {
            double? delay = row.AvgCommitDelayMs;
            if (delay == null || delay <= thresholds.CommitDelayWarningMs) continue;

            Add(issues, AgIssueSeverity.Warning, "Commit latency", row.DatabaseName,
                $"Synchronous commit is adding about {delay.Value:N1} ms per transaction ({row.MirroredWriteTransactionsPerSec:N0} mirrored commits/s).",
                $"Above the {thresholds.CommitDelayWarningMs:N0} ms threshold. Every write on the primary waits for the synchronous secondary to harden its log, so this is network round-trip plus that replica's log-write latency — check both before blaming the workload.");
        }

        foreach (var row in snapshot.Transport.Where(t => t.IsWarning))
        {
            var parts = new List<string>();
            if (row.FlowControlTimeMsPerSec.GetValueOrDefault() > 0) parts.Add($"{row.FlowControlTimeMsPerSec:N0} ms/s in flow control");
            if (row.ResentMessagesPerSec.GetValueOrDefault() > 0) parts.Add($"{row.ResentMessagesPerSec:N1} resent messages/s");

            Add(issues, AgIssueSeverity.Warning, "Transport", row.Instance,
                "Transport is throttling: " + string.Join(", ", parts) + ".",
                "The link, not the workload, is setting the pace — log sends are being held back or retried. Look at network bandwidth and packet loss between the replicas before tuning anything on the server.");
        }
    }

    private static void CheckConfiguration(AgSnapshot snapshot, AgCapabilities caps, List<AgIssueRow> issues)
    {
        if (caps != null && !caps.IsHealthSessionRunning)
        {
            Add(issues, AgIssueSeverity.Information, "Diagnostics", caps.ServerName ?? "(server)",
                "The AlwaysOn_health extended-event session is not running.",
                "The Errors tab has nothing to read, and the history that explains a past failover is not being recorded. Start it in Object Explorer under Management > Extended Events, and set it to start automatically.");
        }

        // Backup preference that cannot be honoured: SECONDARY_ONLY with no eligible secondary means the
        // scripted backup job silently backs nothing up.
        foreach (var group in snapshot.Groups)
        {
            if (!AgReplicaRow.IsState(group.AutomatedBackupPreference, "SECONDARY_ONLY")) continue;

            var secondaries = snapshot.Replicas
                .Where(r => string.Equals(r.AgName, group.Name, StringComparison.OrdinalIgnoreCase) && !AgReplicaRow.IsState(r.Role, "PRIMARY"))
                .ToList();

            if (secondaries.Count == 0 || secondaries.All(r => r.BackupPriority.GetValueOrDefault() == 0))
            {
                Add(issues, AgIssueSeverity.Warning, "Backups", group.Name,
                    "Automated backup preference is SECONDARY_ONLY but no secondary is an eligible backup target.",
                    "sys.fn_hadr_backup_is_preferred_replica returns 0 everywhere, so a backup job that honours the preference skips every replica and the databases go unbacked-up. Give a secondary a non-zero backup priority, or change the preference.");
            }
        }
    }

    /// <summary>
    /// When nothing fired, say so explicitly and list what was actually looked at.
    ///
    /// An empty grid is ambiguous — it reads equally as "healthy" and "this tab is broken" — and "no issues"
    /// without a scope is a claim the dashboard has not earned. This row is also where the vantage-point caveat
    /// belongs: from a secondary, several columns are NULL and the checks that depend on them did not run.
    /// </summary>
    private static void AddAllClear(AgSnapshot snapshot, List<AgIssueRow> issues)
    {
        if (issues.Any(i => i.Severity != AgIssueSeverity.Information)) return;

        string scope = $"{snapshot.Groups.Count} group(s), {snapshot.Replicas.Count} replica(s), {snapshot.Databases.Count} database replica(s)";
        string detail = $"No problems found across {scope}.";

        bool onSecondary = !string.IsNullOrEmpty(snapshot.LocalRole) && !AgReplicaRow.IsState(snapshot.LocalRole, "PRIMARY");
        string recommendation = onSecondary
            ? "Checked: quorum, replica connection and health, database synchronization, data loss and queue estimates, failover readiness, commit quorum, listeners, routing, seeding and commit latency. This connection is to a secondary, so the states only the primary reports were not visible — connect to the primary for the complete picture."
            : "Checked: quorum, replica connection and health, database synchronization, data loss and queue estimates, failover readiness, commit quorum, listeners, routing, seeding and commit latency.";

        issues.Insert(0, new AgIssueRow
        {
            Severity = AgIssueSeverity.Information,
            Area = "Summary",
            Subject = snapshot.ServerName ?? "(server)",
            Detail = detail,
            Recommendation = recommendation
        });
    }

    // -------------------------------------------------------------------------------------------------
    // Formatting — the findings are read as prose, so numbers get units here rather than in the XAML
    // -------------------------------------------------------------------------------------------------

    private static string Describe(double? seconds)
    {
        if (seconds == null || double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value) || seconds < 0) return "an unknown time";
        if (seconds < 1) return "under a second";
        if (seconds < 60) return $"{seconds.Value:N0}s";
        if (seconds < 3600) return TimeSpan.FromSeconds(seconds.Value).ToString(@"m\m\ ss\s");
        if (seconds < 86400) return TimeSpan.FromSeconds(seconds.Value).ToString(@"h\h\ mm\m");
        return TimeSpan.FromSeconds(seconds.Value).ToString(@"d\d\ h\h");
    }

    private static string FormatKb(long? kb)
    {
        if (kb == null) return "unknown";
        double value = kb.Value;
        if (value >= 1024d * 1024d) return (value / (1024d * 1024d)).ToString("N2") + " GB";
        if (value >= 1024d) return (value / 1024d).ToString("N1") + " MB";
        return value.ToString("N0") + " KB";
    }
}
