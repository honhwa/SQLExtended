namespace SQLExtended.Cache.Models;

internal sealed class SearchOptions
{
    /// <summary>Object type filter (e.g., "U", "V", "P"). Null means all types.</summary>
    public string TypeFilter { get; set; }

    /// <summary>Search in object names.</summary>
    public bool SearchObjectNames { get; set; } = true;

    /// <summary>Search in column names.</summary>
    public bool SearchColumnNames { get; set; } = true;

    /// <summary>Search in stored proc/function/view definitions.</summary>
    public bool SearchDefinitions { get; set; } = true;

    public int MaxResults { get; set; } = 200;
}
