using System;
using System.Collections.Generic;
using System.Linq;

namespace SQLExtended.ResultsGrid.Columns;

/// <summary>One column of one results grid, as the Go to Column picker lists it.</summary>
internal sealed class GridColumnEntry
{
    /// <summary>Which grid, in the order the picker lists them: the grid that had the focus first, then the rest in result-set order.</summary>
    public int GridOrder { get; set; }

    /// <summary>"Result 2", or null when there is only one grid to tell apart.</summary>
    public string GridLabel { get; set; }

    /// <summary>The grid's storage column index — column 0 is the row-number column, so data starts at 1.</summary>
    public int StorageColumn { get; set; }

    /// <summary>1-based position as the column appears on screen, which differs from storage order once a header has been dragged.</summary>
    public int Position { get; set; }

    public string Name { get; set; }

    /// <summary>"int", "nvarchar(50)" … or null when SSMS's result set would not say.</summary>
    public string DataType { get; set; }

    /// <summary>The grid this entry belongs to. Opaque here, so this file stays free of the grid assembly.</summary>
    public object Grid { get; set; }
}

/// <summary>
/// Filters and ranks the picker's list as the user types. Free of the grid assembly so the test project links it.
///
/// <para><b>Grouped by grid first, then ranked by how well the name matches, then by on-screen position.</b> The grid
/// the user was in comes first, because that is nearly always the one they mean; ranking a weak match there below an
/// exact one in another result set would put the cursor in a grid they were not looking at. Within a grid, a name
/// that is the text typed beats one that starts with it, which beats a word inside it ("qty" → <c>Ongoing_Qty</c>,
/// <c>OngoingQty</c>), which beats any substring, which beats initials ("oq" → <c>OngoingQty</c>).</para>
/// </summary>
internal static class GridColumnMatcher
{
    public const int Exact = 500, Prefix = 400, WordStart = 300, Substring = 200, Initials = 100, NoMatch = -1;

    /// <summary>The entries that match <paramref name="query"/>, best first. An empty query keeps every column, in grid and screen order.</summary>
    public static List<GridColumnEntry> Filter(IEnumerable<GridColumnEntry> entries, string query)
    {
        query = query?.Trim() ?? "";
        return entries
            .Select(e => (Entry: e, Score: Score(e.Name, query)))
            .Where(x => x.Score != NoMatch)
            .OrderBy(x => x.Entry.GridOrder)
            .ThenByDescending(x => x.Score)
            .ThenBy(x => x.Entry.Position)
            .Select(x => x.Entry)
            .ToList();
    }

    /// <summary>How well <paramref name="name"/> matches what was typed; <see cref="NoMatch"/> when it does not. Case-insensitive.</summary>
    public static int Score(string name, string query)
    {
        name ??= "";
        if (string.IsNullOrEmpty(query))
            return 0;
        if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase))
            return Exact;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return Prefix;

        int at = name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (at > 0)
        {
            // Any later occurrence may be the one on a word boundary ("ab" in "Tab_AbCount" is a word at 4).
            for (int i = at; i >= 0; i = i + 1 < name.Length ? name.IndexOf(query, i + 1, StringComparison.OrdinalIgnoreCase) : -1)
                if (IsWordStart(name, i))
                    return WordStart;
            return Substring;
        }

        return MatchesInitials(name, query) ? Initials : NoMatch;
    }

    /// <summary>A word starts after a separator, at an upper-case letter after a lower-case one, or where letters meet digits.</summary>
    private static bool IsWordStart(string name, int i)
    {
        if (i == 0)
            return true;
        char prev = name[i - 1], c = name[i];
        if (!char.IsLetterOrDigit(prev))
            return true;
        if (char.IsUpper(c) && char.IsLower(prev))
            return true;
        return char.IsDigit(c) != char.IsDigit(prev);
    }

    /// <summary>
    /// "oqd" matches <c>OngoingQtyDate</c> and <c>ongoing_qty_date</c>: each typed character is the start of a word,
    /// in order, though not every word has to be used.
    /// </summary>
    private static bool MatchesInitials(string name, string query)
    {
        int q = 0;
        for (int i = 0; i < name.Length && q < query.Length; i++)
        {
            if (IsWordStart(name, i) && char.IsLetterOrDigit(name[i]) && char.ToUpperInvariant(name[i]) == char.ToUpperInvariant(query[q]))
                q++;
        }
        return q == query.Length;
    }
}
