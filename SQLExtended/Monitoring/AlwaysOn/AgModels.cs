using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SQLExtended.Monitoring.AlwaysOn;

/// <summary>
/// Minimal change-notification base. The AG grids refresh on a timer and are merged in place by key
/// (see <see cref="RowMerge"/>) rather than rebuilt, so rows must raise PropertyChanged for the new
/// values to reach the UI without resetting selection or scroll position.
/// </summary>
internal abstract class AgRowBase : INotifyPropertyChanged
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

/// <summary>One availability group, from sys.availability_groups + sys.dm_hadr_availability_group_states.</summary>
internal sealed class AgGroupRow : AgRowBase
{
    public Guid GroupId { get; set; }

    private string _name; public string Name { get => _name; set => Set(ref _name, value); }
    private string _primaryReplica; public string PrimaryReplica { get => _primaryReplica; set => Set(ref _primaryReplica, value); }
    private string _syncHealth; public string SynchronizationHealth { get => _syncHealth; set => Set(ref _syncHealth, value); }
    private string _clusterType; public string ClusterType { get => _clusterType; set => Set(ref _clusterType, value); }
    private string _backupPreference; public string AutomatedBackupPreference { get => _backupPreference; set => Set(ref _backupPreference, value); }
    private string _primaryRecoveryHealth; public string PrimaryRecoveryHealth { get => _primaryRecoveryHealth; set => Set(ref _primaryRecoveryHealth, value); }
    private int? _requiredSyncSecondaries; public int? RequiredSynchronizedSecondaries { get => _requiredSyncSecondaries; set => Set(ref _requiredSyncSecondaries, value); }
    private int? _failureConditionLevel; public int? FailureConditionLevel { get => _failureConditionLevel; set => Set(ref _failureConditionLevel, value); }
    private int? _healthCheckTimeout;
    public int? HealthCheckTimeout
    {
        get => _healthCheckTimeout;
        set { Set(ref _healthCheckTimeout, value); Raise(nameof(HealthCheckTimeoutText)); }
    }

    /// <summary>
    /// <c>health_check_timeout</c> for display. The column is in <b>milliseconds</b> and the setting is discussed,
    /// documented and changed in seconds (<c>HEALTH_CHECK_TIMEOUT = 30000</c> being the 30-second default), so the
    /// raw number is shown alongside the seconds rather than instead of them — a card reading "30000" invites the
    /// reader to take it for seconds, and being out by a factor of a thousand on a failover threshold matters.
    /// </summary>
    public string HealthCheckTimeoutText =>
        _healthCheckTimeout == null ? null : $"{_healthCheckTimeout.Value / 1000d:0.##} s  ({_healthCheckTimeout.Value:N0} ms)";

    private bool _isDistributed; public bool IsDistributed { get => _isDistributed; set => Set(ref _isDistributed, value); }

    // Rolled up from the replica/database collections so the overview cards can show counts without re-querying.
    private int _replicaCount; public int ReplicaCount { get => _replicaCount; set => Set(ref _replicaCount, value); }
    private int _databaseCount; public int DatabaseCount { get => _databaseCount; set => Set(ref _databaseCount, value); }
    private int _unhealthyCount; public int UnhealthyCount { get => _unhealthyCount; set => Set(ref _unhealthyCount, value); }
    private int _warningCount; public int WarningCount { get => _warningCount; set => Set(ref _warningCount, value); }
}

/// <summary>One replica in a group, from sys.availability_replicas + sys.dm_hadr_availability_replica_states.</summary>
internal sealed class AgReplicaRow : AgRowBase
{
    public string Key => $"{AgName}|{ReplicaServerName}";

    public string AgName { get; set; }
    public string ReplicaServerName { get; set; }

    private string _role; public string Role { get => _role; set => Set(ref _role, value); }
    private string _availabilityMode; public string AvailabilityMode { get => _availabilityMode; set => Set(ref _availabilityMode, value); }
    private string _failoverMode; public string FailoverMode { get => _failoverMode; set => Set(ref _failoverMode, value); }
    private string _operationalState; public string OperationalState { get => _operationalState; set => Set(ref _operationalState, value); }
    private string _connectedState; public string ConnectedState { get => _connectedState; set => Set(ref _connectedState, value); }
    private string _syncHealth; public string SynchronizationHealth { get => _syncHealth; set => Set(ref _syncHealth, value); }
    private string _recoveryHealth; public string RecoveryHealth { get => _recoveryHealth; set => Set(ref _recoveryHealth, value); }
    private string _seedingMode; public string SeedingMode { get => _seedingMode; set => Set(ref _seedingMode, value); }
    private string _readableSecondary; public string ReadableSecondary { get => _readableSecondary; set => Set(ref _readableSecondary, value); }
    private int? _backupPriority; public int? BackupPriority { get => _backupPriority; set => Set(ref _backupPriority, value); }
    private string _endpointUrl; public string EndpointUrl { get => _endpointUrl; set => Set(ref _endpointUrl, value); }
    private bool _isLocal; public bool IsLocal { get => _isLocal; set => Set(ref _isLocal, value); }
    private int? _sessionTimeout; public int? SessionTimeoutSeconds { get => _sessionTimeout; set => Set(ref _sessionTimeout, value); }

    // Routing URLs live on the replica rather than only on the routing list, because the classic read-intent
    // failure is a replica that is *in* someone's routing list with no URL of its own — which routes nowhere.
    private string _readOnlyRoutingUrl; public string ReadOnlyRoutingUrl { get => _readOnlyRoutingUrl; set => Set(ref _readOnlyRoutingUrl, value); }
    private string _readWriteRoutingUrl; public string ReadWriteRoutingUrl { get => _readWriteRoutingUrl; set => Set(ref _readWriteRoutingUrl, value); }

    private int? _lastConnectErrorNumber; public int? LastConnectErrorNumber { get => _lastConnectErrorNumber; set => Set(ref _lastConnectErrorNumber, value); }
    private string _lastConnectError; public string LastConnectErrorDescription { get => _lastConnectError; set => Set(ref _lastConnectError, value); }
    private DateTime? _lastConnectErrorTime; public DateTime? LastConnectErrorTimestamp { get => _lastConnectErrorTime; set => Set(ref _lastConnectErrorTime, value); }

    /// <summary>
    /// Hard problems only — red. PARTIALLY_HEALTHY and the transitional operational states are handled by
    /// <see cref="IsWarning"/> instead, so a replica mid-transition does not read the same as one that is down.
    /// </summary>
    public bool IsUnhealthy => IsBadState(SynchronizationHealth, "HEALTHY", "PARTIALLY_HEALTHY")
                            || IsBadState(ConnectedState, "CONNECTED")
                            || IsBadState(OperationalState, "ONLINE", "PENDING", "PENDING_FAILOVER", "ONLINE_IN_PROGRESS");

    /// <summary>Degraded or in transition but not broken — amber. Mutually exclusive with <see cref="IsUnhealthy"/>.</summary>
    public bool IsWarning => !IsUnhealthy
                          && (IsState(SynchronizationHealth, "PARTIALLY_HEALTHY")
                           || IsState(OperationalState, "PENDING", "PENDING_FAILOVER", "ONLINE_IN_PROGRESS")
                           || IsState(Role, "RESOLVING"));

    /// <summary>
    /// True only when a state is reported AND is not one of its acceptable values.
    ///
    /// The NULL case matters: several of these columns are only populated for replicas local to the instance
    /// being queried. Connected to a secondary, the primary's operational_state_desc and connected_state_desc
    /// both come back NULL — that means "not visible from this vantage point", not "bad", and treating it as
    /// bad flags a perfectly healthy primary on every group.
    /// </summary>
    internal static bool IsBadState(string desc, params string[] acceptableValues)
    {
        if (string.IsNullOrWhiteSpace(desc)) return false;
        return !IsState(desc, acceptableValues);
    }

    /// <summary>True when the state is reported and matches one of <paramref name="values"/>. Null never matches.</summary>
    internal static bool IsState(string desc, params string[] values)
    {
        if (string.IsNullOrWhiteSpace(desc)) return false;

        foreach (var value in values)
            if (string.Equals(desc, value, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}

/// <summary>
/// One database on one replica, from sys.dm_hadr_database_replica_states joined to
/// sys.dm_hadr_database_replica_cluster_states (which is what supplies the database name for
/// remote replicas — DB_NAME() only resolves locally).
/// </summary>
internal sealed class AgDatabaseRow : AgRowBase
{
    public string Key => $"{AgName}|{ReplicaServerName}|{DatabaseName}";

    public string AgName { get; set; }
    public string ReplicaServerName { get; set; }
    public string DatabaseName { get; set; }

    private bool _isPrimaryReplica; public bool IsPrimaryReplica { get => _isPrimaryReplica; set => Set(ref _isPrimaryReplica, value); }
    private string _availabilityMode; public string AvailabilityMode { get => _availabilityMode; set => Set(ref _availabilityMode, value); }
    private bool _isLocal; public bool IsLocal { get => _isLocal; set => Set(ref _isLocal, value); }
    private string _syncState; public string SynchronizationState { get => _syncState; set => Set(ref _syncState, value); }
    private string _syncHealth; public string SynchronizationHealth { get => _syncHealth; set => Set(ref _syncHealth, value); }
    private string _databaseState; public string DatabaseState { get => _databaseState; set => Set(ref _databaseState, value); }
    private string _suspendReason; public string SuspendReason { get => _suspendReason; set => Set(ref _suspendReason, value); }
    private bool? _isSuspended; public bool? IsSuspended { get => _isSuspended; set => Set(ref _isSuspended, value); }
    private bool? _isFailoverReady; public bool? IsFailoverReady { get => _isFailoverReady; set => Set(ref _isFailoverReady, value); }

    /// <summary>
    /// From dm_hadr_database_replica_cluster_states. A database can be in the availability group's configuration
    /// yet not joined on this replica — it is then not being protected at all, and no synchronization state says so.
    /// </summary>
    private bool? _isDatabaseJoined; public bool? IsDatabaseJoined { get => _isDatabaseJoined; set => Set(ref _isDatabaseJoined, value); }

    private long? _logSendQueueKb; public long? LogSendQueueKb { get => _logSendQueueKb; set { Set(ref _logSendQueueKb, value); Raise(nameof(EstimatedDataLossSeconds)); } }
    private long? _logSendRateKbSec; public long? LogSendRateKbSec { get => _logSendRateKbSec; set { Set(ref _logSendRateKbSec, value); Raise(nameof(EstimatedDataLossSeconds)); } }
    private long? _redoQueueKb; public long? RedoQueueKb { get => _redoQueueKb; set { Set(ref _redoQueueKb, value); Raise(nameof(EstimatedRecoverySeconds)); } }
    private long? _redoRateKbSec; public long? RedoRateKbSec { get => _redoRateKbSec; set { Set(ref _redoRateKbSec, value); Raise(nameof(EstimatedRecoverySeconds)); } }
    private long? _filestreamSendRateKbSec; public long? FilestreamSendRateKbSec { get => _filestreamSendRateKbSec; set => Set(ref _filestreamSendRateKbSec, value); }
    private long? _secondaryLagSeconds; public long? SecondaryLagSeconds { get => _secondaryLagSeconds; set => Set(ref _secondaryLagSeconds, value); }
    private DateTime? _lastCommitTime; public DateTime? LastCommitTime { get => _lastCommitTime; set => Set(ref _lastCommitTime, value); }

    private string _endOfLogLsn; public string EndOfLogLsn { get => _endOfLogLsn; set => Set(ref _endOfLogLsn, value); }
    private string _lastHardenedLsn; public string LastHardenedLsn { get => _lastHardenedLsn; set => Set(ref _lastHardenedLsn, value); }
    private string _lastRedoneLsn; public string LastRedoneLsn { get => _lastRedoneLsn; set => Set(ref _lastRedoneLsn, value); }

    /// <summary>
    /// RPO estimate: how long the send queue would take to drain at the current send rate. This is the
    /// number that matters after an unplanned failover — it is the data you would lose.
    /// </summary>
    public double? EstimatedDataLossSeconds => LogSendRateKbSec.GetValueOrDefault() > 0 ? LogSendQueueKb / (double)LogSendRateKbSec : null;

    /// <summary>RTO estimate: how long the redo queue would take to drain at the current redo rate.</summary>
    public double? EstimatedRecoverySeconds => RedoRateKbSec.GetValueOrDefault() > 0 ? RedoQueueKb / (double)RedoRateKbSec : null;

    // Same NULL rule as AgReplicaRow: from a secondary, remote replicas' database rows report NULL states.
    public bool IsUnhealthy => IsSuspended == true
                            || AgReplicaRow.IsBadState(SynchronizationHealth, "HEALTHY", "PARTIALLY_HEALTHY")
                            || AgReplicaRow.IsBadState(DatabaseState, "ONLINE")
                            || AgReplicaRow.IsBadState(SynchronizationState, "SYNCHRONIZED", "SYNCHRONIZING");

    /// <summary>
    /// Amber. SYNCHRONIZING only counts when the replica is SYNCHRONOUS_COMMIT — on an asynchronous replica it
    /// is the normal steady state, and tinting every async secondary would drain the colour of meaning. Same
    /// reasoning for is_failover_ready, which is permanently 0 on async replicas and only tells you something
    /// on a synchronous one.
    /// </summary>
    public bool IsWarning
    {
        get
        {
            if (IsUnhealthy) return false;
            if (AgReplicaRow.IsState(SynchronizationHealth, "PARTIALLY_HEALTHY")) return true;

            bool isSynchronous = AgReplicaRow.IsState(AvailabilityMode, "SYNCHRONOUS_COMMIT");
            if (!isSynchronous) return false;

            return AgReplicaRow.IsState(SynchronizationState, "SYNCHRONIZING")
                || (IsFailoverReady == false && !IsPrimaryReplica);
        }
    }

    /// <summary>Rolling sample buffers backing the queue sparklines. Owned by <see cref="AgHistory"/>.</summary>
    public IReadOnlyList<double> SendQueueHistory { get; internal set; }
    public IReadOnlyList<double> RedoQueueHistory { get; internal set; }

    internal void RaiseHistoryChanged() { Raise(nameof(SendQueueHistory)); Raise(nameof(RedoQueueHistory)); }
}

/// <summary>An in-flight automatic-seeding transfer, from sys.dm_hadr_physical_seeding_stats (SQL 2016+).</summary>
internal sealed class AgSeedingRow : AgRowBase
{
    public string Key => $"{LocalDatabaseName}|{RemoteMachineName}|{Role}";

    public string LocalDatabaseName { get; set; }
    public string RemoteMachineName { get; set; }
    public string Role { get; set; }

    private string _internalState; public string InternalState { get => _internalState; set => Set(ref _internalState, value); }
    private long _transferredBytes; public long TransferredBytes { get => _transferredBytes; set { Set(ref _transferredBytes, value); Raise(nameof(PercentComplete)); Raise(nameof(RemainingBytes)); } }
    private long _databaseSizeBytes; public long DatabaseSizeBytes { get => _databaseSizeBytes; set { Set(ref _databaseSizeBytes, value); Raise(nameof(PercentComplete)); Raise(nameof(RemainingBytes)); } }
    private long _transferRateBytesPerSecond; public long TransferRateBytesPerSecond { get => _transferRateBytesPerSecond; set { Set(ref _transferRateBytesPerSecond, value); Raise(nameof(EstimatedSecondsRemaining)); } }
    private DateTime? _startTimeUtc; public DateTime? StartTimeUtc { get => _startTimeUtc; set => Set(ref _startTimeUtc, value); }
    private DateTime? _endTimeUtc; public DateTime? EndTimeUtc { get => _endTimeUtc; set => Set(ref _endTimeUtc, value); }
    private DateTime? _estimateCompleteUtc; public DateTime? EstimateCompleteUtc { get => _estimateCompleteUtc; set => Set(ref _estimateCompleteUtc, value); }
    private long _diskIoWaitMs; public long TotalDiskIoWaitMs { get => _diskIoWaitMs; set => Set(ref _diskIoWaitMs, value); }
    private long _networkWaitMs; public long TotalNetworkWaitMs { get => _networkWaitMs; set => Set(ref _networkWaitMs, value); }
    private bool _isCompressionEnabled; public bool IsCompressionEnabled { get => _isCompressionEnabled; set => Set(ref _isCompressionEnabled, value); }
    private string _failureMessage; public string FailureMessage { get => _failureMessage; set => Set(ref _failureMessage, value); }

    public double PercentComplete => DatabaseSizeBytes > 0 ? Math.Min(100d, TransferredBytes * 100d / DatabaseSizeBytes) : 0d;
    public long RemainingBytes => Math.Max(0, DatabaseSizeBytes - TransferredBytes);
    public double? EstimatedSecondsRemaining => TransferRateBytesPerSecond > 0 ? RemainingBytes / (double)TransferRateBytesPerSecond : null;
}

/// <summary>A seeding attempt and its outcome, from sys.dm_hadr_automatic_seeding (SQL 2016+).</summary>
internal sealed class AgAutoSeedRow : AgRowBase
{
    public string Key => $"{AgName}|{DatabaseName}|{StartTime:O}";

    public string AgName { get; set; }
    public string DatabaseName { get; set; }
    public DateTime? StartTime { get; set; }

    private DateTime? _completionTime; public DateTime? CompletionTime { get => _completionTime; set => Set(ref _completionTime, value); }
    private string _currentState; public string CurrentState { get => _currentState; set => Set(ref _currentState, value); }
    private bool _performedSeeding; public bool PerformedSeeding { get => _performedSeeding; set => Set(ref _performedSeeding, value); }
    private bool _isSource; public bool IsSource { get => _isSource; set => Set(ref _isSource, value); }
    private string _failureState; public string FailureState { get => _failureState; set => Set(ref _failureState, value); }
    private int? _errorCode; public int? ErrorCode { get => _errorCode; set => Set(ref _errorCode, value); }
    private int _attempts; public int NumberOfAttempts { get => _attempts; set => Set(ref _attempts, value); }

    /// <summary>
    /// This attempt recorded a failure. It is a property of the <em>attempt</em>, not of the database: the DMV is a
    /// history table, so a seed that failed once and succeeded on the retry leaves this true on the older row for as
    /// long as the rows live. Whether the failure still matters is <see cref="AgDiagnostics"/>'s call — see
    /// CheckSeeding, which judges only the newest attempt per database and wants current state as the evidence.
    /// </summary>
    public bool IsFailed => ErrorCode.GetValueOrDefault() != 0 || !string.IsNullOrEmpty(FailureState) && !FailureState.Equals("NO_FAILURE", StringComparison.OrdinalIgnoreCase);

    /// <summary>The attempt finished and seeded — current_state is COMPLETED. Compared case-insensitively, so a
    /// release that changes the casing cannot turn a completed seed back into an open failure.</summary>
    public bool IsCompleted => !string.IsNullOrEmpty(CurrentState) && CurrentState.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase);
}

// -------------------------------------------------------------------------------------------------------
// Cluster and quorum
// -------------------------------------------------------------------------------------------------------

/// <summary>
/// The cluster hosting this instance, from sys.dm_hadr_cluster. One row, or none at all when cluster_type is
/// NONE (a read-scale AG) — which is a valid configuration, not a fault.
/// </summary>
internal sealed class AgClusterInfo
{
    public string ClusterName { get; set; }
    public string QuorumType { get; set; }
    public string QuorumState { get; set; }

    /// <summary>Anything other than NORMAL_QUORUM means the cluster is one failure from going offline.</summary>
    public bool IsQuorumHealthy => AgReplicaRow.IsState(QuorumState, "NORMAL_QUORUM");
}

/// <summary>
/// One quorum voter, from sys.dm_hadr_cluster_members. The vote count is the point: a member that is DOWN
/// while holding a vote is what turns a single node failure into a whole-cluster outage.
/// </summary>
internal sealed class AgClusterMemberRow : AgRowBase
{
    public string Key => MemberName ?? "";
    public string MemberName { get; set; }

    private string _memberType; public string MemberType { get => _memberType; set => Set(ref _memberType, value); }
    private string _memberState; public string MemberState { get => _memberState; set => Set(ref _memberState, value); }
    private int? _quorumVotes; public int? QuorumVotes { get => _quorumVotes; set => Set(ref _quorumVotes, value); }

    /// <summary>UP is the only healthy member state; DOWN and everything else is a hard problem.</summary>
    public bool IsUnhealthy => AgReplicaRow.IsBadState(MemberState, "UP");
}

/// <summary>A cluster network, from sys.dm_hadr_cluster_networks. Multi-subnet AGs live or die by these.</summary>
internal sealed class AgClusterNetworkRow : AgRowBase
{
    public string Key => $"{MemberName}|{NetworkSubnetIp}|{PrefixLength}";

    public string MemberName { get; set; }
    public string NetworkSubnetIp { get; set; }
    public int? PrefixLength { get; set; }

    private string _subnetMask; public string NetworkSubnetMask { get => _subnetMask; set => Set(ref _subnetMask, value); }
    private bool? _isPublic; public bool? IsPublic { get => _isPublic; set => Set(ref _isPublic, value); }
    private bool? _isIpv4; public bool? IsIpv4 { get => _isIpv4; set => Set(ref _isIpv4, value); }
}

/// <summary>
/// A replica's cluster-level view, from sys.dm_hadr_availability_replica_cluster_nodes joined to
/// sys.dm_hadr_availability_replica_cluster_states. join_state_desc is the piece the replica-state DMVs do not
/// carry: a replica can look configured yet not be joined to the WSFC group at all.
/// </summary>
internal sealed class AgClusterNodeRow : AgRowBase
{
    // The node name is part of the key, not just a value: a replica hosted on a failover cluster instance has
    // one row per possible owner node, and keying on the replica alone would silently drop all but the first.
    public string Key => $"{AgName}|{ReplicaServerName}|{NodeName}";

    public string AgName { get; set; }
    public string ReplicaServerName { get; set; }
    public string NodeName { get; set; }
    private string _joinState; public string JoinState { get => _joinState; set => Set(ref _joinState, value); }

    /// <summary>NOT_JOINED means this replica is not participating at all — red.</summary>
    public bool IsUnhealthy => AgReplicaRow.IsState(JoinState, "NOT_JOINED");

    /// <summary>
    /// JOINED_STANDALONE_NO_QUORUM and JOINED_FAILOVER_NOT_READY are joined but unable to fail over. Amber:
    /// replication is working, the safety net is not.
    /// </summary>
    public bool IsWarning => !IsUnhealthy && AgReplicaRow.IsState(JoinState, "JOINED_STANDALONE_NO_QUORUM", "JOINED_FAILOVER_NOT_READY");
}

// -------------------------------------------------------------------------------------------------------
// Listeners and read-only routing
// -------------------------------------------------------------------------------------------------------

/// <summary>
/// One listener IP, from sys.availability_group_listeners joined to
/// sys.availability_group_listener_ip_addresses. One row per IP rather than per listener, because a
/// multi-subnet listener's individual IPs are exactly what goes offline independently.
/// </summary>
internal sealed class AgListenerRow : AgRowBase
{
    public string Key => $"{AgName}|{DnsName}|{IpAddress}";

    public string AgName { get; set; }
    public string DnsName { get; set; }
    public string IpAddress { get; set; }

    private int? _port; public int? Port { get => _port; set => Set(ref _port, value); }
    private string _subnetMask; public string IpSubnetMask { get => _subnetMask; set => Set(ref _subnetMask, value); }
    private string _networkSubnetIp; public string NetworkSubnetIp { get => _networkSubnetIp; set => Set(ref _networkSubnetIp, value); }
    private bool? _isDhcp; public bool? IsDhcp { get => _isDhcp; set => Set(ref _isDhcp, value); }
    private bool? _isConformant; public bool? IsConformant { get => _isConformant; set => Set(ref _isConformant, value); }
    private string _ipConfiguration; public string IpConfigurationFromCluster { get => _ipConfiguration; set => Set(ref _ipConfiguration, value); }
    private int? _state; public int? State { get => _state; set { Set(ref _state, value); Raise(nameof(StateDescription)); Raise(nameof(IsUnhealthy)); Raise(nameof(IsWarning)); } }

    /// <summary>
    /// sys.availability_group_listener_ip_addresses.state has no *_desc companion, so the mapping is here:
    /// <b>0 offline, 1 online</b>, 2 online-pending, 3 failed. Note that 0 is the bad one — the opposite way
    /// round to almost every other state column in these DMVs, and getting it backwards reads as a healthy
    /// listener being down. NULL is "not reported from this instance" — the same vantage-point rule the replica
    /// states follow.
    /// </summary>
    public string StateDescription
    {
        get
        {
            switch (State)
            {
                case 0: return "OFFLINE";
                case 1: return "ONLINE";
                case 2: return "ONLINE_PENDING";
                case 3: return "FAILED";
                default: return null;
            }
        }
    }

    /// <summary>An offline or failed listener IP is a client-facing outage even with perfectly healthy replicas.</summary>
    public bool IsUnhealthy => State == 0 || State == 3;

    /// <summary>Mid-transition, or a cluster IP configuration SQL Server does not consider conformant.</summary>
    public bool IsWarning => !IsUnhealthy && (State == 2 || IsConformant == false);
}

/// <summary>
/// One entry of one replica's read-only routing list, from sys.availability_read_only_routing_lists.
/// Read-intent connections are routed in priority order, so the order is the interesting part.
/// </summary>
internal sealed class AgRoutingRow : AgRowBase
{
    public string Key => $"{AgName}|{SourceReplica}|{RoutingPriority}|{TargetReplica}";

    public string AgName { get; set; }
    public string SourceReplica { get; set; }
    public int RoutingPriority { get; set; }
    public string TargetReplica { get; set; }

    private string _targetReadableSecondary; public string TargetReadableSecondary { get => _targetReadableSecondary; set => Set(ref _targetReadableSecondary, value); }
    private string _targetRole; public string TargetRole { get => _targetRole; set => Set(ref _targetRole, value); }
    private string _readOnlyRoutingUrl; public string ReadOnlyRoutingUrl { get => _readOnlyRoutingUrl; set => Set(ref _readOnlyRoutingUrl, value); }
    private string _readWriteRoutingUrl; public string ReadWriteRoutingUrl { get => _readWriteRoutingUrl; set => Set(ref _readWriteRoutingUrl, value); }

    /// <summary>
    /// A routing target with no read_only_routing_url never receives a routed connection — the list looks
    /// configured and silently does nothing, which is the failure mode worth colouring.
    /// </summary>
    public bool IsWarning => string.IsNullOrWhiteSpace(ReadOnlyRoutingUrl)
                          || AgReplicaRow.IsState(TargetReadableSecondary, "NO");
}

// -------------------------------------------------------------------------------------------------------
// Throughput and commit latency, from the AG performance counters
// -------------------------------------------------------------------------------------------------------

/// <summary>
/// Per-database throughput from the <c>SQLServer:Database Replica</c> counter object. These are only populated
/// for databases on the instance being queried, so this tab describes the local replica's own work.
///
/// The reason this exists alongside the queue columns on the Databases tab: queue sizes tell you how far behind
/// a secondary is, but not what synchronous commit is costing the primary. Transaction Delay divided by
/// Mirrored Write Transactions/sec does, in milliseconds per commit — both differenced first, see
/// <see cref="AvgCommitDelayMs"/>.
/// </summary>
internal sealed class AgThroughputRow : AgRowBase
{
    public string Key => DatabaseName ?? "";
    public string DatabaseName { get; set; }

    // Point-in-time gauges (raw counters) — already a level, so no delta needed.
    private long? _logSendQueueKb; public long? LogSendQueueKb { get => _logSendQueueKb; set => Set(ref _logSendQueueKb, value); }
    private long? _recoveryQueueKb; public long? RecoveryQueueKb { get => _recoveryQueueKb; set => Set(ref _recoveryQueueKb, value); }
    private long? _redoBytesRemainingKb; public long? RedoBytesRemainingKb { get => _redoBytesRemainingKb; set => Set(ref _redoBytesRemainingKb, value); }
    private long? _logRemainingForUndoKb; public long? LogRemainingForUndoKb { get => _logRemainingForUndoKb; set => Set(ref _logRemainingForUndoKb, value); }
    private long? _totalLogRequiringUndoKb; public long? TotalLogRequiringUndoKb { get => _totalLogRequiringUndoKb; set => Set(ref _totalLogRequiringUndoKb, value); }

    // Rates, derived from the cumulative counters against the server's own tick interval.

    /// <summary>
    /// Milliseconds of commit wait accumulated per second — Transaction Delay <em>differenced</em>, not the raw
    /// counter. Despite the name, the DMV reports it as a running total since the counters started, so the raw
    /// value is a whole-uptime figure. See <see cref="AvgCommitDelayMs"/>; this shipped undifferenced once.
    /// </summary>
    private double? _transactionDelayMsPerSec; public double? TransactionDelayMsPerSec { get => _transactionDelayMsPerSec; set { Set(ref _transactionDelayMsPerSec, value); Raise(nameof(AvgCommitDelayMs)); Raise(nameof(IsWarning)); } }

    private double? _mirroredWriteTransactionsPerSec; public double? MirroredWriteTransactionsPerSec { get => _mirroredWriteTransactionsPerSec; set { Set(ref _mirroredWriteTransactionsPerSec, value); Raise(nameof(AvgCommitDelayMs)); Raise(nameof(IsWarning)); } }
    private double? _logBytesReceivedPerSec; public double? LogBytesReceivedPerSec { get => _logBytesReceivedPerSec; set => Set(ref _logBytesReceivedPerSec, value); }
    private double? _redoneBytesPerSec; public double? RedoneBytesPerSec { get => _redoneBytesPerSec; set => Set(ref _redoneBytesPerSec, value); }
    private double? _fileBytesReceivedPerSec; public double? FileBytesReceivedPerSec { get => _fileBytesReceivedPerSec; set => Set(ref _fileBytesReceivedPerSec, value); }

    /// <summary>Amber above this many milliseconds of added commit latency. Set from settings each poll.</summary>
    internal double CommitDelayWarningMs { get; set; } = 20d;

    /// <summary>
    /// Average commit delay in milliseconds per transaction. This is the number that answers "what is synchronous
    /// commit costing me"; single-digit milliseconds is normal, tens of milliseconds is a network or
    /// remote-log-flush problem.
    ///
    /// <b>Both operands are per-second rates, and that is what makes the units work.</b> Commit wait accumulated
    /// per second over commits completed per second cancels the seconds and leaves milliseconds per commit — the
    /// same division Microsoft's own guidance describes, where Performance Monitor has already differenced both
    /// counters. This shipped dividing the *raw* Transaction Delay by the commit rate, which is a whole-uptime
    /// total over a rate: dimensionally ms·s per commit, and a number that climbs for as long as the instance stays
    /// up. It read as 63 seconds per commit on a healthy AG. If this ever looks implausibly large again, check that
    /// the numerator is still being differenced before believing the AG is slow.
    /// </summary>
    public double? AvgCommitDelayMs => TransactionDelayMsPerSec != null && MirroredWriteTransactionsPerSec.GetValueOrDefault() > 0
        ? TransactionDelayMsPerSec / MirroredWriteTransactionsPerSec
        : null;

    public bool IsWarning => AvgCommitDelayMs.GetValueOrDefault() > CommitDelayWarningMs;

    internal void RaiseThresholdChanged() => Raise(nameof(IsWarning));
}

/// <summary>
/// Per-replica transport health from the <c>SQLServer:Availability Replica</c> counter object.
///
/// Flow control is the metric here. When the log send queue is capped by the transport rather than by disk or
/// network throughput, SQL Server throttles sends and accumulates flow-control time — a saturated link shows up
/// here long before it shows up as a synchronization-health change.
/// </summary>
internal sealed class AgTransportRow : AgRowBase
{
    public string Key => Instance ?? "";

    /// <summary>The counter object's instance name, as reported. Format varies by release, so it is shown verbatim.</summary>
    public string Instance { get; set; }

    private double? _bytesSentToReplicaPerSec; public double? BytesSentToReplicaPerSec { get => _bytesSentToReplicaPerSec; set => Set(ref _bytesSentToReplicaPerSec, value); }
    private double? _bytesSentToTransportPerSec; public double? BytesSentToTransportPerSec { get => _bytesSentToTransportPerSec; set => Set(ref _bytesSentToTransportPerSec, value); }
    private double? _bytesReceivedFromReplicaPerSec; public double? BytesReceivedFromReplicaPerSec { get => _bytesReceivedFromReplicaPerSec; set => Set(ref _bytesReceivedFromReplicaPerSec, value); }
    private double? _sendsToReplicaPerSec; public double? SendsToReplicaPerSec { get => _sendsToReplicaPerSec; set => Set(ref _sendsToReplicaPerSec, value); }
    private double? _receivesFromReplicaPerSec; public double? ReceivesFromReplicaPerSec { get => _receivesFromReplicaPerSec; set => Set(ref _receivesFromReplicaPerSec, value); }
    private double? _resentMessagesPerSec; public double? ResentMessagesPerSec { get => _resentMessagesPerSec; set { Set(ref _resentMessagesPerSec, value); Raise(nameof(IsWarning)); } }
    private double? _flowControlPerSec; public double? FlowControlPerSec { get => _flowControlPerSec; set { Set(ref _flowControlPerSec, value); Raise(nameof(IsWarning)); } }
    private double? _flowControlTimeMsPerSec; public double? FlowControlTimeMsPerSec { get => _flowControlTimeMsPerSec; set { Set(ref _flowControlTimeMsPerSec, value); Raise(nameof(IsWarning)); } }

    /// <summary>
    /// Amber once a meaningful fraction of each second is spent in flow control, or once messages are being
    /// resent — both mean the link, not the workload, is setting the pace.
    /// </summary>
    public bool IsWarning => FlowControlTimeMsPerSec.GetValueOrDefault() > 100d || ResentMessagesPerSec.GetValueOrDefault() > 0d;
}

// -------------------------------------------------------------------------------------------------------
// Diagnostics
// -------------------------------------------------------------------------------------------------------

/// <summary>How badly a diagnostic finding wants attention. Ordered so a plain sort puts the worst first.</summary>
internal enum AgIssueSeverity
{
    Critical = 0,
    Warning = 1,
    Information = 2
}

/// <summary>
/// One finding from <see cref="AgDiagnostics"/>: a rule that fired, what it fired about, and what to do.
///
/// The grids show state; this says whether that state is a problem. Some of the worst Always On conditions are
/// invisible in any single grid because they are a *combination* — an automatic-failover pair whose secondary is
/// merely SYNCHRONIZING looks fine on both the Replicas and Databases tabs, and means the cluster cannot fail
/// over right now.
/// </summary>
internal sealed class AgIssueRow
{
    public AgIssueSeverity Severity { get; set; }

    /// <summary>Which part of the configuration the finding came from — Cluster, Replica, Database, Listener…</summary>
    public string Area { get; set; }

    /// <summary>What it is about: an AG name, a replica, "AG / database on replica".</summary>
    public string Subject { get; set; }

    /// <summary>The finding, in one sentence.</summary>
    public string Detail { get; set; }

    /// <summary>What to do about it, or what it means if nothing needs doing.</summary>
    public string Recommendation { get; set; }

    public string SeverityText => Severity == AgIssueSeverity.Critical ? "CRITICAL" : Severity == AgIssueSeverity.Warning ? "WARNING" : "INFO";

    // Row tinting: the shared grid style keys off these two names.
    public bool IsUnhealthy => Severity == AgIssueSeverity.Critical;
    public bool IsWarning => Severity == AgIssueSeverity.Warning;
}

/// <summary>An event read out of the AlwaysOn_health extended-event session.</summary>
internal sealed class AgEventRow
{
    public DateTime EventTimeUtc { get; set; }
    public string EventName { get; set; }
    public int? ErrorNumber { get; set; }
    public int? Severity { get; set; }
    public int? ErrorState { get; set; }
    public string Message { get; set; }
    public string AgName { get; set; }
    public string DatabaseName { get; set; }
    public string ReplicaServerName { get; set; }

    public DateTime EventTimeLocal => EventTimeUtc.Kind == DateTimeKind.Utc ? EventTimeUtc.ToLocalTime() : DateTime.SpecifyKind(EventTimeUtc, DateTimeKind.Utc).ToLocalTime();

    /// <summary>Severity 16+ is a user-visible error; 20+ is fatal. Drives the row tint on the Errors tab.</summary>
    public bool IsError => Severity.GetValueOrDefault() >= 16 || string.Equals(EventName, "error_reported", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(EventName, "availability_group_lease_expired", StringComparison.OrdinalIgnoreCase);

    /// <summary>Severity 11–15 is a user-correctable condition rather than a failure — amber.</summary>
    public bool IsWarning => !IsError && Severity.GetValueOrDefault() >= 11 && Severity.GetValueOrDefault() <= 15;
}

/// <summary>Everything one poll cycle collected. Assembled off the UI thread, then merged into the grids.</summary>
internal sealed class AgSnapshot
{
    public List<AgGroupRow> Groups { get; } = new List<AgGroupRow>();
    public List<AgReplicaRow> Replicas { get; } = new List<AgReplicaRow>();
    public List<AgDatabaseRow> Databases { get; } = new List<AgDatabaseRow>();
    public List<AgSeedingRow> Seeding { get; } = new List<AgSeedingRow>();
    public List<AgAutoSeedRow> AutoSeeding { get; } = new List<AgAutoSeedRow>();
    public List<AgClusterMemberRow> ClusterMembers { get; } = new List<AgClusterMemberRow>();
    public List<AgClusterNetworkRow> ClusterNetworks { get; } = new List<AgClusterNetworkRow>();
    public List<AgClusterNodeRow> ClusterNodes { get; } = new List<AgClusterNodeRow>();
    public List<AgListenerRow> Listeners { get; } = new List<AgListenerRow>();
    public List<AgRoutingRow> Routing { get; } = new List<AgRoutingRow>();
    public List<AgThroughputRow> Throughput { get; } = new List<AgThroughputRow>();
    public List<AgTransportRow> Transport { get; } = new List<AgTransportRow>();

    /// <summary>Derived by <see cref="AgDiagnostics"/> after collection, not read from a DMV.</summary>
    public List<AgIssueRow> Issues { get; } = new List<AgIssueRow>();

    /// <summary>The cluster hosting this instance, or null when cluster_type is NONE or the view is empty.</summary>
    public AgClusterInfo Cluster { get; set; }

    public string ServerName { get; set; }

    /// <summary>The login this poll ran as, per <c>SUSER_SNAME()</c>. Shown beside the server in the header.</summary>
    public string LoginName { get; set; }
    public DateTime CollectedAtLocal { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// How many sections this poll read and how many of those failed, reported next to the timing. What the window
    /// covers varies with the release and the login's rights, so "9 sections in 212 ms" says considerably more
    /// about an empty tab than the duration alone.
    /// </summary>
    public int SectionsRead { get; set; }

    public int SectionsFailed { get; set; }

    /// <summary>
    /// Seconds between the two counter readings the rates were computed from, per the server's own ms_ticks.
    /// Null on the very first poll of a server, where the rate columns have no baseline to work from.
    /// </summary>
    public double? CounterIntervalSeconds { get; set; }

    /// <summary>The local replica's role, resolved once so callers do not re-scan the replica list.</summary>
    public string LocalRole { get; set; }

    /// <summary>
    /// Per-section failures. Each DMV section is collected independently so an unexpected schema
    /// difference on one view degrades that tab instead of blanking the whole dashboard.
    /// </summary>
    public List<string> Warnings { get; } = new List<string>();

    /// <summary>Set when HADR is off or the instance has no groups — the UI shows this instead of empty grids.</summary>
    public string UnavailableReason { get; set; }
    public bool IsAvailable => UnavailableReason == null;
}
