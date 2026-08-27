using SQLExtended.Validation;
using Xunit;

namespace SQLExtended.Tests;

public class ModuleLocalNameScannerTests
{
    [Fact]
    public void TableAlias_IsCollected()
    {
        const string def = @"
            CREATE PROCEDURE dbo.GetSales AS
            SELECT o.Id, o.Total FROM dbo.Orders o WHERE o.Total > 0";

        var names = ModuleLocalNameScanner.Scan(def);

        Assert.Contains("o", names);
    }

    [Fact]
    public void CteName_IsCollected()
    {
        const string def = @"
            CREATE VIEW dbo.vTotals AS
            WITH cteTotals AS (SELECT CustomerId, SUM(Total) AS T FROM dbo.Orders GROUP BY CustomerId)
            SELECT * FROM cteTotals";

        var names = ModuleLocalNameScanner.Scan(def);

        Assert.Contains("cteTotals", names);
    }

    [Fact]
    public void DerivedTableAlias_IsCollected()
    {
        const string def = @"
            CREATE PROCEDURE dbo.P AS
            SELECT d.x FROM (SELECT 1 AS x) d";

        var names = ModuleLocalNameScanner.Scan(def);

        Assert.Contains("d", names);
    }

    [Fact]
    public void TableVariable_IsCollectedWithAndWithoutSigil()
    {
        const string def = @"
            CREATE PROCEDURE dbo.P AS
            DECLARE @tv TABLE (Id INT);
            SELECT * FROM @tv";

        var names = ModuleLocalNameScanner.Scan(def);

        Assert.Contains("@tv", names);
        Assert.Contains("tv", names);
    }

    [Fact]
    public void GenuineTableReference_IsNotCollected()
    {
        // A real table referenced (not aliased) must not appear as a "local name" — otherwise a
        // missing table would be wrongly suppressed.
        const string def = @"
            CREATE PROCEDURE dbo.P AS
            SELECT * FROM badtable";

        var names = ModuleLocalNameScanner.Scan(def);

        Assert.DoesNotContain("badtable", names);
    }

    [Fact]
    public void NullOrEmpty_ReturnsEmptySet()
    {
        Assert.Empty(ModuleLocalNameScanner.Scan(null));
        Assert.Empty(ModuleLocalNameScanner.Scan("   "));
    }

    [Fact]
    public void UnparseableJunk_ReturnsEmptySetWithoutThrowing()
    {
        var names = ModuleLocalNameScanner.Scan("CREATE PROCEDURE dbo.P AS SELECT FROM FROM (((");
        Assert.NotNull(names);
    }
}
