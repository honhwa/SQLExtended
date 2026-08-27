/*
    Always On — is a seeding failure current, or is it history?
    ----------------------------------------------------------
    Run on the PRIMARY (or any replica; sections 1 and 2 are cluster-wide).

    Why this exists: sys.dm_hadr_automatic_seeding is a HISTORY table, not a current-state view. It holds one row
    per seeding attempt for the life of the availability group, and it is memory-resident, so nothing but an
    instance restart clears it. A seed that failed once and succeeded on the retry leaves BOTH rows in place —
    so a failed row on its own says nothing about whether the database is protected right now.

    sys.dm_hadr_physical_seeding_stats behaves the same way for the transfer itself: rows for finished transfers
    stay listed, and end_time_utc is the only thing separating one still running from one that has completed.

    The Always On dashboard's Diagnostics tab now judges only the newest attempt per database and demotes it to
    Information once the database is joined on every secondary. These queries are how to confirm that by hand.
*/

-- ---------------------------------------------------------------------------------------------------------------
-- 1. Every seeding attempt, newest first per database.
--
--    Read it like this:
--      * newest row COMPLETED / performed_seeding = 1  -> any older FAILED row is history, nothing to do
--      * newest row FAILED, nothing newer              -> a real failure; section 2 will show the database
--                                                         not joined on the target replica
--      * newest row SEEDING                            -> still running; watch section 3
--
--    failure_state_desc reads NO_FAILURE when the attempt has not failed. The DMV names no replica, which is why
--    section 2 is needed to tell which secondary an attempt was for.
-- ---------------------------------------------------------------------------------------------------------------
SELECT
    ag.name                  AS ag_name,
    d.database_name,
    s.start_time,
    s.completion_time,
    s.current_state,
    s.performed_seeding,
    s.is_source,
    s.failure_state,
    s.failure_state_desc,
    s.error_code,
    s.number_of_attempts
FROM sys.dm_hadr_automatic_seeding AS s
LEFT JOIN sys.availability_groups AS ag ON ag.group_id = s.ag_id
-- dm_hadr_database_replica_cluster_states is keyed (replica_id, group_database_id), so a plain join would fan each
-- seeding row out once per replica. TOP (1) just resolves the name.
OUTER APPLY (
    SELECT TOP (1) dbcs.database_name
    FROM sys.dm_hadr_database_replica_cluster_states AS dbcs
    WHERE dbcs.group_database_id = s.ag_db_id
) AS d
ORDER BY d.database_name, s.start_time DESC;

-- ---------------------------------------------------------------------------------------------------------------
-- 2. What the databases are actually doing now — the evidence a past failure has been made good.
--
--    is_database_joined = 1 on every replica, with SYNCHRONIZED or SYNCHRONIZING and HEALTHY, means the seed
--    landed. A database can be in the group's configuration and NOT joined on a replica, in which case it is not
--    being protected there at all and no synchronization column says so — which is the case a seeding warning
--    should be reporting.
-- ---------------------------------------------------------------------------------------------------------------
SELECT
    ag.name                          AS ag_name,
    dc.database_name,
    ar.replica_server_name,
    ar.availability_mode_desc,
    dc.is_database_joined,
    dc.is_failover_ready,
    drs.synchronization_state_desc,
    drs.synchronization_health_desc,
    drs.database_state_desc,
    drs.is_suspended,
    drs.suspend_reason_desc
FROM sys.dm_hadr_database_replica_cluster_states AS dc
JOIN sys.availability_replicas AS ar ON ar.replica_id = dc.replica_id
JOIN sys.availability_groups  AS ag ON ag.group_id  = ar.group_id
LEFT JOIN sys.dm_hadr_database_replica_states AS drs
       ON drs.replica_id = dc.replica_id
      AND drs.group_database_id = dc.group_database_id
ORDER BY dc.database_name, ar.replica_server_name;

-- ---------------------------------------------------------------------------------------------------------------
-- 3. Physical seeding transfers, running and recent.
--
--    end_time_utc IS NULL  -> still running; percent_complete should keep advancing
--    end_time_utc IS NOT NULL -> finished, so a failure_message on it is the record of an attempt, not a live fault
--
--    The wait columns say where a slow seed is losing its time: total_network_wait_time_ms against
--    total_disk_io_wait_time_ms is network versus target storage.
-- ---------------------------------------------------------------------------------------------------------------
SELECT
    ps.local_database_name,
    ps.remote_machine_name,
    ps.role_desc,
    ps.internal_state_desc,
    ps.start_time_utc,
    ps.end_time_utc,
    ps.estimate_time_complete_utc,
    ps.transferred_size_bytes,
    ps.database_size_bytes,
    CASE WHEN ps.database_size_bytes > 0
         THEN CONVERT(DECIMAL(5, 1), ps.transferred_size_bytes * 100.0 / ps.database_size_bytes) END AS percent_complete,
    ps.transfer_rate_bytes_per_second,
    ps.total_disk_io_wait_time_ms,
    ps.total_network_wait_time_ms,
    ps.is_compression_enabled,
    ps.failure_message
FROM sys.dm_hadr_physical_seeding_stats AS ps
ORDER BY ps.start_time_utc DESC;

-- ---------------------------------------------------------------------------------------------------------------
-- 4. Whether automatic seeding is even configured on each replica, and who may create the database there.
--
--    Both are needed on the TARGET and are set in different places, which is why a seed can fail with the mode
--    already correct. The grant is run ON THE SECONDARY:
--
--        ALTER AVAILABILITY GROUP [<ag>] GRANT CREATE ANY DATABASE;
--
--    seeding_mode_desc exists from SQL Server 2016; on an older release this section returns no such column.
-- ---------------------------------------------------------------------------------------------------------------
SELECT
    ag.name AS ag_name,
    ar.replica_server_name,
    ar.availability_mode_desc,
    ar.failover_mode_desc,
    ar.seeding_mode_desc
FROM sys.availability_replicas AS ar
JOIN sys.availability_groups  AS ag ON ag.group_id = ar.group_id
ORDER BY ag.name, ar.replica_server_name;

-- Run on the SECONDARY to confirm the grant is in place. permission_name reads CREATE ANY DATABASE for the
-- availability group's own principal; no row means the grant was never made there.
SELECT
    ag.name AS ag_name,
    p.permission_name,
    p.state_desc
FROM sys.server_permissions AS p
JOIN sys.availability_groups AS ag ON ag.group_id = p.major_id
WHERE p.class_desc = 'AVAILABILITY GROUP';

-- ---------------------------------------------------------------------------------------------------------------
-- 5. What the engine logged about the seed, if sections 1-3 leave the cause unclear.
--
--    Seeding writes its own messages to the error log on both replicas — the target's log is usually the one that
--    names the path or the file it could not create. Run it on the replica that was being seeded.
-- ---------------------------------------------------------------------------------------------------------------
EXEC sys.xp_readerrorlog 0, 1, N'seeding';
EXEC sys.xp_readerrorlog 0, 1, N'Automatic seeding';
