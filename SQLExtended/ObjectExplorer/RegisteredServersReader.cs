using System;
using System.Collections.Generic;
using Microsoft.SqlServer.Management.RegisteredServers;

namespace SQLExtended.ObjectExplorer;

/// <summary>
/// Reads SSMS's Registered Servers hierarchy (Local Server Groups) and produces a map from a
/// server/instance name to the chain of group folders that contain it. Backed by SMO's
/// <see cref="RegisteredServersStore"/> (reads %APPDATA%\Microsoft\SQL Server Management Studio\...\RegSrvr.xml).
///
/// Used by <see cref="ServerGroupFolderService"/> to mirror those groups as folders at the root of
/// the Object Explorer tree. Servers registered directly under the root group (in no folder) map to
/// an empty path and are left ungrouped at the OE root.
/// </summary>
internal static class RegisteredServersReader
{
    /// <summary>
    /// Builds a case-insensitive map of normalized server name → group path (outermost group first,
    /// innermost last). Returns an empty map if the store can't be read. Safe to call repeatedly;
    /// the store is re-read each call so changes to Registered Servers are picked up.
    /// </summary>
    public static Dictionary<string, List<string>> BuildServerGroupMap()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var store = RegisteredServersStore.LocalFileStore;
            var root = store?.DatabaseEngineServerGroup;
            if (root != null)
                Walk(root, new List<string>(), map);
        }
        catch
        {
            // Store missing or unreadable — callers treat an empty map as "nothing to group".
        }
        return map;
    }

    private static void Walk(ServerGroup group, List<string> path, Dictionary<string, List<string>> map)
    {
        foreach (RegisteredServer rs in group.RegisteredServers)
        {
            // A registered server's display Name (e.g. an alias or IP) can differ from its actual
            // connection target (ServerName), and the OE node may show either — so index by both.
            // First registration wins if the same key appears in multiple groups.
            foreach (string key in new[] { Normalize(rs.ServerName), Normalize(rs.Name) })
                if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                    map[key] = new List<string>(path);
        }

        foreach (ServerGroup child in group.ServerGroups)
        {
            path.Add(child.Name);
            Walk(child, path, map);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>
    /// Normalizes a server name for matching between Registered Servers and Object Explorer nodes:
    /// trims, drops a leading protocol prefix (tcp:), strips a trailing ,port, and collapses an
    /// explicit default instance (SERVER\MSSQLSERVER → SERVER). Comparison is case-insensitive.
    /// </summary>
    public static string Normalize(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName)) return "";

        string s = serverName.Trim();

        if (s.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(4).Trim();

        int comma = s.IndexOf(',');
        if (comma >= 0)
            s = s.Substring(0, comma).Trim();

        if (s.EndsWith("\\MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(0, s.Length - "\\MSSQLSERVER".Length).Trim();

        return s;
    }
}
