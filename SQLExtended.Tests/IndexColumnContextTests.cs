using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

public class IndexColumnContextTests
{
    private static SqlContextAnalyzer.AnalysisResult AnalyzeAtEnd(string sql)
        => SqlContextAnalyzer.Analyze(sql, sql.Length);

    [Theory]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (")]
    [InlineData("CREATE NONCLUSTERED INDEX IX_Orders ON dbo.Orders(")]
    [InlineData("CREATE UNIQUE CLUSTERED INDEX IX_Orders ON dbo.Orders (Cust")]
    [InlineData("CREATE UNIQUE NONCLUSTERED INDEX [IX Orders] ON [dbo].[Orders] (CustomerId, ")]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId ASC, OrderDate DESC, [Ord")]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) INCLUDE (")]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId)\nINCLUDE (Total, St")]
    [InlineData("CREATE NONCLUSTERED COLUMNSTORE INDEX IX_Orders ON dbo.Orders (")]
    [InlineData("CREATE STATISTICS ST_Orders ON dbo.Orders (")]
    public void Analyze_InIndexColumnList_ReturnsIndexColumn(string sql)
    {
        var r = AnalyzeAtEnd(sql);
        Assert.Equal(SqlContextAnalyzer.CompletionType.IndexColumn, r.Type);
        Assert.Equal("dbo", r.TargetSchema);
        Assert.Equal("Orders", r.TargetTable);
        Assert.Null(r.TargetDatabase);
    }

    [Theory]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId ")]      // after a column: ASC/DESC or ',' next
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) ")]     // list closed
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) INCLUDE (Total) ")]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders ")]
    public void Analyze_OutsideColumnSlot_IsNotIndexColumn(string sql)
    {
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.IndexColumn, AnalyzeAtEnd(sql).Type);
    }

    [Fact]
    public void Analyze_UnqualifiedAndThreePartTargets()
    {
        var bare = AnalyzeAtEnd("CREATE INDEX IX ON Orders (");
        Assert.Null(bare.TargetSchema);
        Assert.Equal("Orders", bare.TargetTable);

        var threePart = AnalyzeAtEnd("CREATE INDEX IX ON Sales.dbo.[Order Lines] (");
        Assert.Equal("Sales", threePart.TargetDatabase);
        Assert.Equal("dbo", threePart.TargetSchema);
        Assert.Equal("Order Lines", threePart.TargetTable);

        var temp = AnalyzeAtEnd("CREATE INDEX IX ON #work (");
        Assert.Equal(SqlContextAnalyzer.CompletionType.IndexColumn, temp.Type);
        Assert.Equal("#work", temp.TargetTable);
    }

    [Fact]
    public void Analyze_CollectsColumnsAlreadyListed()
    {
        var r = AnalyzeAtEnd("CREATE INDEX IX ON dbo.Orders (CustomerId ASC, [Order Date] DESC) INCLUDE (Total, ");
        Assert.Equal(SqlContextAnalyzer.CompletionType.IndexColumn, r.Type);
        Assert.Contains("CustomerId", r.ExistingColumns);
        Assert.Contains("Order Date", r.ExistingColumns);
        Assert.Contains("total", r.ExistingColumns);   // case-insensitive, as column names are
        Assert.Equal(3, r.ExistingColumns.Count);
    }

    [Theory]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) WHERE ")]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) WHERE Sh")]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) INCLUDE (Total) WHERE ")]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId)\nINCLUDE (Total)\nWHERE ShippedDate IS NULL AND ")]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) WHERE Status IN (1, 2) OR [Ord")]
    public void Analyze_InFilterPredicateSlot_ReturnsIndexColumn(string sql)
    {
        var r = AnalyzeAtEnd(sql);
        Assert.Equal(SqlContextAnalyzer.CompletionType.IndexColumn, r.Type);
        Assert.Equal("dbo", r.TargetSchema);
        Assert.Equal("Orders", r.TargetTable);
        Assert.Empty(r.ExistingColumns);   // "Qty >= 1 AND Qty < 10" repeats a column legitimately
    }

    [Theory]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) WHERE ShippedDate ")]     // operator next
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) WHERE Qty >= ")]         // constant next
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) WHERE Status IN (1, ")]  // constant list
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) WHERE Qty > 0 WITH (ONLINE = ON, ")]
    [InlineData("CREATE INDEX IX_Orders ON dbo.Orders (CustomerId) WHERE Qty > 0; SELECT 1 WHERE ")]
    public void Analyze_OutsideFilterSlot_IsNotIndexColumn(string sql)
    {
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.IndexColumn, AnalyzeAtEnd(sql).Type);
    }

    [Theory]
    [InlineData("CREATE INDEX IX_Orders ON ")]
    [InlineData("CREATE UNIQUE NONCLUSTERED INDEX IX_Orders ON Ord")]
    [InlineData("CREATE INDEX IX_Orders ON dbo.")]
    [InlineData("CREATE STATISTICS ST ON dbo.Ord")]
    public void Analyze_AfterIndexOn_ReturnsTableName(string sql)
    {
        Assert.Equal(SqlContextAnalyzer.CompletionType.TableName, AnalyzeAtEnd(sql).Type);
    }

    [Fact]
    public void Analyze_JoinOnAliasDot_StillColumnAfterDot()
    {
        // The INDEX ... ON qualifier rule must not swallow an ordinary "ON o." join predicate.
        var r = AnalyzeAtEnd("SELECT * FROM dbo.Orders o JOIN dbo.Customer c ON o.");
        Assert.Equal(SqlContextAnalyzer.CompletionType.ColumnAfterDot, r.Type);
        Assert.Equal("o", r.DotPrefix);
    }
}
