using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLExtended.Cache.Models;

namespace SQLExtended.Cache;

/// <summary>
/// Shared schema cache consumed by all features (schema viewer, tooltips, IntelliSense, search).
/// In-memory lookups are sub-millisecond. Population queries run in the background.
/// </summary>
internal interface ISchemaCache
{
    // --- State ---

    CacheState GetState(string connectionKey, string database);
    event EventHandler<CacheRefreshEventArgs> CacheRefreshed;

    // --- Population ---

    /// <summary>
    /// Loads all schema metadata for a database into the cache.
    /// Queries sys.* views in parallel for fast bulk loading.
    /// </summary>
    Task LoadDatabaseAsync(string connectionString, string database, bool forceFullRefresh = false);

    /// <summary>
    /// Lightweight refresh: re-queries only objects modified since last refresh.
    /// </summary>
    Task IncrementalRefreshAsync(string connectionString, string database);

    void ClearDatabase(string connectionKey, string database);
    void ClearAll();

    // --- Lookups (all from in-memory cache, sub-millisecond) ---

    IReadOnlyList<CachedDatabase> GetDatabases(string connectionKey);
    IReadOnlyList<CachedObject> GetObjects(string connectionKey, string database, string schema = null, string typeFilter = null);
    CachedObject FindObject(string connectionKey, string database, string schema, string name);
    IReadOnlyList<CachedColumn> GetColumns(string connectionKey, string database, string schema, string tableName);
    IReadOnlyList<CachedIndex> GetIndexes(string connectionKey, string database, string schema, string tableName);
    IReadOnlyList<CachedForeignKey> GetForeignKeys(string connectionKey, string database, string schema, string tableName);
    IReadOnlyList<CachedParameter> GetParameters(string connectionKey, string database, string schema, string objectName);

    // --- Search ---

    IReadOnlyList<SearchResult> Search(string connectionKey, string database, string searchTerm, SearchOptions options = null);

    // --- Connection key helpers ---

    /// <summary>
    /// Extracts a stable connection key ("server") from a connection string.
    /// </summary>
    string GetConnectionKey(string connectionString);
}
