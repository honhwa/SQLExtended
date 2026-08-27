namespace SQLExtended.Cache.Models;

internal sealed class CachedIndex
{
    public string SchemaName { get; set; }
    public string TableName { get; set; }
    public string IndexName { get; set; }
    public string IndexType { get; set; }
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }

    /// <summary>Comma-separated key column names.</summary>
    public string KeyColumns { get; set; }

    /// <summary>Comma-separated included column names.</summary>
    public string IncludedColumns { get; set; }

    public string FilterDefinition { get; set; }
}
