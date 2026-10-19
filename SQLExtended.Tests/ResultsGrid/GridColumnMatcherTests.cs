using System.Collections.Generic;
using System.Linq;
using SQLExtended.ResultsGrid.Columns;
using Xunit;

namespace SQLExtended.Tests.ResultsGrid;

/// <summary>
/// Pins the Go to Column picker's filter. A wrong ranking is never an error: the top item is simply a column the user
/// did not mean, and Enter takes them there — so the order is what is worth asserting.
/// </summary>
public class GridColumnMatcherTests
{
    [Theory]
    [InlineData("Qty", "qty", GridColumnMatcher.Exact)]
    [InlineData("QtyOrdered", "qty", GridColumnMatcher.Prefix)]
    [InlineData("OngoingQty", "qty", GridColumnMatcher.WordStart)]
    [InlineData("Ongoing_Qty", "qty", GridColumnMatcher.WordStart)]
    [InlineData("Ongoing Qty", "qty", GridColumnMatcher.WordStart)]
    [InlineData("Address2", "2", GridColumnMatcher.WordStart)]
    [InlineData("Equity", "qui", GridColumnMatcher.Substring)]
    [InlineData("OngoingQtyDate", "oqd", GridColumnMatcher.Initials)]
    [InlineData("ongoing_qty_date", "oqd", GridColumnMatcher.Initials)]
    [InlineData("OngoingQtyDate", "oqx", GridColumnMatcher.NoMatch)]
    [InlineData("Customer", "xyz", GridColumnMatcher.NoMatch)]
    [InlineData("Anything", "", 0)]
    public void Score_RanksHowTheNameMatches(string name, string query, int expected)
    {
        Assert.Equal(expected, GridColumnMatcher.Score(name, query));
    }

    [Fact]
    public void Score_FindsALaterOccurrenceOnAWordBoundary()
    {
        // The first "ab" is mid-word; the second starts a word, and that is the one that counts.
        Assert.Equal(GridColumnMatcher.WordStart, GridColumnMatcher.Score("Tab_AbCount", "ab"));
    }

    [Fact]
    public void Filter_EmptyQueryKeepsScreenOrder()
    {
        var entries = Entries(("CustomerId", 3), ("Name", 1), ("Qty", 2));
        Assert.Equal(new[] { "Name", "Qty", "CustomerId" }, GridColumnMatcher.Filter(entries, "  ").Select(e => e.Name));
    }

    [Fact]
    public void Filter_BestMatchFirst_ThenPosition()
    {
        var entries = Entries(("OngoingQty", 1), ("QtyOrdered", 2), ("Equity", 3), ("Qty", 4), ("Customer", 5), ("QtyShipped", 6));
        Assert.Equal(new[] { "Qty", "QtyOrdered", "QtyShipped", "OngoingQty" }, GridColumnMatcher.Filter(entries, "qty").Select(e => e.Name));
    }

    [Fact]
    public void Filter_TheGridTheUserWasInComesFirst()
    {
        // An exact match in another result set does not jump ahead of a weaker one in the grid that had the focus.
        var entries = new List<GridColumnEntry>
        {
            new() { Name = "Qty", Position = 1, GridOrder = 2 },
            new() { Name = "OngoingQty", Position = 4, GridOrder = 0 },
        };
        Assert.Equal(new[] { "OngoingQty", "Qty" }, GridColumnMatcher.Filter(entries, "qty").Select(e => e.Name));
    }

    [Fact]
    public void Filter_DuplicateNamesAreBothKept()
    {
        // Two "Id" columns from a join are different columns; the position tells them apart in the list.
        var entries = Entries(("Id", 1), ("Id", 7));
        Assert.Equal(new[] { 1, 7 }, GridColumnMatcher.Filter(entries, "id").Select(e => e.Position));
    }

    private static List<GridColumnEntry> Entries(params (string Name, int Position)[] columns) =>
        columns.Select(c => new GridColumnEntry { Name = c.Name, Position = c.Position, StorageColumn = c.Position }).ToList();
}
