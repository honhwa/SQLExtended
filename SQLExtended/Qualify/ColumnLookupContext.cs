using SQLExtended.Cache;
using SQLExtended.Cache.Models;
using SQLExtended.IntelliSense;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SQLExtended.Qualify;

/// <summary>
/// The active query window's connection and schema cache, as Qualify Columns and Tab-after-<c>*</c> look columns up in
/// them. Both refuse to run until the current database's cache has loaded.
/// </summary>
internal sealed class ColumnLookupContext
{
    private readonly SchemaCache _cache;
    private readonly string _connectionString;
    private readonly string _connKey;
    private readonly string _database;

    private ColumnLookupContext(SchemaCache cache, string connectionString, string connKey, string database)
    {
        _cache = cache;
        _connectionString = connectionString;
        _connKey = connKey;
        _database = database;
    }

    /// <summary>Null, with the reason in words for the status bar, when there is no connection or its cache is not ready.
    /// Must be called on the UI thread — the connection is read from the active window.</summary>
    public static ColumnLookupContext TryCreate(out string notReady)
    {
        notReady = null;
        string connectionString = ConnectionHelper.GetActiveConnectionString();
        string database = ConnectionHelper.GetCurrentDatabaseName();
        if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(database))
        {
            notReady = "connect the query window first — which table a column belongs to comes from the schema cache.";
            return null;
        }

        // Running against a cache that has not loaded would alias the tables and find no columns, and the user would
        // reasonably read that as the feature not working.
        var cache = SchemaCache.Instance;
        string connKey = cache.GetConnectionKey(connectionString);
        var state = cache.GetState(connKey, database);
        if (state is CacheState.NotLoaded or CacheState.Loading or CacheState.Error)
        {
            if (state != CacheState.Loading)
                _ = cache.LoadDatabaseAsync(connectionString, database);
            notReady = $"the schema cache for {database} is loading — try again in a moment.";
            return null;
        }

        return new ColumnLookupContext(cache, connectionString, connKey, database);
    }

    /// <summary>The columns of a table in declared order, or null when they are not known.</summary>
    public IReadOnlyCollection<string> Lookup(QualifierTableName table, IReadOnlyList<LocalTableScanner.LocalTable> localTables)
    {
        // A linked server's columns are fetched one table at a time on demand; not worth a round trip from a command.
        if (table.Server != null)
            return null;

        if (LocalTableScanner.IsLocalName(table.Name))
            return Names(LocalTableScanner.Find(localTables, table.Name)?.Columns);

        string database = table.Database ?? _database;
        string schema = table.Schema ?? "dbo";

        // Another database on this server: start its load so the next run can see it, and leave its scope alone now.
        if (!string.Equals(database, _database, StringComparison.OrdinalIgnoreCase) && _cache.GetState(_connKey, database) == CacheState.NotLoaded)
        {
            _ = _cache.LoadDatabaseAsync(_connectionString, database);
            return null;
        }

        var columns = _cache.GetColumns(_connKey, database, schema, table.Name);
        if (columns.Count == 0 && SystemCatalogCache.IsSystemSchema(schema))
            columns = SystemCatalogCache.Instance.GetColumns(_connKey, schema, table.Name);
        return Names(columns);
    }

    // Ordered for an expanded *, which lists the columns as SQL Server would. OrderBy is stable, so a source whose
    // ordinals are all unset keeps the order it came in.
    private static IReadOnlyCollection<string> Names(IEnumerable<CachedColumn> columns)
    {
        var names = columns?.OrderBy(c => c.Ordinal).Select(c => c.ColumnName).ToList();
        return names is { Count: > 0 } ? names : null;
    }
}
