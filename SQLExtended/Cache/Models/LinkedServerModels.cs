using System;
using System.Collections.Generic;

namespace SQLExtended.Cache.Models;

/// <summary>One row of the local <c>sys.servers</c> with <c>is_linked = 1</c>.</summary>
internal sealed class LinkedServerInfo
{
    public string Name { get; set; }
    public string Product { get; set; }
    public string Provider { get; set; }
    public string DataSource { get; set; }
    public bool IsDataAccessEnabled { get; set; }
    public bool IsRpcOutEnabled { get; set; }

    /// <summary>"SQL Server", or the provider's product name, for the completion item's suffix.</summary>
    public string Description => string.IsNullOrWhiteSpace(Product) ? Provider ?? "" : Product;
}

/// <summary>A table or view on a linked server, as <c>sp_tables_ex</c> reports it.</summary>
internal sealed class LinkedObject
{
    public string Schema { get; set; }
    public string Name { get; set; }

    /// <summary>TABLE, VIEW or SYNONYM — the provider's own word, not a sys.objects type code.</summary>
    public string Type { get; set; }
}

/// <summary>How far a linked-server load has got, for the Schema Cache window.</summary>
internal enum LinkedLoadState
{
    NotLoaded,
    Loading,
    Ready,
    Failed
}

/// <summary>What the Schema Cache window shows for one linked server.</summary>
internal sealed class LinkedServerSnapshot
{
    public LinkedServerInfo Server { get; set; }
    public LinkedLoadState DatabasesState { get; set; }
    public string DatabasesError { get; set; }
    public DateTime? DatabasesLoadedUtc { get; set; }
    public IReadOnlyList<LinkedDatabaseSnapshot> Databases { get; set; } = Array.Empty<LinkedDatabaseSnapshot>();
}

/// <summary>What the Schema Cache window shows for one database on a linked server.</summary>
internal sealed class LinkedDatabaseSnapshot
{
    public string Name { get; set; }
    public LinkedLoadState ObjectsState { get; set; }
    public string ObjectsError { get; set; }
    public int TableCount { get; set; }
    public int ViewCount { get; set; }
    public int OtherCount { get; set; }

    /// <summary>Tables whose columns have been read so far — those are loaded one table at a time, as they are used.</summary>
    public int TablesWithColumns { get; set; }
}
