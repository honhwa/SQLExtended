namespace SQLExtended.Cache.Models;

internal sealed class CachedForeignKey
{
    public string SchemaName { get; set; }
    public string TableName { get; set; }
    public string ForeignKeyName { get; set; }
    public string Columns { get; set; }
    public string ReferencedSchema { get; set; }
    public string ReferencedTable { get; set; }
    public string ReferencedColumns { get; set; }
    public string DeleteAction { get; set; }
    public string UpdateAction { get; set; }
}
