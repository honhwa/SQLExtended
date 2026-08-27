# Always On monitor

> Pinning, staged collection and the rules common to all four dashboards are in
> `SQLExtended/Monitoring/CLAUDE.md` — read that first.

`Monitoring/AlwaysOn/` is the availability-group monitor (Ctrl+Alt+A, `AgMonitorToolWindow`). It follows the
active query window's connection, forced to `master` — the editor may be sitting on a non-readable secondary.
Needs only `VIEW SERVER STATE`. Live-only: nothing is persisted, and `AgHistory` keeps a 120-sample in-memory
window per database-per-replica to back the queue sparklines.

Version-safety is handled by `AgCapabilities`, which probes `sys.all_columns` for the specific optional columns
(`secondary_lag_seconds`, `cluster_type_desc`, `is_distributed`, `seeding_mode_desc`) instead of branching on
version numbers, then `AgCapabilities.Column` substitutes `NULL AS <alias>` where a column is absent so the
reader can always address every column by name. Each DMV section is collected in its own try/catch and records
into `AgSnapshot.Warnings` — a surprise on one view costs one tab, not the dashboard.

Health is three-tier: `IsUnhealthy` (red) for hard failures, `IsWarning` (amber) for degraded/transitional, neutral
otherwise. The two bools are mutually exclusive by construction (`IsWarning` short-circuits on `IsUnhealthy`).
Two rules drive the predicates, and both exist to stop the tint becoming noise:
- **NULL means "not visible from here", not "bad."** Several `*_state_desc` columns are populated only for
  replicas local to the queried instance — from a secondary the primary's `operational_state_desc` is NULL.
  `AgReplicaRow.IsBadState` returns false for null; only `IsState` matches an actual value.
- **`SYNCHRONIZING` is amber only on a `SYNCHRONOUS_COMMIT` replica.** On an async replica it is the normal
  steady state. This is why `DatabasesSql` carries `ar.availability_mode_desc` — `AgDatabaseRow.IsWarning`
  cannot decide without it. Same reasoning for `is_failover_ready`, which is permanently 0 when async.

Two more things worth knowing before changing this:
- **Grids are merged in place by key** (`RowMerge`), not rebound, or a 5-second refresh would throw away
  selection and scroll. Rows therefore raise `PropertyChanged`, and `AgHistory` hands out a *fresh array* each
  poll — a DependencyProperty set to a reference-equal value is a no-op, so a mutated buffer leaves sparklines
  frozen.
- **The Errors tab loads on demand.** It reads the `AlwaysOn_health` XEvent session's *current* rollover file
  via `sys.fn_xe_file_target_read_file`; casting the whole 5-file default set to XML server-side is far too
  expensive to sit on a timer.

Tabs: Overview, Diagnostics, Replicas, Databases, Throughput, Cluster, Listeners, Seeding, Errors. Both
`ActiveGrid()` and `SqlForActiveTab()` switch on named `Tab*` constants — they were bare indices once, and a
tab inserted in the middle silently repointed both.

## Diagnostics tab

`AgDiagnostics` is a rules engine over the snapshot the other tabs are already built from — no extra round trip.
It exists because the grids show *state* and state is not a verdict; the worst Always On conditions are
combinations no single grid reveals:
- An automatic-failover pair whose secondary is merely `SYNCHRONIZING` reads HEALTHY on both the Replicas and
  Databases tabs, and means the cluster **cannot fail over right now**.
- A group below `required_synchronized_secondaries_to_commit` has a primary that is **refusing commits** while
  every replica still reports HEALTHY. Nothing in any DMV column says so in words.

Two rules govern every check, and both exist to stop findings becoming noise: **NULL is never a finding** (the
same vantage-point rule the row tinting follows), and **`SYNCHRONOUS_COMMIT` is what makes `SYNCHRONIZING`
interesting**. When nothing fires, an explicit all-clear row lists what was checked and — from a secondary —
what was not visible. An empty grid reads equally as "healthy" and "this tab is broken".

**A third rule, learned the hard way: the seeding DMVs report finished work, so a finding has to be checked
against current state.** `sys.dm_hadr_automatic_seeding` is a *history* table — one row per attempt, kept for the
life of the group, memory-resident so only a restart clears it. A seed that failed once and succeeded on the retry
leaves **both** rows behind, and the first version of `CheckSeeding` fired on every failed row: a fixed problem
reported as a current one, forever, which on this tab is indistinguishable from a database that is unprotected
right now. So only the **newest attempt per (group, database)** is judged, and even that is demoted to Information
once the database is demonstrably seeded — the attempt says `COMPLETED`, or every secondary reports
`is_database_joined = 1`. The DMV names **no replica**, which is why the join state has to come from
`snapshot.Databases`; one secondary explicitly *not* joined answers "still broken" whatever the others say, and an
unknown join state (no rows, or a release without the column) leaves the warning standing — unknown must not
silence a real failure. `sys.dm_hadr_physical_seeding_stats` needs the same treatment for the same reason:
`end_time_utc` is the only thing separating a transfer still running from one that finished, and a
`failure_message` on a finished one is a record, not a fault. `SoluitionDocs/Queries/alwayson-seeding-check.sql`
is the by-hand version of this judgement — reach for it when a seeding finding is disputed.

Thresholds come from `SQLExtendedSettings` (`Ag*`) so they are per-installation, and are read on the UI thread each
poll rather than captured once.

## Throughput tab

From `sys.dm_os_performance_counters`, not the HADR DMVs, and the only tab that needs `AgCounterTracker`.
- The queue columns on the Databases tab say how far behind a secondary is; **`Transaction Delay` ÷
  `Mirrored Write Transactions/sec`** says what synchronous commit is costing the primary, in milliseconds per
  commit. That is the number to reach for when "the AG is healthy but writes are slow".
- Cumulative counters (`cntr_type` 272696576 / 272696320) are differenced against the previous reading;
  `PERF_COUNTER_LARGE_RAWCOUNT` values are already levels and pass straight through. The type is read from the
  DMV rather than assumed per counter name.
- **`Transaction Delay` is one of those cumulative counters, and both operands of that ratio have to be rates.**
  Its name reads like a level and it is not one — the DMV reports a running total of milliseconds waited since the
  counters started. It shipped used raw, dividing a whole-uptime total by a per-second rate: dimensionally ms·s per
  commit, climbing for as long as the instance stays up, and reported on a healthy AG as *63,450 ms per commit*
  (317,250 ms of accumulated wait over 5 commits/s). Differenced, the units cancel — wait-ms per second over
  commits per second is milliseconds per commit, which is the division Microsoft's own guidance describes, where
  Performance Monitor has already differenced both. `AgQueryService.CommitWaitMsPerSecond` makes the call from
  `cntr_type`, not the counter name, and is internal so `AgDiagnosticsTests` can pin it. If this number ever looks
  implausibly large again, check the numerator is still being differenced before believing the AG is slow. The
  Transport row's `Flow Control Time (ms/sec)` was always differenced and was never affected.
- **The first poll per server reads twice, 1 s apart** (`BaselineSampleMs`), so the rate columns have numbers
  immediately — the same trade the Performance dashboard makes, paid once per server.
- `Group Commit Time` / `Group Commits/Sec` are deliberately *not* shown: their units could not be pinned down
  confidently enough to label honestly.
- The `SQLServer:Availability Replica` object's `instance_name` format has varied across releases, so it is
  displayed verbatim under a neutral "Counter instance" heading rather than parsed.

## Cluster and Listeners tabs

Both cover things no replica-state column mentions:
- **Quorum** (`sys.dm_hadr_cluster`, `_cluster_members`, `_cluster_networks`) is the one condition that takes
  every group on the instance offline at once. The member **vote count** is why a down member matters or does
  not, which is why it has its own column. An empty tab is normal for `CLUSTER_TYPE = NONE`.
- `sys.dm_hadr_availability_replica_cluster_nodes` supplies `join_state_desc`, which the replica-state DMVs do
  not carry — a replica can look configured and not be joined to the WSFC group at all. It joins on the group
  *name*, not an id, and a replica on an FCI has **one row per possible owner node**, so `AgClusterNodeRow.Key`
  includes the node name.
- **A listener IP that is OFFLINE is a client-facing outage with every replica reporting HEALTHY.**
  `sys.availability_group_listener_ip_addresses.state` has no `*_desc` companion, so the mapping lives in
  `AgListenerRow.StateDescription` — and it is **0 = offline, 1 = online**, 2 online-pending, 3 failed. That is
  the opposite way round to nearly every other state column in these DMVs; it shipped transposed once, which
  painted every healthy listener IP red and raised a Critical finding against it. One row per IP, not per listener, because multi-subnet listeners' IPs go
  offline independently — and in a multi-subnet listener only the primary's subnet IP *should* be online.
- Read-only routing is here because the classic failure is silent: a routing target with no
  `read_only_routing_url` is in the list and can never receive a routed connection.
- **A group with no listener is Information, not a warning.** A listener is normal but not required, and groups
  reached by node name on purpose are common; nothing is broken at the moment the check runs. It still says what
  it costs (a failover breaks clients, read-intent routing needs one), and being informational keeps the verdict
  strip on "Healthy" and lets "Problems only" hide it.
