using System;
using System.Collections.Generic;

namespace SQLExtended.Cache.Models;

/// <summary>A count of cached objects of one display category (e.g. "Tables", "Views").</summary>
internal sealed class ObjectTypeCount
{
    public string Label { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// A point-in-time view of one cached database on one server, used by the
/// Schema Cache tool window to show what the shared cache currently holds.
/// </summary>
internal sealed class CacheSnapshotEntry
{
    /// <summary>The server key (the connection's lowercased DataSource).</summary>
    public string ConnectionKey { get; set; }

    public string Database { get; set; }

    public CacheState State { get; set; }

    /// <summary>Number of objects (tables/views/procs/functions) held for this database.</summary>
    public int ObjectCount { get; set; }

    /// <summary>UTC time of the last full refresh this session, or null if never refreshed this session.</summary>
    public DateTime? LastRefreshUtc { get; set; }

    /// <summary>
    /// The connection string last used for this database, when known. Null for entries
    /// hydrated from the SQLite store at startup that haven't been touched this session.
    /// </summary>
    public string ConnectionString { get; set; }

    /// <summary>True when the data was loaded from disk (SQLite) but not refreshed against the server this session.</summary>
    public bool FromDiskOnly => State == CacheState.Stale && LastRefreshUtc == null;

    /// <summary>Per-category counts of the cached objects, in display order. Empty when nothing is cached for this database.</summary>
    public IReadOnlyList<ObjectTypeCount> ObjectTypeCounts { get; set; } = Array.Empty<ObjectTypeCount>();
}
