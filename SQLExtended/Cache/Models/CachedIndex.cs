using System;
using System.Collections.Generic;

namespace SQLExtended.Cache.Models;

internal sealed class CachedIndex
{
    public string SchemaName { get; set; }
    public string TableName { get; set; }
    public string IndexName { get; set; }
    public string IndexType { get; set; }
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }

    /// <summary>
    /// Comma-separated key column names, unbracketed, each followed by " DESC" when descending ("Id, OrderDate DESC").
    /// Use <see cref="KeyColumnNames"/> to compare against column names.
    /// </summary>
    public string KeyColumns { get; set; }

    /// <summary>Comma-separated included column names.</summary>
    public string IncludedColumns { get; set; }

    public string FilterDefinition { get; set; }

    /// <summary>
    /// The key column names alone, with the " DESC" marker removed. Only the trailing marker is removed, never
    /// "everything after the first space": the names are unbracketed, so "Order Date DESC" is the column
    /// "Order Date", and cutting at the space would turn it into a column called "Order".
    /// </summary>
    public IEnumerable<string> KeyColumnNames()
    {
        if (string.IsNullOrEmpty(KeyColumns))
            yield break;

        foreach (var part in KeyColumns.Split(','))
        {
            string name = part.Trim();
            if (name.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 5).TrimEnd();
            if (name.Length > 0)
                yield return name;
        }
    }
}
