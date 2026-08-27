namespace SQLExtended.ResultsGrid.Find;

/// <summary>
/// What the user asked to find, and how. Free of the grid assembly, WPF and SqlClient so the test project
/// can link it alongside <see cref="GridFindMatcher"/> and <see cref="GridFindScan"/>.
/// </summary>
internal sealed class GridFindOptions
{
    /// <summary>Case-sensitive comparison. Off by default: a DBA scanning a result set is looking for a
    /// value, not a spelling.</summary>
    public bool MatchCase { get; set; }

    /// <summary>The whole cell must equal the term, rather than merely contain it.</summary>
    public bool WholeCell { get; set; }

    /// <summary>Treat the term as a .NET regular expression.</summary>
    public bool UseRegex { get; set; }

    /// <summary>Tint every match, not just the one being stepped through.</summary>
    public bool HighlightAll { get; set; } = true;

    /// <summary>Search every result set in the query window rather than only the grid last worked in.</summary>
    public bool AllResultSets { get; set; }

    public GridFindOptions Clone() => new()
    {
        MatchCase = MatchCase,
        WholeCell = WholeCell,
        UseRegex = UseRegex,
        HighlightAll = HighlightAll,
        AllResultSets = AllResultSets
    };

    /// <summary>
    /// Whether two option sets would find the <b>same cells</b>. Deliberately ignores
    /// <see cref="HighlightAll"/>, which only decides what is painted — toggling it must not throw away a
    /// completed scan and re-read the whole grid for an answer that cannot have changed.
    /// </summary>
    public bool MatchingEquals(GridFindOptions other) =>
        other != null && MatchCase == other.MatchCase && WholeCell == other.WholeCell && UseRegex == other.UseRegex && AllResultSets == other.AllResultSets;
}
