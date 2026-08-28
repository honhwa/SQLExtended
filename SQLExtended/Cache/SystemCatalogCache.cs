using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLExtended.Cache.Models;
using Microsoft.Data.SqlClient;

namespace SQLExtended.Cache;

/// <summary>
/// The <c>sys</c> and <c>INFORMATION_SCHEMA</c> catalog surface — catalog views, DMVs, their
/// table-valued functions and every column on them — so IntelliSense can complete <c>sys.</c>.
///
/// This is separate from <see cref="SchemaCache"/> rather than folded into it for one reason:
/// <b>it is keyed per server, not per database.</b> <see cref="SchemaCacheLoader"/> filters on
/// <c>is_ms_shipped = 0</c>, which is what makes <c>sys.</c> come back empty today; simply
/// dropping that filter would work, but it would then load ~1,100 objects and ~9,000 columns
/// again for every database on the instance. The catalog surface is a property of the engine
/// build, not of the database — every database on an instance exposes the same catalog views —
/// so it is read once per server and shared.
///
/// The read is therefore done against whatever database the connection is already pointing at,
/// with no USE and no re-pointing. That assumption has one visible edge: a contained database or
/// an Azure SQL Database presents a slightly different surface to a box instance, and whichever
/// database the first completion happened in is the one that answers for the server all session.
/// The alternative — a load per database — costs far more than that edge is worth.
///
/// Nothing here is persisted to SQLite. It is one query, it is a per-session cost, and the answer
/// changes only when the instance is patched.
/// </summary>
internal sealed class SystemCatalogCache
{
    private static readonly Lazy<SystemCatalogCache> _instance = new(() => new SystemCatalogCache());
    public static SystemCatalogCache Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, SystemCatalogData> _byServer = new(StringComparer.OrdinalIgnoreCase);

    // A load in flight, and a load that failed. Both are per server and both matter: completion
    // asks on every keystroke, so without the first a slow instance would stack a query per
    // character, and without the second a login that cannot read the catalog (or an instance that
    // timed out) would pay the full command timeout again on every one. Clearing the schema cache
    // clears these too — that is the only way to retry a server that failed earlier in the session.
    private readonly ConcurrentDictionary<string, byte> _loading = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _failed = new(StringComparer.OrdinalIgnoreCase);

    private SystemCatalogCache() { }

    /// <summary>
    /// True once this server's catalog is in memory. Otherwise starts a background load (unless one
    /// is already running, or an earlier one failed) and returns false, so the caller offers nothing
    /// this pass — the items appear when the user next triggers completion. Never blocks: this is
    /// called from the completion path.
    /// </summary>
    public bool EnsureLoaded(string connectionString, string connectionKey)
    {
        if (string.IsNullOrEmpty(connectionKey))
            return false;
        if (_byServer.ContainsKey(connectionKey))
            return true;
        if (string.IsNullOrEmpty(connectionString) || _failed.ContainsKey(connectionKey))
            return false;
        if (!_loading.TryAdd(connectionKey, 0))
            return false;

        _ = Task.Run(() =>
        {
            try
            {
                _byServer[connectionKey] = Load(connectionString);
            }
            catch (Exception ex)
            {
                // No catalog is a missing completion list, never an error the user has to dismiss - but the
                // memo below means it is never retried this session either, so this is the only chance to say
                // why "sys." went quiet.
                Diagnostics.SQLExtendedLog.Warning("SystemCatalog", $"Catalog load failed for {connectionKey}; sys. completion will be empty until the cache is cleared", ex);
                _failed[connectionKey] = 0;
            }
            finally
            {
                _loading.TryRemove(connectionKey, out _);
            }
        });

        return false;
    }

    /// <summary>Every system object on the server, or an empty list if it is not loaded yet.</summary>
    public IReadOnlyList<CachedObject> GetObjects(string connectionKey)
    {
        if (connectionKey != null && _byServer.TryGetValue(connectionKey, out var data))
            return data.Objects;
        return Array.Empty<CachedObject>();
    }

    /// <summary>Columns of one system object, in ordinal order, or an empty list.</summary>
    public IReadOnlyList<CachedColumn> GetColumns(string connectionKey, string schema, string objectName)
    {
        if (connectionKey == null || schema == null || objectName == null)
            return Array.Empty<CachedColumn>();
        if (_byServer.TryGetValue(connectionKey, out var data) &&
            data.Columns.TryGetValue(Key(schema, objectName), out var columns))
            return columns;
        return Array.Empty<CachedColumn>();
    }

    /// <summary>True if the name is one of the two schemas this cache covers.</summary>
    public static bool IsSystemSchema(string schema) =>
        string.Equals(schema, "sys", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(schema, "INFORMATION_SCHEMA", StringComparison.OrdinalIgnoreCase);

    /// <summary>The schemas offered as completion items, in display order.</summary>
    public static readonly string[] SystemSchemas = { "sys", "INFORMATION_SCHEMA" };

    public void Clear()
    {
        _byServer.Clear();
        _failed.Clear();
        // _loading is deliberately left alone: a load in flight will still write its result, and
        // removing the key here would let a second load start alongside it.
    }

    // --- Loading ---

    private static SystemCatalogData Load(string connectionString)
    {
        var data = new SystemCatalogData();

        using var conn = SqlConnectionFactory.Create(connectionString);
        conn.Open();
        using var cmd = new SqlCommand(SystemCatalogSql.ObjectsAndColumns, conn) { CommandTimeout = 60 };
        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            data.Objects.Add(new CachedObject
            {
                SchemaName = reader.GetString(0),
                ObjectName = reader.GetString(1),
                ObjectType = reader.GetString(2)?.Trim()
            });
        }

        if (!reader.NextResult())
            return data;

        List<CachedColumn> current = null;
        string currentKey = null;

        while (reader.Read())
        {
            var column = new CachedColumn
            {
                SchemaName = reader.GetString(0),
                TableName = reader.GetString(1),
                ColumnName = reader.GetString(2),
                Ordinal = reader.GetInt32(3),
                DataType = SchemaCacheLoader.FormatDataType(
                    reader.GetString(4), reader.GetInt16(5), reader.GetByte(6), reader.GetByte(7)),
                IsNullable = reader.GetBoolean(8)
            };

            // The query orders by schema/object, so grouping is a running key rather than a lookup.
            string key = Key(column.SchemaName, column.TableName);
            if (currentKey == null || !string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                current = new List<CachedColumn>();
                data.Columns[key] = current;
                currentKey = key;
            }
            current.Add(column);
        }

        return data;
    }

    private static string Key(string schema, string objectName) => schema + "." + objectName;

    private sealed class SystemCatalogData
    {
        public List<CachedObject> Objects { get; } = new();

        /// <summary>"schema.object" → its columns, in ordinal order. A dictionary rather than a
        /// filtered scan: this holds ~9,000 columns and is hit on every keystroke after a dot.</summary>
        public Dictionary<string, List<CachedColumn>> Columns { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
