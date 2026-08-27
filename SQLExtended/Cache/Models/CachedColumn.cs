namespace SQLExtended.Cache.Models;

internal sealed class CachedColumn
{
    public string SchemaName { get; set; }
    public string TableName { get; set; }
    public string ColumnName { get; set; }
    public int Ordinal { get; set; }
    public string DataType { get; set; }
    public int MaxLength { get; set; }
    public int Precision { get; set; }
    public int Scale { get; set; }
    public bool IsNullable { get; set; }
    public bool IsIdentity { get; set; }
    public bool IsComputed { get; set; }
    public string ComputedDefinition { get; set; }
    public string DefaultDefinition { get; set; }

    /// <summary>
    /// MS_Description extended property value, if any.
    /// </summary>
    public string Description { get; set; }
}
