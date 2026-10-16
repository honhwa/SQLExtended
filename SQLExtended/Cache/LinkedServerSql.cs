using System;

namespace SQLExtended.Cache;

/// <summary>
/// The parts of <see cref="LinkedServerCache"/> that do not need a connection: the local query, and how the
/// rows the remote catalog procedures return are filtered and displayed. Free of SqlClient so the test project
/// can link it, the same split <see cref="SystemCatalogSql"/> exists for.
/// </summary>
internal static class LinkedServerSql
{
    /// <summary>
    /// The linked servers defined on the local instance. <c>is_linked = 1</c> leaves out the local server itself
    /// (server_id 0) and the remote-server entries kept only for old-style RPC.
    /// </summary>
    public const string Servers = @"
        SELECT name, product, provider, data_source, is_data_access_enabled, is_rpc_out_enabled
        FROM sys.servers
        WHERE is_linked = 1
        ORDER BY name";

    /// <summary>
    /// The remote reads go through the linked server's provider, and a server that is down or unreachable makes
    /// the provider wait out its own login timeout first. Long enough for a slow but working server; the failure
    /// is then remembered, so it is paid once per refresh rather than per keystroke.
    /// </summary>
    public const int RemoteTimeoutSeconds = 30;

    /// <summary>
    /// Whether a row from <c>sp_tables_ex</c> belongs in the completion list: tables, views and synonyms, outside the
    /// catalog schemas. A SQL Server target reports its catalog views as plain VIEWs, so the schema is what tells
    /// them apart; other providers use SYSTEM TABLE / SYSTEM VIEW, which the type check drops.
    /// </summary>
    public static bool IsUserObject(string schema, string type)
    {
        if (string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(schema, "INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase))
            return false;
        return string.Equals(type, "TABLE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "VIEW", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "SYNONYM", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "nvarchar(50)", "decimal(18,2)", "varchar(max)" from <c>sp_columns_ex</c>'s TYPE_NAME, COLUMN_SIZE and
    /// DECIMAL_DIGITS. COLUMN_SIZE is in characters, not bytes, so this cannot reuse
    /// <c>SchemaCacheLoader.FormatDataType</c>, which halves an nvarchar's byte length.
    /// </summary>
    public static string FormatType(string typeName, int? size, int? digits)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return "";

        string type = typeName.Trim();
        if (type.IndexOf('(') >= 0)
            return type;

        string lower = type.ToLowerInvariant();
        if ((lower == "decimal" || lower == "numeric") && size > 0)
            return $"{type}({size},{digits ?? 0})";

        if (lower.Contains("char") || lower.Contains("binary"))
        {
            // A (max) column is reported as 0, -1, 2^30-1 or 2^31-1 depending on the provider.
            return size is null or <= 0 or > 8000 ? $"{type}(max)" : $"{type}({size})";
        }

        return type;
    }
}
