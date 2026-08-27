using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SQLExtended.Monitoring.Jobs;

/// <summary>
/// The four state-changing operations the Jobs grid offers, each a thin call to the msdb stored procedure
/// SSMS itself uses. Everything else in this subsystem reads; this is the only file that writes.
///
/// Jobs are addressed by <c>@job_id</c> rather than <c>@job_name</c> throughout. Names are not unique across
/// master/target servers in a multiserver setup and can be renamed underneath a dashboard that polls every few
/// seconds, whereas the GUID the grid already holds is exactly the row the user clicked.
///
/// Permissions are a step up from the rest of the dashboard: reading needs SQLAgentReaderRole, but starting and
/// stopping need SQLAgentOperatorRole (or job ownership), and changing the enabled state needs ownership or
/// sysadmin. Nothing is pre-checked here — the server's own error is more accurate than any guess this code
/// could make, so it is allowed to surface verbatim.
/// </summary>
internal static class JobActionService
{
    /// <summary>
    /// Longer than the read timeout. sp_start_job and sp_stop_job signal Agent and return without waiting for
    /// the job, but sp_update_job takes a write lock that a busy Agent can hold briefly.
    /// </summary>
    private const int CommandTimeoutSeconds = 30;

    /// <summary>
    /// Starts a job. Agent runs it asynchronously, so this returns as soon as the request is accepted — it does
    /// not wait for the job to finish. Errors if the job is already running, which is the server telling the
    /// truth and is surfaced as-is.
    /// </summary>
    public static Task StartAsync(string connectionString, Guid jobId, CancellationToken ct) =>
        ExecuteAsync(connectionString, "msdb.dbo.sp_start_job", jobId, ct);

    /// <summary>Stops a running job. Errors if it is not running.</summary>
    public static Task StopAsync(string connectionString, Guid jobId, CancellationToken ct) =>
        ExecuteAsync(connectionString, "msdb.dbo.sp_stop_job", jobId, ct);

    /// <summary>
    /// Enables or disables a job. This only affects scheduled execution — a disabled job can still be started
    /// by hand, which is why <see cref="StartAsync"/> is offered regardless of the enabled state.
    /// </summary>
    public static Task SetEnabledAsync(string connectionString, Guid jobId, bool enabled, CancellationToken ct) =>
        ExecuteAsync(connectionString, "msdb.dbo.sp_update_job", jobId, ct, cmd =>
            cmd.Parameters.Add("@enabled", SqlDbType.TinyInt).Value = enabled ? 1 : 0);

    private static async Task ExecuteAsync(string connectionString, string procedure, Guid jobId, CancellationToken ct, Action<SqlCommand> addParameters = null)
    {
        using (var conn = new SqlConnection(connectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);

            using (var cmd = new SqlCommand(procedure, conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = CommandTimeoutSeconds })
            {
                cmd.Parameters.Add("@job_id", SqlDbType.UniqueIdentifier).Value = jobId;
                addParameters?.Invoke(cmd);

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>The equivalent T-SQL, for "Open as query" and for anyone who wants to script the action instead.</summary>
    public static string ScriptFor(JobAction action, Guid jobId, string jobName)
    {
        string header = $"-- {Describe(action)}: {jobName}" + Environment.NewLine;
        string id = "'" + jobId.ToString() + "'";

        switch (action)
        {
            case JobAction.Start: return header + $"EXEC msdb.dbo.sp_start_job @job_id = {id};";
            case JobAction.Stop: return header + $"EXEC msdb.dbo.sp_stop_job @job_id = {id};";
            case JobAction.Enable: return header + $"EXEC msdb.dbo.sp_update_job @job_id = {id}, @enabled = 1;";
            case JobAction.Disable: return header + $"EXEC msdb.dbo.sp_update_job @job_id = {id}, @enabled = 0;";
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    internal static string Describe(JobAction action)
    {
        switch (action)
        {
            case JobAction.Start: return "Run now";
            case JobAction.Stop: return "Stop";
            case JobAction.Enable: return "Enable";
            case JobAction.Disable: return "Disable";
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }
}

internal enum JobAction { Start, Stop, Enable, Disable }
