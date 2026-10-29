using Microsoft.Data.SqlClient;
using System;

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
    /// Opens a DAC to <paramref name="database"/>. Throws <see cref="DacUnavailableException"/> with
    /// something a DBA can act on — the raw SqlException for a refused DAC names neither the DAC nor the
    /// two settings that most often explain it.
    /// </summary>
    public static SqlConnection Open(string baseConnectionString, string database)
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

        var connection = new SqlConnection(dacConnectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch (Exception ex)
        {
            connection.Dispose();
            throw new DacUnavailableException(Explain(ex), ex, IsBusy(ex));
        }
    }

    /// <summary>
    /// Whether the refusal means "someone else is holding the DAC right now". This is the only DAC failure that
    /// is a moment rather than a fact about the server, and therefore the only one worth retrying or forgetting:
    /// a login without sysadmin stays that way, remote admin connections stay off until someone enables them,
    /// and Azure SQL Database has no DAC to enable. Treating those as transient costs three connect timeouts on
    /// every refresh; treating this one as permanent is what makes a single collision look like a broken feature
    /// for the rest of the session.
    /// </summary>
    internal static bool IsBusy(Exception ex)
        => (ex is SqlException sql && sql.Number == 17810) || LooksBusy(ex?.Message);

    /// <summary>
    /// 17810 reads "the maximum number of '1' dedicated administrator connections already exists". The text
    /// match is a backstop for the cases where the number is not on the exception handed to us. Deliberately
    /// not matched on "dedicated administrator connection" alone: Azure refuses with "Dedicated administrator
    /// connections are not supported", which is the opposite of busy and must not be retried.
    /// </summary>
    private static bool LooksBusy(string message)
        => !string.IsNullOrEmpty(message)
        && (message.IndexOf("maximum number of", StringComparison.OrdinalIgnoreCase) >= 0
            || (message.IndexOf("dedicated administrator", StringComparison.OrdinalIgnoreCase) >= 0
                && message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0));

    /// <summary>
    /// Turns the three failures that actually happen into their cause. Everything else is passed through
    /// with the DAC named, so at least the reader knows which connection failed. <c>internal</c> rather than
    /// private so the misrouting guarded here — a refusal that is not "busy" reported as "in use" — can be
    /// checked without a server.
    /// </summary>
    internal static string Explain(Exception ex)
    {
        string message = ex.Message ?? "";

        if (IsBusy(ex))
        {
            return "The server's single dedicated administrator connection is already in use. Close any other DAC session and "
                 + "try again — note that a pooled connection holds the slot with nothing visible in Object Explorer to show "
                 + "for it. To see who has it: SELECT s.session_id, s.login_name, s.program_name, s.host_name FROM "
                 + "sys.dm_exec_sessions s JOIN sys.endpoints e ON s.endpoint_id = e.endpoint_id WHERE e.name = "
                 + "'Dedicated Admin Connection';";
        }

        if (ex is SqlException sql && (sql.Number == 18456 || sql.Number == 297))
        {
            return "The administrator connection was refused: decrypting module text requires membership of the sysadmin server role. "
                 + "(" + message + ")";
        }

        return "Could not open a dedicated administrator connection. Decrypting module text needs one, it requires sysadmin, and "
             + "connecting to a remote instance also needs \"remote admin connections\" enabled on that server. Azure SQL Database "
             + "does not offer one at all. (" + message + ")";
    }
}

/// <summary>
/// A DAC could not be opened. Separate from the per-object failures because it stops the whole run rather
/// than one object, and the caller reports it once instead of once per module.
/// </summary>
internal sealed class DacUnavailableException : Exception
{
    public DacUnavailableException(string message, Exception inner = null, bool isBusy = false) : base(message, inner)
        => IsBusy = isBusy;

    /// <summary>
    /// Somebody else held the DAC at the moment we asked. Nothing about the server or the login is wrong, so
    /// the caller retries instead of remembering it — unlike every other reason a DAC cannot be opened.
    /// </summary>
    public bool IsBusy { get; }
}
