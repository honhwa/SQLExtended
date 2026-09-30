using SQLExtended.Cache.Models;
using Xunit;

namespace SQLExtended.Tests.Cache;

public class CachedIndexTests
{
    // KeyColumns comes from the loader's FOR XML PATH as "name[ DESC], ..." with the names unbracketed. A descending
    // key column that kept its marker silently lost its (PK) flag in completion, and the schema viewer's old
    // "cut at the first space" turned the column "Order Date" into "Order".
    [Theory]
    [InlineData("Id", new[] { "Id" })]
    [InlineData("CustomerId, OrderDate DESC", new[] { "CustomerId", "OrderDate" })]
    [InlineData("Order Date DESC, Line No", new[] { "Order Date", "Line No" })]
    [InlineData("Description", new[] { "Description" })]          // ends in "DESC" only as a word part: untouched
    [InlineData("Sort Desc DESC", new[] { "Sort Desc" })]         // only the one trailing marker is removed
    public void KeyColumnNames_StripsOnlyTheTrailingDescMarker(string keyColumns, string[] expected)
    {
        Assert.Equal(expected, new CachedIndex { KeyColumns = keyColumns }.KeyColumnNames());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void KeyColumnNames_NoKeys_IsEmpty(string keyColumns)
    {
        Assert.Empty(new CachedIndex { KeyColumns = keyColumns }.KeyColumnNames());
    }
}
