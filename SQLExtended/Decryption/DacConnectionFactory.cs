using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Text;

namespace SQLExtended.Decryption;

/// <summary>
/// Opens a Dedicated Administrator Connection. <c>sys.sysobjvalues</c>, where encrypted module text lives,
/// is readable over the DAC and nowhere else, so every part of this feature goes through here.
/// </summary>
internal static class DacConnectionFactory
{
    /// <summary>
    /// Builds the DAC form of a connection string. Three of these settings are not preferences:
    ///
    /// <list type="bullet">
    /// <item>The <c>ADMIN:</c> prefix is what asks for the DAC at all.</item>
    /// <item>Pooling is off. An instance permits exactly one DAC at a time, and a pooled connection stays
    /// open after <c>Dispose</c> — the next attempt, ours or SSMS's own "New Query (DAC)", would be
    /// refused by a connection nobody is using.</item>
    /// <item>Any explicit port is dropped. The DAC listens on its own port, which SQL Browser resolves from
    /// the instance name; carrying over <c>,1433</c> would point the connection at the normal endpoint and
    /// it would not be a DAC at all.</item>
    /// </list>
    /// </summary>
    public static string BuildConnectionString(string baseConnectionString, string database)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            Pooling = false,
            MultipleActiveResultSets = false,
            ConnectTimeout = 10,
            ApplicationName = "SQLExtended for SSMS (DAC)",
        };

        if (!string.IsNullOrEmpty(database))
            builder.InitialCatalog = database;

        builder.DataSource = "ADMIN:" + StripDacUnsupportedParts(builder.DataSource);
        return builder.ConnectionString;
    }

    /// <summary>
    /// The DAC reached by address rather than by name, for the case where the client cannot work out its
    /// port. <b>No <c>ADMIN:</c> prefix here</b> — and that is measured, not assumed:
    ///
    /// <list type="bullet">
    /// <item><c>ADMIN:host,port</c> is rejected outright by SqlClient with provider error 25,
    /// "Connection string is not valid". The prefix only accepts an instance name.</item>
    /// <item>A plain <c>tcp:host,port</c> pointed at the DAC's port lands on the DAC endpoint anyway. The
    /// prefix is a way of <i>finding</i> the port through SQL Browser, not a flag the server requires: on a
    /// SQL Server 2019 Express instance whose DAC was listening on 11618, this form produced a session whose
    /// <c>sys.endpoints</c> row was 'Dedicated Admin Connection' and which could read
    /// <c>sys.sysobjvalues</c>.</item>
    /// </list>
    /// </summary>
    public static string BuildDirectConnectionString(string baseConnectionString, string database, int dacPort)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            Pooling = false,
            MultipleActiveResultSets = false,

            // Only reached after a failure, so this timeout is the user's wait on an error message rather
            // than on a normal connect. A wrong port is refused immediately when it is local; five seconds
            // covers the remote case where a firewall drops the SYN and nothing ever answers.
            ConnectTimeout = 5,
            ApplicationName = "SQLExtended for SSMS (DAC)",
        };

        if (!string.IsNullOrEmpty(database))
            builder.InitialCatalog = database;

        builder.DataSource = "tcp:" + StripToHost(builder.DataSource) + "," + dacPort;
        return builder.ConnectionString;
    }

    /// <summary>
    /// Reduces a data source to the host (and instance) the DAC needs: an existing <c>ADMIN:</c> prefix is
    /// not doubled, a protocol prefix (<c>tcp:</c>, <c>np:</c>) is dropped because the DAC is TCP, and a
    /// trailing <c>,port</c> goes for the reason above.
    /// </summary>
    public static string StripDacUnsupportedParts(string dataSource)
    {
        string source = (dataSource ?? "").Trim();

        if (source.StartsWith("ADMIN:", StringComparison.OrdinalIgnoreCase))
            source = source.Substring("ADMIN:".Length);

        foreach (string prefix in new[] { "tcp:", "np:", "lpc:" })
        {
            if (source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                source = source.Substring(prefix.Length);
                break;
            }
        }

        int comma = source.IndexOf(',');
        if (comma >= 0) source = source.Substring(0, comma);

        return source.Trim();
    }

    /// <summary>
    /// Just the machine, with no instance name. Used only alongside an explicit port, where the port already
    /// identifies the instance and the name would only invite the client to resolve it — which is exactly
    /// what just failed.
    /// </summary>
    public static string StripToHost(string dataSource)
    {
        string source = StripDacUnsupportedParts(dataSource);

        int slash = source.IndexOf('\\');
        if (slash >= 0) source = source.Substring(0, slash);

        return source.Trim();
    }

    /// <summary>
    /// Opens a DAC to <paramref name="database"/>. Throws <see cref="DacUnavailableException"/> with
    /// something a DBA can act on — the raw SqlException for a refused DAC names neither the DAC nor the
    /// two settings that most often explain it.
    /// </summary>
    /// <param name="probeHolder">
    /// Whether to find out who is holding the slot when it is busy, which costs one extra connection on the
    /// normal endpoint. The caller passes false while it still intends to retry and true on the attempt
    /// whose failure is the one the user will read — the answer is only worth a round trip once.
    /// </param>
    public static SqlConnection Open(string baseConnectionString, string database, bool probeHolder = true)
    {
        if (string.IsNullOrEmpty(baseConnectionString))
            throw new DacUnavailableException("No connection is available to open an administrator connection on.");

        string dacConnectionString;
        try
        {
            dacConnectionString = BuildConnectionString(baseConnectionString, database);
        }
        catch (Exception ex)
        {
            throw new DacUnavailableException("The active connection string could not be turned into an administrator connection: " + ex.Message, ex);
        }

        try
        {
            return Connect(dacConnectionString);
        }
        catch (Exception ex)
        {
            // A busy slot and a refused login are answered precisely already, and neither is a problem the
            // server could help with. Everything else may be a client that could not work out where the DAC
            // lives — which the server itself can tell us, by listing what it is listening on.
            if (IsBusy(ex) || IsLoginRefused(ex))
                throw new DacUnavailableException(Explain(ex, baseConnectionString, probeHolder), ex, IsLoginRefused(ex));

            return OpenOnListeningPort(ex, baseConnectionString, database, probeHolder);
        }
    }

    private static SqlConnection Connect(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Second opinion for a failed connect: ask the server which ports it is listening on and try each one
    /// until a session lands on the DAC endpoint. This is what turns the error-43 dead end — "An error
    /// occurred while obtaining the dedicated administrator connection (DAC) port. Make sure that SQL
    /// Browser is running" — into a working connection, without SQL Browser being installed at all.
    ///
    /// The port cannot be read from a catalog view: <c>sys.tcp_endpoints</c> reports <c>port = 0</c> for a
    /// dynamically assigned DAC port even while the listener is up (measured), and it reports the normal
    /// endpoint the same way. So the candidates come from <c>sys.dm_tcp_listener_states</c>, and the answer
    /// is confirmed by asking the session which endpoint it landed on rather than by trusting the port.
    ///
    /// If the ordinary connection cannot be made either, there is nothing to learn and the original failure
    /// is the honest answer.
    /// </summary>
    private static SqlConnection OpenOnListeningPort(Exception failure, string baseConnectionString, string database, bool probeHolder)
    {
        DacEndpoint endpoint = ProbeDacEndpoint(baseConnectionString);

        if (!endpoint.Reached)
            throw new DacUnavailableException(Explain(failure, baseConnectionString, probeHolder), failure);

        foreach (int port in endpoint.Ports)
        {
            SqlConnection candidate = null;
            try
            {
                candidate = Connect(BuildDirectConnectionString(baseConnectionString, database, port));
                if (IsDacSession(candidate)) return candidate;

                // The instance's normal endpoint answers too, so a connection is not proof of anything —
                // only the endpoint it landed on is. Disposed and tried against the next candidate.
                candidate.Dispose();
            }
            catch
            {
                candidate?.Dispose();
            }
        }

        throw new DacUnavailableException(ExplainUnresolvedPort(failure, endpoint), failure, endpoint.HasNoDac);
    }

    /// <summary>
    /// Whether this connection is on the DAC endpoint. The only trustworthy test: any port the instance
    /// listens on will accept a connection, and the difference only shows in the endpoint it lands on.
    /// </summary>
    private static bool IsDacSession(SqlConnection connection)
    {
        const string sql = @"
SELECT CASE WHEN EXISTS (
    SELECT 1
    FROM sys.dm_exec_sessions s
    JOIN sys.endpoints e ON s.endpoint_id = e.endpoint_id
    WHERE s.session_id = @@SPID AND e.name = 'Dedicated Admin Connection')
THEN 1 ELSE 0 END;";

        try
        {
            using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 5 };
            return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True when the failure is the instance's one DAC slot being <i>occupied</i>, not the DAC being
    /// unusable. The two need opposite handling: a busy slot usually frees itself within seconds, so the
    /// caller may try again; a login that is not sysadmin, an instance with remote admin connections off,
    /// or Azure SQL Database will never start working mid-session, and retrying only costs a connect
    /// timeout per object per refresh.
    ///
    /// Only <see cref="SqlException"/> is inspected, never the message of whatever wrapper it arrives in.
    /// <see cref="DacUnavailableException"/>'s own text names the dedicated administrator connection — the
    /// very phrase this looks for — so walking the whole exception chain would classify every failure,
    /// including "not sysadmin", as busy and turn a permanent answer into an endless retry.
    ///
    /// The error <i>number</i> is matched as well as the text, because the text comes from the server and
    /// is localized: a Chinese or German instance says the same thing in words that never match, but 17810
    /// is 17810 in every language.
    /// </summary>
    public static bool IsBusy(Exception ex)
    {
        for (Exception current = ex; current != null; current = current.InnerException)
        {
            if (current is SqlException sql && (sql.Number == 17810 || sql.Number == 17809 || LooksLikeBusyText(sql.Message)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The text half of <see cref="IsBusy"/>, split out because it is the part that can be tested without a
    /// server. Both phrasings the engine uses are matched: the DAC one ("the maximum number of 'Dedicated
    /// Admin Connection' connections has already been reached") and the shorter login one.
    /// </summary>
    internal static bool LooksLikeBusyText(string message)
    {
        if (string.IsNullOrEmpty(message)) return false;

        if (message.IndexOf("maximum number of", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return message.IndexOf("dedicated admin", StringComparison.OrdinalIgnoreCase) >= 0
            && message.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// True when the DAC refused the login outright — the login is not sysadmin, or it is disabled. Unlike
    /// a transport failure this cannot start working while the session lasts, so the caller caches it for
    /// the session instead of retrying it.
    /// </summary>
    public static bool IsLoginRefused(Exception ex)
    {
        for (Exception current = ex; current != null; current = current.InnerException)
        {
            if (current is SqlException sql && (sql.Number == 18456 || sql.Number == 18452 || sql.Number == 297))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Names whoever is holding the DAC, over an ordinary connection. Only called after a busy failure,
    /// because that is the one case where the answer changes what the user does — close that ADMIN: query
    /// window, or wait for the tool that polls it.
    ///
    /// Best effort by design: a login without VIEW SERVER STATE, a network that just failed, or a row that
    /// is already gone all return null, and the caller then falls back to printing the query to run by hand.
    /// It must never throw — it runs while another exception is being explained.
    /// </summary>
    public static string DescribeDacHolder(string baseConnectionString)
    {
        const string sql = @"
SELECT TOP (5) s.login_name, s.program_name, s.host_name
FROM sys.dm_exec_sessions s
JOIN sys.endpoints e ON s.endpoint_id = e.endpoint_id
WHERE e.name = 'Dedicated Admin Connection' AND s.session_id <> @@SPID
ORDER BY s.session_id;";

        try
        {
            if (string.IsNullOrEmpty(baseConnectionString)) return null;

            var builder = new SqlConnectionStringBuilder(baseConnectionString)
            {
                ConnectTimeout = 5,
                ApplicationName = "SQLExtended for SSMS (DAC probe)",
            };

            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();

            using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 5 };
            using var reader = cmd.ExecuteReader();

            var holders = new List<string>();
            while (reader.Read() && holders.Count < 5)
            {
                string login = reader.IsDBNull(0) ? "?" : reader.GetString(0);
                string program = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                string host = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();

                holders.Add(login
                            + (program.Length == 0 ? "" : $" via \"{program}\"")
                            + (host.Length == 0 ? "" : " on " + host));
            }

            if (holders.Count == 0)
            {
                // Not the same as "nobody has it": the row can be gone a moment before the socket is
                // released, which is exactly the state a retry fixes.
                return "No DAC session is visible at this instant, so the slot may have just been released — try again.";
            }

            return "Holding it now: " + string.Join("; ", holders) + ".";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads everything the server can say about its own DAC, over the ordinary connection: whether one
    /// exists here at all, which ports it could be listening on, and whether it accepts remote clients.
    ///
    /// Every query is independently optional. This runs while a failure is being explained, so a missing
    /// permission on one of them must cost that one fact, not the diagnosis.
    /// </summary>
    public static DacEndpoint ProbeDacEndpoint(string baseConnectionString)
    {
        var endpoint = new DacEndpoint();
        if (string.IsNullOrEmpty(baseConnectionString))
        {
            endpoint.Error = "no connection string was available";
            return endpoint;
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(baseConnectionString)
            {
                // Three seconds, not ten: this runs only after a DAC attempt has already failed, and the
                // case it exists for — the server is up, the client just cannot find the DAC — answers in
                // milliseconds. A server that is genuinely unreachable costs the user three seconds on top
                // of the failure they were already getting, not a second full timeout.
                ConnectTimeout = 3,
                ApplicationName = "SQLExtended for SSMS (DAC port probe)",
            };

            // The address that was tried, so the message can print it instead of describing it.
            endpoint.Host = StripToHost(builder.DataSource);

            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            endpoint.Reached = true;

            endpoint.EngineEdition = Scalar(connection,
                "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int);") ?? 0;

            endpoint.RemoteAdminEnabled = Scalar(connection,
                "SELECT CAST(value_in_use AS int) FROM sys.configurations WHERE name = 'remote admin connections';") == 1;

            string machine = ScalarText(connection, "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128));");
            if (!string.IsNullOrEmpty(machine))
                endpoint.ClientIsRemote = !string.Equals(machine, Environment.MachineName, StringComparison.OrdinalIgnoreCase);

            // Not a source of the answer, but a source of one candidate: it is correct on instances whose
            // DAC port is fixed, and 0 — measured — on the ones where it is dynamic.
            int declared = Scalar(connection,
                "SELECT port FROM sys.tcp_endpoints WHERE name = 'Dedicated Admin Connection';") ?? 0;
            endpoint.AddPort(declared);

            // The real candidate list. Every TDS listener the instance has, DAC included, and nothing in
            // this view says which is which — that is settled by connecting and asking, see IsDacSession.
            foreach (int port in ScalarList(connection,
                "SELECT DISTINCT port FROM sys.dm_tcp_listener_states WHERE state_desc = 'ONLINE';"))
            {
                endpoint.AddPort(port);
            }

            if (endpoint.Ports.Count == 0)
            {
                // The DAC listens on TCP and nothing else, so an instance whose TCP protocol is switched off
                // is a second, independent reason for there to be no port at all — worth knowing before
                // someone restarts the service with trace flag 7806 and finds it still missing.
                endpoint.TcpEnabled = Scalar(connection,
                    "SELECT TOP (1) CAST(value_data AS int) FROM sys.dm_server_registry " +
                    "WHERE registry_key LIKE '%SuperSocketNetLib\\Tcp' AND value_name = 'Enabled';") == 1;
            }
        }
        catch (Exception ex)
        {
            endpoint.Error = ex.Message;
        }

        return endpoint;
    }

    private static int? Scalar(SqlConnection connection, string sql)
    {
        try
        {
            using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 5 };
            object value = cmd.ExecuteScalar();
            if (value == null || value == DBNull.Value) return null;

            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static string ScalarText(SqlConnection connection, string sql)
    {
        try
        {
            using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 5 };
            object value = cmd.ExecuteScalar();
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }
        catch
        {
            return null;
        }
    }

    private static List<int> ScalarList(SqlConnection connection, string sql)
    {
        var values = new List<int>();
        try
        {
            using var cmd = new SqlCommand(sql, connection) { CommandTimeout = 5 };
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0)) values.Add(Convert.ToInt32(reader.GetValue(0)));
            }
        }
        catch
        {
            // An older build without sys.dm_tcp_listener_states, or no VIEW SERVER STATE: the declared
            // port above is all we get, and the message says what that means.
        }

        return values;
    }

    /// <summary>
    /// Turns the failures that actually happen into their cause. Everything else is passed through with the
    /// DAC named, so at least the reader knows which connection failed.
    /// </summary>
    private static string Explain(Exception ex, string baseConnectionString, bool probeHolder)
    {
        string message = ex.Message ?? "";

        if (LooksLikeBusyText(message))
        {
            // Naming the holder turns a dead end into an instruction: the slot is usually held by an
            // ADMIN: query window the user forgot, or by a tool polling it, and both are visible in
            // sys.dm_exec_sessions. When the probe cannot say, the query to run by hand is still printed.
            string holder = probeHolder ? DescribeDacHolder(baseConnectionString) : null;

            return "The server's single dedicated administrator connection is already in use, and it is the only way to "
                 + "read encrypted module text. Close that session and try again. "
                 + (holder ?? "To see who has it: SELECT s.session_id, s.login_name, s.program_name, s.host_name FROM "
                            + "sys.dm_exec_sessions s JOIN sys.endpoints e ON s.endpoint_id = e.endpoint_id "
                            + "WHERE e.name = 'Dedicated Admin Connection';");
        }

        if (IsLoginRefused(ex))
        {
            return "The administrator connection was refused: decrypting module text requires membership of the sysadmin server role. "
                 + "(" + message + ")";
        }

        return "Could not open a dedicated administrator connection. Decrypting module text needs one, it requires sysadmin, and "
             + "connecting to a remote instance also needs \"remote admin connections\" enabled on that server. Azure SQL Database "
             + "accepts a DAC only into a user database, never into master, and a managed instance only from inside its own virtual "
             + "network, on port 1434. (" + message + ")";
    }

    /// <summary>
    /// The error-43 message, with the cause filled in from what the server said about itself.
    ///
    /// The raw text is useless on its own: it names SQL Browser, which is neither installed nor needed on
    /// Express, and says nothing about the two causes that are actually common — no listener at all because
    /// Express needs trace flag 7806, and a client that has no way to ask which port it got.
    /// </summary>
    private static string ExplainUnresolvedPort(Exception failure, DacEndpoint endpoint)
    {
        // The port was found and tried, and the answer was "you are not sysadmin" — that is the whole
        // diagnosis, and burying it under the port story would hide the one thing to fix.
        if (IsLoginRefused(failure))
            return Explain(failure, null, false);

        var text = new StringBuilder();

        text.Append("Could not reach the dedicated administrator connection. The client could not work out which port it "
                  + "listens on (error 43 — that message names SQL Browser, which is only how a client asks), and ");

        if (endpoint.Ports.Count == 0)
        {
            text.Append("the server is not listening on any TCP port at all, so there is nothing to connect to. ");
        }
        else
        {
            text.Append("none of the ports the server is listening on (")
                .Append(string.Join(", ", endpoint.Ports))
                .Append(") turned out to be it. ");
        }

        if (endpoint.EngineEdition == 4)
        {
            text.Append("This instance is SQL Server Express, which does not start a DAC listener at all unless the "
                      + "service is started with trace flag 7806 — the instance's ERRORLOG says so in as many words when "
                      + "it starts. That flag, plus a restart, is the only way to decrypt on Express. ");
        }

        if (endpoint.TcpEnabled == false)
        {
            text.Append("TCP/IP is switched off on this instance as well, and the DAC listens on TCP only. ");
        }

        if (endpoint.RemoteAdminEnabled == false && endpoint.ClientIsRemote == true)
        {
            text.Append("And \"remote admin connections\" is 0, so even a listener would accept loopback clients only: run "
                      + "sp_configure 'remote admin connections', 1; RECONFIGURE; or decrypt from the server itself. ");
        }

        if (endpoint.EngineEdition != 4)
        {
            text.Append("The instance's ERRORLOG names the port when the listener starts (\"Dedicated admin connection "
                      + "support was established ... on port <n>\"); no such line means it never started, and the line "
                      + "above it says why. ");
        }

        text.Append("SoluitionDocs/Queries/dac-availability-check.sql reports all of this from the instance itself. ");
        text.Append("Decrypting module text needs the DAC — no other connection can read sys.sysobjvalues. ");
        text.Append('(').Append(failure.Message ?? "").Append(')');

        return text.ToString();
    }
}

/// <summary>
/// What a server says about its own DAC. Every field is best effort: <see cref="Reached"/> is the only one
/// that means anything on its own.
/// </summary>
internal sealed class DacEndpoint
{
    private readonly List<int> _ports = new();

    /// <summary>The ordinary connection opened, so the rest of the fields are answers rather than guesses.</summary>
    public bool Reached { get; set; }

    /// <summary>
    /// Ports worth trying, in order. Never a claim that one of them is the DAC: the instance's normal
    /// endpoint is in here too, and only connecting and asking the session settles it.
    /// </summary>
    public IReadOnlyList<int> Ports => _ports;

    /// <summary><c>remote admin connections</c>. Null when the setting could not be read.</summary>
    public bool? RemoteAdminEnabled { get; set; }

    /// <summary>Whether this client is on a different machine from the server. Null when unknown.</summary>
    public bool? ClientIsRemote { get; set; }

    /// <summary>Whether the instance's TCP protocol is enabled. Null when unknown. Only read when no port was found.</summary>
    public bool? TcpEnabled { get; set; }

    /// <summary><c>SERVERPROPERTY('EngineEdition')</c>, or 0. 4 is Express, 5 Azure SQL Database.</summary>
    public int EngineEdition { get; set; }

    /// <summary>The server's host name, for the message that names the address that was tried.</summary>
    public string Host { get; set; }

    /// <summary>Why the probe failed, when it did.</summary>
    public string Error { get; set; }

    /// <summary>Azure SQL Database: a DAC exists, but only into a named user database — never into master.</summary>
    public bool HasNoDac => EngineEdition == 5;

    internal void AddPort(int port)
    {
        if (port > 0 && !_ports.Contains(port)) _ports.Add(port);
    }
}

/// <summary>
/// A DAC could not be opened. Separate from the per-object failures because it stops the whole run rather
/// than one object, and the caller reports it once instead of once per module.
/// </summary>
internal sealed class DacUnavailableException : Exception
{
    /// <summary>
    /// True when the answer cannot change while the session lasts — a login that is not sysadmin, or a
    /// platform that has no DAC to offer. Those are worth remembering for the session; a port that could
    /// not be resolved is not, because starting SQL Browser or turning on remote admin connections fixes it
    /// without restarting anything.
    /// </summary>
    public bool Permanent { get; }

    public DacUnavailableException(string message, Exception inner = null, bool permanent = false) : base(message, inner)
    {
        Permanent = permanent;
    }
}
