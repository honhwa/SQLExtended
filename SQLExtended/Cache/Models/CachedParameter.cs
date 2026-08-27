namespace SQLExtended.Cache.Models;

internal sealed class CachedParameter
{
    public string SchemaName { get; set; }
    public string ObjectName { get; set; }
    public string ParameterName { get; set; }
    public int Ordinal { get; set; }
    public string DataType { get; set; }
    public int MaxLength { get; set; }
    public bool IsOutput { get; set; }
    public bool HasDefault { get; set; }
}
