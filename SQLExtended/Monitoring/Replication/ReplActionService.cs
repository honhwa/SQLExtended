using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace SQLExtended.Monitoring.Replication;

/// <summary>
/// The only writing code in the replication subsystem: posting a tracer token.
///
/// It is here because a tracer token is the one thing that answers "is replication working right now" without
/// inference. Every other number on this dashboard is a reading of what already happened — the token is a real
/// transaction written into the publication, timed as the log reader picks it up and again as each subscriber
/// commits it. A topology that looks idle because nothing has changed is indistinguishable from one that is
/// broken until you post one.
///
/// The rules match <c>JobActionService</c>, for the same reasons:
///  * <b>It runs in the publication database on the publisher.</b> sp_posttracertoken is a publication-database
///    procedure, so this only works when the connected instance is itself the publisher. The caller checks that
///    before offering the action rather than letting the server return a confusing "could not find stored
///    procedure" from the wrong database.
///  * <b>The caller confirms first, naming the publication and the server.</b> This window follows whatever query
///    window has focus, so the instance it points at is not always the one the user has in mind.
///  * <b>The server's own error is surfaced verbatim.</b> A permissions refusal or "the publication does not
///    exist" from SQL Server is more accurate than any pre-check this code could make.
///
/// Posting a token is cheap and non-destructive — it writes one command into the replication stream, which every
/// subscriber then applies as a no-op — but it is still a write, which is why it confirms.
/// </summary>
internal static class ReplActionService
{
    /// <summary>
    /// Posts a tracer token into <paramref name="publication"/>. The connection must be to the publisher; the
    /// publication database is set here rather than by the caller so the two cannot drift apart.
    /// </summary>
    public static async Task PostTracerTokenAsync(string baseConnectionString, string publisherDatabase, string publication, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(publisherDatabase)) throw new ArgumentException("A publication database is required.", nameof(publisherDatabase));
        if (string.IsNullOrWhiteSpace(publication)) throw new ArgumentException("A publication name is required.", nameof(publication));

        string connectionString = ReplQueryService.BuildMonitorConnectionString(baseConnectionString, publisherDatabase);

        using (var conn = SqlConnectionFactory.Create(connectionString))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);

            // Parameterised rather than interpolated: publication names allow characters that would otherwise
            // need escaping, and this is a write.
            using (var cmd = new SqlCommand("sys.sp_posttracertoken", conn) { CommandType = System.Data.CommandType.StoredProcedure, CommandTimeout = 30 })
            {
                cmd.Parameters.Add(new SqlParameter("@publication", System.Data.SqlDbType.NVarChar, 128) { Value = publication });
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Whether a tracer token can be posted for a publication from this connection: the publisher named on the
    /// publication has to be the instance we are connected to.
    ///
    /// Compared loosely, because the publisher is recorded as a linked-server name and the local instance reports
    /// SERVERPROPERTY('ServerName') — for a named instance those match, but a publisher registered by an alias or
    /// with only its host name does not. When they do not match this returns false and the UI says to connect to
    /// the publisher, which is honest; guessing and failing at the procedure call is not.
    /// </summary>
    public static bool CanPostFrom(string localServerName, string publisher)
    {
        if (string.IsNullOrWhiteSpace(localServerName) || string.IsNullOrWhiteSpace(publisher)) return false;
        if (string.Equals(localServerName, publisher, StringComparison.OrdinalIgnoreCase)) return true;

        // A default instance is sometimes recorded as HOST and sometimes as HOST\MSSQLSERVER.
        return string.Equals(Bare(localServerName), Bare(publisher), StringComparison.OrdinalIgnoreCase);
    }

    private static string Bare(string serverName)
    {
        int slash = serverName.IndexOf('\\');
        if (slash < 0) return serverName;

        string instance = serverName.Substring(slash + 1);
        return string.Equals(instance, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ? serverName.Substring(0, slash) : serverName;
    }
}
