using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace SQLExtended;

/// <summary>
/// Keeps the Entra (Azure AD) access token SSMS is already holding for a server, so this extension can reuse it.
///
/// <para>Everything here reconnects from a harvested connection string alone, and a connection string cannot spell
/// an Entra sign-in: it can carry Windows auth or a SQL login's password and nothing else. Until this existed every
/// Entra connection was harvested as integrated security, and Azure SQL Database answered every one of them with
/// <c>Windows logins are not supported in this version of SQL Server</c> (error 40607) on a background thread —
/// the schema cache, IntelliSense and the dashboards all failed together, naming nothing about the cause.</para>
///
/// <para>The token itself comes from <c>UIConnectionInfo.RenewableToken</c>, an SSMS-internal
/// <c>IRenewableToken</c>. It is not put in the connection string (there is no keyword for it) — the string is
/// left credential-free and <see cref="SqlConnectionFactory"/> attaches the token to the
/// <c>SqlConnection.AccessToken</c> property instead, keyed by server.</para>
///
/// <para><b>Refreshing is deliberately biased to the harvest.</b> <see cref="Remember"/> runs on the UI thread
/// while SSMS's own connection is in hand, which is where a token renewal may legitimately prompt; the connection
/// opens happen on background threads, where a prompt would hang a poll timer with no window to show it against.
/// So the cached string is refreshed eagerly whenever a harvest sees it within <see cref="RefreshWindow"/> of
/// expiry, and the open path only falls back to renewing if it finds nothing usable.</para>
/// </summary>
internal static class EntraTokenBroker
{
    /// <summary>How close to expiry a cached token may get before a harvest renews it.</summary>
    private static readonly TimeSpan RefreshWindow = TimeSpan.FromMinutes(5);

    private sealed class Entry
    {
        public object RenewableToken;
        public string Token;
        public DateTimeOffset Expiry;
    }

    private static readonly ConcurrentDictionary<string, Entry> Servers = new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes a server name to the key both sides of this class agree on.
    ///
    /// <para>The same server arrives written several ways — <c>tcp:host,1433</c> from the connection dialog,
    /// <c>host</c> from Object Explorer, <c>ADMIN:host</c> from a DAC window — and a key that told them apart
    /// would silently lose the token for a window that has one.</para>
    /// </summary>
    public static string ServerKey(string dataSource)
    {
        // The ADMIN: strip mirrors ConnectionHelper.NormalizeHarvestedDataSource, repeated rather than called so
        // this class stays free of the VS assemblies and can be linked into the test project.
        string source = (dataSource ?? "").Trim();
        if (source.StartsWith("ADMIN:", StringComparison.OrdinalIgnoreCase))
            source = source.Substring("ADMIN:".Length).Trim();

        int protocol = source.IndexOf(':');
        if (protocol > 0 && protocol <= 4) // tcp:, np:, lpc: — never a drive letter, server names are not paths
            source = source.Substring(protocol + 1);

        int port = source.LastIndexOf(',');
        if (port > 0)
            source = source.Substring(0, port);

        return source.Trim().TrimEnd('.').ToLowerInvariant();
    }

    /// <summary>
    /// Records the token object SSMS holds for a server and refreshes the cached string if it is near expiry.
    /// Call from the UI thread, during a harvest.
    /// </summary>
    public static void Remember(string dataSource, object renewableToken)
    {
        string key = ServerKey(dataSource);
        if (string.IsNullOrEmpty(key) || renewableToken == null)
            return;

        var entry = Servers.AddOrUpdate(
            key,
            _ => new Entry { RenewableToken = renewableToken },
            (_, existing) =>
            {
                existing.RenewableToken = renewableToken;
                return existing;
            }
        );

        Refresh(key, entry);
    }

    /// <summary>
    /// Drops the token for a server. Called when a harvest finds that server reconnected as a SQL login - a stale
    /// token would otherwise be attached to a connection that is asking to be someone else. Nothing forgets on a
    /// harvest that merely lacks a token: Object Explorer and the query window hand out different connection
    /// objects for the same server, and only one of them carries the token.
    /// </summary>
    public static void Forget(string dataSource)
    {
        string key = ServerKey(dataSource);
        if (!string.IsNullOrEmpty(key))
            Servers.TryRemove(key, out _);
    }

    /// <summary>True if a token has been harvested for this connection string's server.</summary>
    public static bool HasToken(string dataSource)
    {
        string key = ServerKey(dataSource);
        return !string.IsNullOrEmpty(key) && Servers.TryGetValue(key, out var entry) && !string.IsNullOrEmpty(entry.Token);
    }

    /// <summary>
    /// The access token for a server, or null if none was harvested. Safe to call from any thread; a renewal is
    /// only attempted here when the cached token has already expired, which the harvest normally prevents.
    /// </summary>
    public static string TryGetAccessToken(string dataSource)
    {
        string key = ServerKey(dataSource);
        if (string.IsNullOrEmpty(key) || !Servers.TryGetValue(key, out var entry))
            return null;

        if (string.IsNullOrEmpty(entry.Token) || entry.Expiry <= DateTimeOffset.UtcNow)
            Refresh(key, entry);

        return string.IsNullOrEmpty(entry.Token) ? null : entry.Token;
    }

    /// <summary>
    /// Pulls a fresh string out of the <c>IRenewableToken</c> when the cached one is inside
    /// <see cref="RefreshWindow"/> of expiry. A failed renewal keeps whatever is cached rather than clearing it:
    /// a token with minutes left on it still opens connections, and dropping it would turn a transient failure
    /// into the 40607 login error this class exists to prevent.
    /// </summary>
    private static void Refresh(string key, Entry entry)
    {
        if (!string.IsNullOrEmpty(entry.Token) && entry.Expiry > DateTimeOffset.UtcNow.Add(RefreshWindow))
            return;

        object token = entry.RenewableToken;
        if (token == null)
            return;

        try
        {
            // IRenewableToken is internal to SSMS and may be implemented explicitly, so the members are resolved
            // through the interface rather than by name on the concrete type.
            var contract = token.GetType().GetInterfaces().FirstOrDefault(i => i.Name == "IRenewableToken");
            if (contract == null)
                return;

            string access = contract.GetMethod("GetAccessToken", BindingFlags.Instance | BindingFlags.Public)?.Invoke(token, null) as string;
            if (string.IsNullOrEmpty(access))
                return;

            bool first = string.IsNullOrEmpty(entry.Token);
            entry.Token = access;
            entry.Expiry = contract.GetProperty("TokenExpiry", BindingFlags.Instance | BindingFlags.Public)?.GetValue(token) is DateTimeOffset expiry
                ? expiry
                : DateTimeOffset.UtcNow.AddMinutes(30); // SSMS's own tokens run an hour; half of that is a safe guess

            if (first)
                Diagnostics.SQLExtendedLog.Info("Connection", $"Reusing SSMS's Entra access token for {key} (expires {entry.Expiry:u}).");
        }
        catch (Exception ex)
        {
            Diagnostics.SQLExtendedLog.Warning("Connection", $"Could not renew the Entra access token for {key}; the cached one will be used until it expires", ex);
        }
    }
}
