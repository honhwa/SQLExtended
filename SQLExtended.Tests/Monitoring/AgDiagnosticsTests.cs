using System;
using System.Globalization;
using System.Linq;
using SQLExtended.Monitoring.AlwaysOn;
using Xunit;

namespace SQLExtended.Tests.Monitoring;

/// <summary>
/// Tests for the Always On state predicates and diagnostic rules.
///
/// These are worth having because the rules encode judgements a live server cannot easily be made to
/// demonstrate on demand — you cannot lose cluster quorum in CI, and the conditions that matter most
/// (an automatic-failover pair that is not ready, a commit quorum one replica from stopping writes) are
/// exactly the ones nobody wants to reproduce for real.
/// </summary>
public class AgDiagnosticsTests
{
    // -----------------------------------------------------------------------------------------------
    // The NULL rule: several *_desc columns are only populated for replicas local to the queried
    // instance, so from a secondary the primary's states come back NULL. Treating that as "bad" would
    // flag a healthy primary on every poll, which is the single easiest way to make this window useless.
    // -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsBadState_TreatsMissingStateAsNotAFinding(string state)
    {
        Assert.False(AgReplicaRow.IsBadState(state, "CONNECTED"));
        Assert.False(AgReplicaRow.IsState(state, "CONNECTED"));
    }

    [Fact]
    public void IsBadState_FlagsOnlyValuesOutsideTheAcceptableSet()
    {
        Assert.False(AgReplicaRow.IsBadState("CONNECTED", "CONNECTED"));
        Assert.False(AgReplicaRow.IsBadState("connected", "CONNECTED"));   // case-insensitive
        Assert.True(AgReplicaRow.IsBadState("DISCONNECTED", "CONNECTED"));
    }

    [Fact]
    public void Replica_HealthAndWarningAreMutuallyExclusive()
    {
        var partiallyHealthy = new AgReplicaRow { SynchronizationHealth = "PARTIALLY_HEALTHY", ConnectedState = "CONNECTED", OperationalState = "ONLINE" };
        Assert.False(partiallyHealthy.IsUnhealthy);
        Assert.True(partiallyHealthy.IsWarning);

        var disconnected = new AgReplicaRow { SynchronizationHealth = "HEALTHY", ConnectedState = "DISCONNECTED" };
        Assert.True(disconnected.IsUnhealthy);
        Assert.False(disconnected.IsWarning);
    }

    // -----------------------------------------------------------------------------------------------
    // SYNCHRONIZING only means something on a synchronous replica. On an async one it is the normal
    // steady state, and tinting every async secondary would drain the colour of meaning.
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Database_SynchronizingIsNormalOnAnAsynchronousReplica()
    {
        var row = new AgDatabaseRow
        {
            AvailabilityMode = "ASYNCHRONOUS_COMMIT",
            SynchronizationState = "SYNCHRONIZING",
            SynchronizationHealth = "HEALTHY",
            DatabaseState = "ONLINE",
            IsFailoverReady = false
        };

        Assert.False(row.IsUnhealthy);
        Assert.False(row.IsWarning);
    }

    [Fact]
    public void Database_SynchronizingIsAWarningOnASynchronousReplica()
    {
        var row = new AgDatabaseRow
        {
            AvailabilityMode = "SYNCHRONOUS_COMMIT",
            SynchronizationState = "SYNCHRONIZING",
            SynchronizationHealth = "HEALTHY",
            DatabaseState = "ONLINE"
        };

        Assert.False(row.IsUnhealthy);
        Assert.True(row.IsWarning);
    }

    [Fact]
    public void Database_EstimatesAreNullRatherThanInfiniteWhenNothingIsMoving()
    {
        var row = new AgDatabaseRow { LogSendQueueKb = 5000, LogSendRateKbSec = 0, RedoQueueKb = 5000, RedoRateKbSec = 0 };

        // A zero rate would make these a division by zero; null is the honest answer, and the grid shows a dash.
        Assert.Null(row.EstimatedDataLossSeconds);
        Assert.Null(row.EstimatedRecoverySeconds);

        row.LogSendRateKbSec = 500;
        Assert.Equal(10d, row.EstimatedDataLossSeconds);
    }

    // -----------------------------------------------------------------------------------------------
    // Listener state has no *_desc companion column, so the mapping lives in the row. 0 is offline and 1 is
    // online — the opposite way round to most of these DMVs, and the reason this table is spelled out here.
    // -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "OFFLINE", true, false)]
    [InlineData(1, "ONLINE", false, false)]
    [InlineData(2, "ONLINE_PENDING", false, true)]
    [InlineData(3, "FAILED", true, false)]
    public void Listener_StateMapsToADescriptionAndAHealthVerdict(int state, string expected, bool unhealthy, bool warning)
    {
        var row = new AgListenerRow { State = state };

        Assert.Equal(expected, row.StateDescription);
        Assert.Equal(unhealthy, row.IsUnhealthy);
        Assert.Equal(warning, row.IsWarning);
    }

    [Fact]
    public void Listener_UnreportedStateIsNotAFinding()
    {
        var row = new AgListenerRow { State = null };

        Assert.Null(row.StateDescription);
        Assert.False(row.IsUnhealthy);
        Assert.False(row.IsWarning);
    }

    // -----------------------------------------------------------------------------------------------
    // Commit delay: the number that says what synchronous commit costs the primary.
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Throughput_CommitDelayIsWaitPerSecondOverCommitsPerSecond()
    {
        var row = new AgThroughputRow { TransactionDelayMsPerSec = 500, MirroredWriteTransactionsPerSec = 50, CommitDelayWarningMs = 20 };

        Assert.Equal(10d, row.AvgCommitDelayMs);
        Assert.False(row.IsWarning);

        row.MirroredWriteTransactionsPerSec = 10;   // 500 ms of wait per second / 10 commits per second = 50 ms per commit
        Assert.Equal(50d, row.AvgCommitDelayMs);
        Assert.True(row.IsWarning);
    }

    [Fact]
    public void Throughput_NoCommitsMeansNoLatencyRatherThanZero()
    {
        var row = new AgThroughputRow { TransactionDelayMsPerSec = 500, MirroredWriteTransactionsPerSec = 0 };

        Assert.Null(row.AvgCommitDelayMs);
        Assert.False(row.IsWarning);
    }

    /// <summary>
    /// The bug this pins: Transaction Delay is cumulative in sys.dm_os_performance_counters, so using it raw
    /// divides a whole-uptime total by a per-second rate. On a healthy AG that read as 63,450 ms per commit —
    /// the raw counter (317,250 ms of wait since startup) over 5 commits/s. Both operands must be rates.
    /// </summary>
    [Fact]
    public void Throughput_CommitWaitIsDifferencedWhenTheCounterIsCumulative()
    {
        const int bulkCount = 272696576;   // PERF_COUNTER_BULK_COUNT — cumulative
        const int rawCount = 65792;        // PERF_COUNTER_LARGE_RAWCOUNT — already a level

        // Cumulative: the differenced rate is used and the raw total is ignored, however large it has grown.
        Assert.Equal(250d, AgQueryService.CommitWaitMsPerSecond(317_250, bulkCount, 250d));

        // No baseline yet on the first sample of a server, so there is no rate — null, not the raw total.
        Assert.Null(AgQueryService.CommitWaitMsPerSecond(317_250, bulkCount, null));

        // A release that made it a genuine level would be used as read rather than differenced.
        Assert.Equal(42d, AgQueryService.CommitWaitMsPerSecond(42, rawCount, 999d));
    }

    [Fact]
    public void Throughput_ACumulativeCommitWaitCannotProduceAnUptimeScaledLatency()
    {
        // 317,250 ms of accumulated wait, 5 commits/s, and 250 ms/s of that wait arriving in the last interval.
        double? perSecond = AgQueryService.CommitWaitMsPerSecond(317_250, 272696576, 250d);
        var row = new AgThroughputRow { TransactionDelayMsPerSec = perSecond, MirroredWriteTransactionsPerSec = 5 };

        Assert.Equal(50d, row.AvgCommitDelayMs);          // 250 / 5 — a real, if poor, commit latency
        Assert.NotEqual(63_450d, row.AvgCommitDelayMs);   // what the raw total produced
    }

    // -----------------------------------------------------------------------------------------------
    // Counter deltas
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void CounterTracker_ReportsNothingUntilItHasABaseline()
    {
        var tracker = new AgCounterTracker();
        Assert.True(tracker.NeedsBaseline);
        Assert.Null(tracker.IntervalSecondsFrom(1000));

        tracker.Store(new System.Collections.Generic.Dictionary<string, long> { ["a"] = 100 }, 1000);

        Assert.False(tracker.NeedsBaseline);
        Assert.Equal(2d, tracker.IntervalSecondsFrom(3000));
        Assert.Equal(50d, tracker.RateFor("a", 200, 2d));
    }

    [Fact]
    public void CounterTracker_TreatsACounterResetAsUnknownRatherThanNegative()
    {
        var tracker = new AgCounterTracker();
        tracker.Store(new System.Collections.Generic.Dictionary<string, long> { ["a"] = 100 }, 1000);

        // Lower than the baseline means the instance restarted and every total reset with it.
        Assert.Null(tracker.RateFor("a", 40, 2d));

        // ms_ticks going backwards means the same thing.
        Assert.Null(tracker.IntervalSecondsFrom(500));
    }

    [Fact]
    public void CounterTracker_OnlyCumulativeCounterTypesAreDifferenced()
    {
        Assert.True(AgCounterTracker.IsCumulative(272696576));   // PERF_COUNTER_BULK_COUNT
        Assert.True(AgCounterTracker.IsCumulative(272696320));   // PERF_COUNTER_COUNTER
        Assert.False(AgCounterTracker.IsCumulative(65792));      // PERF_COUNTER_LARGE_RAWCOUNT — already a level
    }

    // -----------------------------------------------------------------------------------------------
    // The rules
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Evaluate_QuorumLossIsCritical()
    {
        var snapshot = HealthyGroup();
        snapshot.Cluster = new AgClusterInfo { ClusterName = "CL01", QuorumType = "NODE_MAJORITY", QuorumState = "UNKNOWN_QUORUM_STATE" };
        snapshot.ClusterMembers.Add(new AgClusterMemberRow { MemberName = "N2", MemberState = "DOWN", MemberType = "CLUSTER_NODE", QuorumVotes = 1 });

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        Assert.Contains(snapshot.Issues, i => i.Severity == AgIssueSeverity.Critical && i.Area == "Cluster" && i.Detail.Contains("Quorum state"));
        Assert.Contains(snapshot.Issues, i => i.Severity == AgIssueSeverity.Critical && i.Subject == "N2");
    }

    [Fact]
    public void Evaluate_ADownClusterMemberWithNoVoteIsOnlyAWarning()
    {
        var snapshot = HealthyGroup();
        snapshot.Cluster = new AgClusterInfo { QuorumState = "NORMAL_QUORUM" };
        snapshot.ClusterMembers.Add(new AgClusterMemberRow { MemberName = "FS", MemberState = "DOWN", MemberType = "FILE_SHARE_WITNESS", QuorumVotes = 0 });

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Subject == "FS");
        Assert.Equal(AgIssueSeverity.Warning, finding.Severity);
    }

    /// <summary>
    /// The finding no single grid reveals: both replicas read HEALTHY and the secondary's database is merely
    /// SYNCHRONIZING, so an automatic failover could not complete right now.
    /// </summary>
    [Fact]
    public void Evaluate_AnAutomaticFailoverPartnerThatIsNotReadyIsCritical()
    {
        var snapshot = HealthyGroup();
        snapshot.Databases.Single(d => !d.IsPrimaryReplica).SynchronizationState = "SYNCHRONIZING";

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        Assert.Contains(snapshot.Issues, i => i.Severity == AgIssueSeverity.Critical && i.Area == "Failover");
    }

    [Fact]
    public void Evaluate_AHealthyGroupProducesAnExplicitAllClearAndNoProblems()
    {
        var snapshot = HealthyGroup();

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        Assert.DoesNotContain(snapshot.Issues, i => i.Severity != AgIssueSeverity.Information);

        // An empty grid reads equally as "healthy" and "this tab is broken", so the all-clear is stated.
        var summary = snapshot.Issues.First();
        Assert.Equal("Summary", summary.Area);
        Assert.Contains("No problems found", summary.Detail);
    }

    [Fact]
    public void Evaluate_CommitQuorumBelowTheRequirementIsCritical()
    {
        var snapshot = HealthyGroup();
        snapshot.Groups[0].RequiredSynchronizedSecondaries = 2;   // only one sync secondary exists

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Commit quorum");
        Assert.Equal(AgIssueSeverity.Critical, finding.Severity);
        Assert.Contains("refuses commits", finding.Recommendation);
    }

    [Fact]
    public void Evaluate_CommitQuorumExactlyAtTheRequirementIsReportedAsHavingNoMargin()
    {
        var snapshot = HealthyGroup();
        snapshot.Groups[0].RequiredSynchronizedSecondaries = 1;

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Commit quorum");
        Assert.Equal(AgIssueSeverity.Information, finding.Severity);
    }

    [Fact]
    public void Evaluate_CommitQuorumIsSkippedWhenTheColumnDoesNotExist()
    {
        var snapshot = HealthyGroup();
        snapshot.Groups[0].RequiredSynchronizedSecondaries = 2;

        var caps = Caps();
        caps.HasRequiredSyncSecondaries = false;

        AgDiagnostics.Evaluate(snapshot, caps, new AgThresholds());

        // The value cannot be trusted on a release that does not have the column, so the rule stays quiet.
        Assert.DoesNotContain(snapshot.Issues, i => i.Area == "Commit quorum");
    }

    [Fact]
    public void Evaluate_DataLossPastTheThresholdIsReportedAgainstTheSecondaryOnly()
    {
        var snapshot = HealthyGroup();
        var secondary = snapshot.Databases.Single(d => !d.IsPrimaryReplica);
        secondary.LogSendQueueKb = 1_000_000;
        secondary.LogSendRateKbSec = 1_000;          // 1000 seconds of data loss

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds { RpoWarningSeconds = 60, RpoCriticalSeconds = 300 });

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Data loss");
        Assert.Equal(AgIssueSeverity.Critical, finding.Severity);
        Assert.Contains("SECONDARY01", finding.Subject);
    }

    [Fact]
    public void Evaluate_AStalledRedoQueueNamesTheReaderBlockingItAsTheLikelyCause()
    {
        var snapshot = HealthyGroup();
        var secondary = snapshot.Databases.Single(d => !d.IsPrimaryReplica);
        secondary.RedoQueueKb = 500_000;
        secondary.RedoRateKbSec = 0;

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Queue");
        Assert.Equal(AgIssueSeverity.Warning, finding.Severity);
        Assert.Contains("redo thread", finding.Recommendation);
    }

    [Fact]
    public void Evaluate_AGroupWithNoListenerIsInformationalOnly()
    {
        var snapshot = HealthyGroup();
        snapshot.Listeners.Clear();

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        Assert.Contains(snapshot.Issues, i => i.Area == "Listener" && i.Severity == AgIssueSeverity.Information && i.Detail.Contains("no listener"));
    }

    [Fact]
    public void Evaluate_AnOfflineListenerIpIsCriticalEvenWithHealthyReplicas()
    {
        var snapshot = HealthyGroup();
        snapshot.Listeners[0].State = 0;   // OFFLINE

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Listener");
        Assert.Equal(AgIssueSeverity.Critical, finding.Severity);
        Assert.Contains("replication is fine", finding.Recommendation);
    }

    [Fact]
    public void Evaluate_ARoutingTargetWithNoUrlIsAWarning()
    {
        var snapshot = HealthyGroup();
        snapshot.Routing.Add(new AgRoutingRow
        {
            AgName = "AG01",
            SourceReplica = "PRIMARY01",
            TargetReplica = "SECONDARY01",
            RoutingPriority = 1,
            TargetReadableSecondary = "ALL",
            ReadOnlyRoutingUrl = null
        });

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        Assert.Contains(snapshot.Issues, i => i.Area == "Routing" && i.Severity == AgIssueSeverity.Warning && i.Detail.Contains("read_only_routing_url"));
    }

    [Fact]
    public void Evaluate_SuspendedDataMovementIsCriticalAndSaysHowToResume()
    {
        var snapshot = HealthyGroup();
        var secondary = snapshot.Databases.Single(d => !d.IsPrimaryReplica);
        secondary.IsSuspended = true;
        secondary.SuspendReason = "SUSPEND_FROM_USER";

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Database" && i.Severity == AgIssueSeverity.Critical);
        Assert.Contains("SET HADR RESUME", finding.Recommendation);
    }

    [Fact]
    public void Evaluate_AStoppedHealthSessionIsReportedAsInformational()
    {
        var snapshot = HealthyGroup();
        var caps = Caps();
        caps.IsHealthSessionRunning = false;

        AgDiagnostics.Evaluate(snapshot, caps, new AgThresholds());

        Assert.Contains(snapshot.Issues, i => i.Area == "Diagnostics" && i.Severity == AgIssueSeverity.Information);
    }

    [Fact]
    public void Evaluate_FindingsAreOrderedWorstFirst()
    {
        var snapshot = HealthyGroup();
        snapshot.Databases.Single(d => !d.IsPrimaryReplica).IsSuspended = true;   // critical
        snapshot.Replicas.Single(r => !r.IsLocal).SynchronizationHealth = "PARTIALLY_HEALTHY";   // warning

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        var severities = snapshot.Issues.Select(i => i.Severity).ToList();
        Assert.Equal(severities.OrderBy(s => s), severities);
    }

    // -----------------------------------------------------------------------------------------------
    // Seeding. sys.dm_hadr_automatic_seeding is a history table: a failure that a later attempt fixed stays
    // in it until the instance restarts. Reporting those as current problems is what these pin against.
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Evaluate_ASeedingFailureASuccessfulRetryFollowedIsHistoryNotAWarning()
    {
        var snapshot = HealthyGroup();
        snapshot.AutoSeeding.Add(Seed("2026-01-04 02:00", "FAILED", failureState: "SEEDING_ERROR", errorCode: 41158, attempts: 1));
        snapshot.AutoSeeding.Add(Seed("2026-01-04 02:30", "COMPLETED", performedSeeding: true));

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        // Only the newest attempt is judged and it succeeded, so the older failed row is not reported at all —
        // not even as Information. Silence is right here: nothing about this database needs reading.
        Assert.DoesNotContain(snapshot.Issues, i => i.Area == "Seeding");
    }

    /// <summary>
    /// The reported case: the seed did land, but the failed attempt's row carries a stale failure_state and the
    /// retry reused it rather than adding one. The database being joined on every secondary is the evidence.
    /// </summary>
    [Fact]
    public void Evaluate_ASeedingFailureIsHistoryOnceTheDatabaseIsJoinedEverywhere()
    {
        var snapshot = HealthyGroup();   // both database rows have IsDatabaseJoined = true
        snapshot.AutoSeeding.Add(Seed("2026-01-04 02:00", "SEEDING", failureState: "SEEDING_ERROR", errorCode: 41158, attempts: 3));

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Seeding");
        Assert.Equal(AgIssueSeverity.Information, finding.Severity);
        Assert.Contains("seeded and joined now", finding.Detail);
    }

    [Fact]
    public void Evaluate_ASeedingFailureIsAWarningWhileTheDatabaseIsNotJoined()
    {
        var snapshot = HealthyGroup();
        snapshot.Databases.Single(d => !d.IsPrimaryReplica).IsDatabaseJoined = false;
        snapshot.AutoSeeding.Add(Seed("2026-01-04 02:00", "FAILED", failureState: "SEEDING_ERROR", errorCode: 41158, attempts: 3));

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Seeding" && i.Severity == AgIssueSeverity.Warning);
        Assert.Contains("most recent seeding attempt failed", finding.Detail);
        Assert.Contains("CREATE ANY DATABASE on the secondary", finding.Recommendation);
    }

    /// <summary>
    /// The unknown must not silence the finding — the same NULL rule the row tinting follows, in the direction that
    /// keeps a real failure visible. The DMV names no replica, so with no join state there is nothing to clear it.
    /// </summary>
    [Fact]
    public void Evaluate_ASeedingFailureStaysAWarningWhenTheJoinStateIsNotVisible()
    {
        var snapshot = HealthyGroup();
        snapshot.Databases.Single(d => !d.IsPrimaryReplica).IsDatabaseJoined = null;   // release without the column
        snapshot.AutoSeeding.Add(Seed("2026-01-04 02:00", "FAILED", failureState: "SEEDING_ERROR", errorCode: 41158, attempts: 3));

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Seeding" && i.Severity == AgIssueSeverity.Warning);
        Assert.Contains("could not be seen from this connection", finding.Detail);
    }

    [Fact]
    public void Evaluate_ASucceededSeedingAttemptIsNotAFindingAtAll()
    {
        var snapshot = HealthyGroup();
        snapshot.AutoSeeding.Add(Seed("2026-01-04 02:30", "COMPLETED", failureState: "NO_FAILURE", performedSeeding: true));

        AgDiagnostics.Evaluate(snapshot, Caps(), new AgThresholds());

        Assert.DoesNotContain(snapshot.Issues, i => i.Area == "Seeding");
    }

    [Fact]
    public void Evaluate_APhysicalSeedingFailureIsAWarningOnlyWhileTheTransferIsRunning()
    {
        var running = HealthyGroup();
        running.Seeding.Add(new AgSeedingRow { LocalDatabaseName = "Sales", RemoteMachineName = "SECONDARY01", FailureMessage = "network error" });

        AgDiagnostics.Evaluate(running, Caps(), new AgThresholds());
        Assert.Single(running.Issues, i => i.Area == "Seeding" && i.Severity == AgIssueSeverity.Warning);

        var finished = HealthyGroup();
        finished.Seeding.Add(new AgSeedingRow
        {
            LocalDatabaseName = "Sales",
            RemoteMachineName = "SECONDARY01",
            FailureMessage = "network error",
            EndTimeUtc = new DateTime(2026, 1, 4, 2, 30, 0)
        });

        AgDiagnostics.Evaluate(finished, Caps(), new AgThresholds());
        Assert.Single(finished.Issues, i => i.Area == "Seeding" && i.Severity == AgIssueSeverity.Information);
    }

    private static AgAutoSeedRow Seed(string startTime, string currentState, string failureState = null, int errorCode = 0, int attempts = 1, bool performedSeeding = false) =>
        new AgAutoSeedRow
        {
            AgName = "AG01",
            DatabaseName = "Sales",
            StartTime = DateTime.Parse(startTime, CultureInfo.InvariantCulture),
            CompletionTime = null,
            CurrentState = currentState,
            PerformedSeeding = performedSeeding,
            IsSource = true,
            FailureState = failureState,
            ErrorCode = errorCode,
            NumberOfAttempts = attempts
        };

    // -----------------------------------------------------------------------------------------------
    // A two-replica synchronous group with automatic failover, everything healthy, seen from the primary.
    // -----------------------------------------------------------------------------------------------

    private static AgCapabilities Caps() => new AgCapabilities
    {
        IsHadrEnabled = true,
        ServerName = "PRIMARY01",
        HasRequiredSyncSecondaries = true,
        IsHealthSessionRunning = true
    };

    private static AgSnapshot HealthyGroup()
    {
        var snapshot = new AgSnapshot { ServerName = "PRIMARY01", LocalRole = "PRIMARY" };

        snapshot.Groups.Add(new AgGroupRow
        {
            Name = "AG01",
            PrimaryReplica = "PRIMARY01",
            SynchronizationHealth = "HEALTHY",
            PrimaryRecoveryHealth = "ONLINE",
            AutomatedBackupPreference = "SECONDARY",
            ClusterType = "WSFC"
        });

        snapshot.Replicas.Add(new AgReplicaRow
        {
            AgName = "AG01",
            ReplicaServerName = "PRIMARY01",
            Role = "PRIMARY",
            AvailabilityMode = "SYNCHRONOUS_COMMIT",
            FailoverMode = "AUTOMATIC",
            OperationalState = "ONLINE",
            ConnectedState = "CONNECTED",
            SynchronizationHealth = "HEALTHY",
            RecoveryHealth = "ONLINE",
            IsLocal = true,
            BackupPriority = 50
        });

        snapshot.Replicas.Add(new AgReplicaRow
        {
            AgName = "AG01",
            ReplicaServerName = "SECONDARY01",
            Role = "SECONDARY",
            AvailabilityMode = "SYNCHRONOUS_COMMIT",
            FailoverMode = "AUTOMATIC",
            OperationalState = "ONLINE",
            ConnectedState = "CONNECTED",
            SynchronizationHealth = "HEALTHY",
            RecoveryHealth = "ONLINE",
            ReadableSecondary = "NO",
            IsLocal = false,
            BackupPriority = 50
        });

        snapshot.Databases.Add(new AgDatabaseRow
        {
            AgName = "AG01",
            ReplicaServerName = "PRIMARY01",
            DatabaseName = "Sales",
            IsPrimaryReplica = true,
            IsLocal = true,
            AvailabilityMode = "SYNCHRONOUS_COMMIT",
            SynchronizationState = "SYNCHRONIZED",
            SynchronizationHealth = "HEALTHY",
            DatabaseState = "ONLINE",
            IsSuspended = false,
            IsFailoverReady = true,
            IsDatabaseJoined = true
        });

        snapshot.Databases.Add(new AgDatabaseRow
        {
            AgName = "AG01",
            ReplicaServerName = "SECONDARY01",
            DatabaseName = "Sales",
            IsPrimaryReplica = false,
            IsLocal = false,
            AvailabilityMode = "SYNCHRONOUS_COMMIT",
            SynchronizationState = "SYNCHRONIZED",
            SynchronizationHealth = "HEALTHY",
            DatabaseState = "ONLINE",
            IsSuspended = false,
            IsFailoverReady = true,
            IsDatabaseJoined = true
        });

        // A healthy group has a listener with its IP online. Without one the "no listener" rule fires, which is
        // correct behaviour but would make every other assertion here read against a degraded fixture.
        snapshot.Listeners.Add(new AgListenerRow
        {
            AgName = "AG01",
            DnsName = "ag01-listener",
            IpAddress = "10.0.0.10",
            Port = 1433,
            State = 1,   // ONLINE
            IsConformant = true
        });

        return snapshot;
    }
}
