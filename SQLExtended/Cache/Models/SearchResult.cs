namespace SQLExtended.Cache.Models;

internal sealed class SearchResult
{
    public string SchemaName { get; set; }
    public string ObjectName { get; set; }
    public string ObjectType { get; set; }

    /// <summary>Where the match was found: ObjectName, ColumnName, or Definition.</summary>
    public string MatchLocation { get; set; }

    /// <summary>The specific matched text (column name, or definition snippet).</summary>
    public string MatchDetail { get; set; }
}
