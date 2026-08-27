using System;
using System.Collections.Generic;
#if !SQLEXTENDED_TESTS
using System.Data.SqlClient;
using Microsoft.VisualStudio.Shell;
#endif
using System.Text.RegularExpressions;

namespace SQLExtended.Snippets;

/// <summary>
/// Resolves built-in placeholders in snippet bodies at insertion time.
/// Placeholders use the $name$ syntax (e.g., $date$, $dbname$).
/// </summary>
internal static class SnippetPlaceholderResolver
{
    private static readonly Regex PlaceholderPattern = new Regex(
        @"\$([a-zA-Z_][a-zA-Z0-9_]*)\$",
        RegexOptions.Compiled);

    /// <summary>
    /// Optional override for connection-dependent placeholders ($dbname$, $server$).
    /// When set, this is used instead of the cached SSMS connection info. Intended for tests.
    /// </summary>
    public static Func<(string DatabaseName, string ServerName)> ConnectionInfoProvider { get; set; }

    // Cached connection-derived placeholder values. Snippet resolution runs on both the UI thread
    // (insertion) and background threads (the completion list is built off-thread so a large-script
    // parse can't freeze SSMS). Reading the live values from SSMS requires the UI thread, so we
    // cache the last-known values and refresh them from UI-thread contexts (see
    // RefreshConnectionInfoFromSsms). Off-thread callers read these without any thread affinity.
    private static volatile string _cachedDatabaseName;
    private static volatile string _cachedServerName;

    /// <summary>
    /// All supported placeholder names and their descriptions.
    /// </summary>
    public static readonly IReadOnlyList<PlaceholderInfo> BuiltInPlaceholders = new List<PlaceholderInfo>
    {
        new PlaceholderInfo("date", "Current date (yyyy-MM-dd)"),
        new PlaceholderInfo("time", "Current time (HH:mm:ss)"),
        new PlaceholderInfo("datetime", "Current date and time (yyyy-MM-dd HH:mm:ss)"),
        new PlaceholderInfo("year", "Current year"),
        new PlaceholderInfo("month", "Current month (01-12)"),
        new PlaceholderInfo("day", "Current day of month (01-31)"),
        new PlaceholderInfo("user", "Current Windows username"),
        new PlaceholderInfo("machine", "Current machine name"),
        new PlaceholderInfo("dbname", "Current database name from active connection"),
        new PlaceholderInfo("server", "Current server name from active connection"),
        new PlaceholderInfo("guid", "New unique GUID"),
        new PlaceholderInfo("cursor", "Final caret position after tab-stop navigation"),
    };

    /// <summary>
    /// Resolves all $placeholder$ tokens in the snippet body.
    /// Unknown placeholders are left unchanged.
    /// </summary>
    public static string Resolve(string body)
    {
        return Resolve(body, null);
    }

    /// <summary>
    /// Resolves all $placeholder$ tokens in the snippet body.
    /// System placeholders are resolved to runtime values; custom placeholders
    /// are substituted with defaults from the dictionary (if provided).
    /// Any remaining unknown placeholders are left unchanged.
    /// </summary>
    public static string Resolve(string body, Dictionary<string, string> customDefaults)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains("$"))
            return body;

        return PlaceholderPattern.Replace(body, match =>
        {
            string name = match.Groups[1].Value;
            string resolved = ResolveBuiltIn(name.ToLowerInvariant());
            if (resolved != null)
                return resolved;

            // Try custom defaults (case-insensitive lookup)
            if (customDefaults != null)
            {
                foreach (var kvp in customDefaults)
                {
                    if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                        return kvp.Value;
                }
            }

            return match.Value; // Leave unknown placeholders unchanged
        });
    }

    /// <summary>
    /// Resolves only system/built-in placeholders, leaving custom placeholders unchanged.
    /// Used when preparing snippet bodies for the VS expansion engine, where custom
    /// placeholders become interactive tab stops.
    /// </summary>
    public static string ResolveSystemOnly(string body)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains("$"))
            return body;

        return PlaceholderPattern.Replace(body, match =>
        {
            string name = match.Groups[1].Value.ToLowerInvariant();
            string resolved = ResolveBuiltIn(name);
            // Non-null = system placeholder, replace it. Null = custom, leave for expansion engine.
            return resolved ?? match.Value;
        });
    }

    /// <summary>
    /// Returns the distinct names of placeholders in the body that are NOT built-in system placeholders.
    /// Names are returned in the case they appear in the body.
    /// </summary>
    public static IReadOnlyList<string> GetCustomPlaceholderNames(string body)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains("$"))
            return Array.Empty<string>();

        var builtInNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in BuiltInPlaceholders)
            builtInNames.Add(p.Name);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (Match m in PlaceholderPattern.Matches(body))
        {
            string name = m.Groups[1].Value;
            if (!builtInNames.Contains(name) && seen.Add(name))
                result.Add(name);
        }

        return result;
    }

    /// <summary>
    /// Returns true if the body contains any $placeholder$ tokens.
    /// </summary>
    public static bool HasPlaceholders(string body)
    {
        return !string.IsNullOrEmpty(body) && PlaceholderPattern.IsMatch(body);
    }

    private static string ResolveBuiltIn(string name)
    {
        var now = DateTime.Now;

        switch (name)
        {
            case "date":
                return now.ToString("yyyy-MM-dd");
            case "time":
                return now.ToString("HH:mm:ss");
            case "datetime":
                return now.ToString("yyyy-MM-dd HH:mm:ss");
            case "year":
                return now.Year.ToString();
            case "month":
                return now.Month.ToString("D2");
            case "day":
                return now.Day.ToString("D2");
            case "user":
                return Environment.UserName;
            case "machine":
                return Environment.MachineName;
            case "dbname":
                return GetConnectionInfo().DatabaseName ?? "$dbname$";
            case "server":
                return GetConnectionInfo().ServerName ?? "$server$";
            case "guid":
                return Guid.NewGuid().ToString();
            case "cursor":
                return null; // Handled by SnippetSession, not resolved here
            default:
                return null;
        }
    }

    private static (string DatabaseName, string ServerName) GetConnectionInfo()
    {
        // Use test override if set
        if (ConnectionInfoProvider != null)
        {
            try { return ConnectionInfoProvider(); }
            catch { return (null, null); }
        }

        // Read the cached values (refreshed on the UI thread — see RefreshConnectionInfoFromSsms).
        // This path has no thread affinity, so it is safe from the background completion builder.
        return (_cachedDatabaseName, _cachedServerName);
    }

#if !SQLEXTENDED_TESTS
    /// <summary>
    /// Refreshes the cached connection-derived placeholder values ($dbname$, $server$) from the
    /// active SSMS connection. Must be called on the UI thread — the underlying SSMS APIs are
    /// UI-thread-only. Called from connection-tracking and snippet-insertion paths that already
    /// run on the UI thread; background resolution reads the cached values written here.
    /// </summary>
    public static void RefreshConnectionInfoFromSsms()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try { _cachedDatabaseName = ConnectionHelper.GetCurrentDatabaseName(); }
        catch { /* No active connection */ }

        try
        {
            string connStr = ConnectionHelper.GetActiveConnectionString();
            if (!string.IsNullOrEmpty(connStr))
                _cachedServerName = new SqlConnectionStringBuilder(connStr).DataSource;
        }
        catch { /* No active connection */ }
    }
#endif
}

internal sealed class PlaceholderInfo
{
    public string Name { get; }
    public string Description { get; }

    public PlaceholderInfo(string name, string description)
    {
        Name = name;
        Description = description;
    }
}
