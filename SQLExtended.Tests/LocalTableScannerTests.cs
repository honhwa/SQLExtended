using System.Linq;
using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

public class LocalTableScannerTests
{
    [Fact]
    public void Scan_CreateLocalTempTable_CapturesNameAndColumns()
    {
        const string sql = @"
            CREATE TABLE #Orders
            (
                OrderId    int NOT NULL IDENTITY(1,1),
                CustomerId int NULL,
                Total      decimal(18,2) NOT NULL
            )
            SELECT * FROM #Orders";

        var tables = LocalTableScanner.Scan(sql);

        var t = Assert.Single(tables);
        Assert.Equal("#Orders", t.Name);
        Assert.False(t.IsGlobal);
        Assert.False(t.IsTableVariable);
        Assert.Equal(new[] { "OrderId", "CustomerId", "Total" }, t.Columns.Select(c => c.ColumnName).ToArray());

        var orderId = t.Columns[0];
        Assert.Equal("int", orderId.DataType);
        Assert.False(orderId.IsNullable);
        Assert.True(orderId.IsIdentity);

        Assert.Equal("decimal(18,2)", t.Columns[2].DataType);
    }

    [Fact]
    public void Scan_GlobalTempTable_IsFlaggedGlobal()
    {
        const string sql = "CREATE TABLE ##Shared (Id int)";
        var t = Assert.Single(LocalTableScanner.Scan(sql));
        Assert.Equal("##Shared", t.Name);
        Assert.True(t.IsGlobal);
    }

    [Fact]
    public void Scan_TableVariable_CapturesNameAndColumns()
    {
        const string sql = "DECLARE @Items TABLE (Id int NOT NULL, Name nvarchar(50) NULL)";
        var t = Assert.Single(LocalTableScanner.Scan(sql));
        Assert.Equal("@Items", t.Name);
        Assert.True(t.IsTableVariable);
        Assert.Equal(new[] { "Id", "Name" }, t.Columns.Select(c => c.ColumnName).ToArray());
        Assert.Equal("nvarchar(50)", t.Columns[1].DataType);
    }

    [Fact]
    public void Scan_SelectInto_CapturesNameAndNamedColumns()
    {
        const string sql = "SELECT o.OrderId, Total = o.Amount INTO #Tmp FROM dbo.Orders o";
        var t = Assert.Single(LocalTableScanner.Scan(sql));
        Assert.Equal("#Tmp", t.Name);
        Assert.Equal(new[] { "OrderId", "Total" }, t.Columns.Select(c => c.ColumnName).ToArray());
    }

    [Fact]
    public void Scan_IgnoresRegularTables()
    {
        const string sql = "CREATE TABLE dbo.Customer (Id int)";
        Assert.Empty(LocalTableScanner.Scan(sql));
    }

    [Fact]
    public void Scan_MultipleLocalTables_AllCaptured()
    {
        const string sql = @"
            CREATE TABLE #A (Id int);
            DECLARE @B TABLE (Id int);
            SELECT 1 AS X INTO #C;";

        var names = LocalTableScanner.Scan(sql).Select(t => t.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "#A", "#C", "@B" }, names);
    }

    [Theory]
    [InlineData("#temp", true)]
    [InlineData("@var", true)]
    [InlineData("##global", true)]
    [InlineData("dbo", false)]
    [InlineData("Orders", false)]
    [InlineData("", false)]
    public void IsLocalName_DetectsSigils(string name, bool expected)
    {
        Assert.Equal(expected, LocalTableScanner.IsLocalName(name));
    }

    [Fact]
    public void Scan_PartialScript_StillCapturesCompletedDefinitions()
    {
        // The user is mid-typing a SELECT below a finished temp table.
        const string sql = "CREATE TABLE #t (Id int, Name varchar(10))\nSELECT Id, Na FROM #t WHERE ";
        var t = Assert.Single(LocalTableScanner.Scan(sql));
        Assert.Equal("#t", t.Name);
        Assert.Equal(new[] { "Id", "Name" }, t.Columns.Select(c => c.ColumnName).ToArray());
    }
}
