using System.Linq;
using SQLExtended.Safety;
using Xunit;

namespace SQLExtended.Tests.Safety;

/// <summary>
/// A miss here runs an UPDATE or DELETE across a whole table with no question asked, and on screen it looks exactly
/// like the guard working — so the flagged cases are pinned as carefully as the exemptions.
/// </summary>
public class UnsafeDmlAnalyzerTests
{
    private static UnsafeDmlResult Analyze(string sql) => UnsafeDmlAnalyzer.Analyze(sql);

    [Theory]
    [InlineData("UPDATE dbo.Customers SET Active = 0")]
    [InlineData("DELETE FROM dbo.Customers")]
    [InlineData("DELETE dbo.Customers")]
    [InlineData("update Customers set Active = 0 -- WHERE Id = 1")]          // a WHERE in a comment is not a WHERE
    [InlineData("UPDATE Customers SET Note = 'WHERE Id = 1'")]                // nor is one in a string
    [InlineData("DELETE TOP (1000) FROM dbo.Log")]                            // TOP in a loop purges everything
    [InlineData("UPDATE c SET Active = 0 FROM dbo.Customers c LEFT JOIN dbo.Orders o ON o.CustomerId = c.Id")]
    [InlineData("DELETE c FROM dbo.Customers c CROSS JOIN dbo.Orders o")]
    [InlineData("DELETE c FROM dbo.Customers c, dbo.Orders o")]
    [InlineData("WITH x AS (SELECT * FROM dbo.Customers) DELETE FROM x")]
    public void StatementWithoutWhere_IsFlagged(string sql) => Assert.Single(Analyze(sql).Findings);

    [Theory]
    [InlineData("UPDATE dbo.Customers SET Active = 0 WHERE Id = 1")]
    [InlineData("DELETE FROM dbo.Customers WHERE Id = 1")]
    [InlineData("DELETE FROM #Work")]
    [InlineData("UPDATE #Work SET Done = 1")]
    [InlineData("DECLARE @t TABLE (Id int); DELETE FROM @t")]
    [InlineData("UPDATE c SET Active = 0 FROM dbo.Customers c INNER JOIN dbo.Orders o ON o.CustomerId = c.Id")]
    [InlineData("DELETE c FROM dbo.Customers c JOIN dbo.Orders o ON o.CustomerId = c.Id")]
    [InlineData("SELECT * FROM dbo.Customers")]
    [InlineData("INSERT INTO dbo.Customers (Name) VALUES ('a')")]
    [InlineData("")]
    public void SafeStatement_IsNotFlagged(string sql) => Assert.Empty(Analyze(sql).Findings);

    [Theory]
    [InlineData("CREATE PROCEDURE dbo.Purge AS DELETE FROM dbo.Log")]
    [InlineData("ALTER PROCEDURE dbo.Purge AS BEGIN UPDATE dbo.Log SET Archived = 1 END")]
    [InlineData("CREATE OR ALTER PROCEDURE dbo.Purge AS DELETE FROM dbo.Log")]
    [InlineData("CREATE TRIGGER trg ON dbo.Orders AFTER INSERT AS UPDATE dbo.Stats SET Total = Total + 1")]
    [InlineData("ALTER TRIGGER trg ON dbo.Orders AFTER INSERT AS DELETE FROM dbo.Stats")]
    [InlineData("CREATE FUNCTION dbo.f() RETURNS @r TABLE (Id int) AS BEGIN DELETE FROM @r; UPDATE @r SET Id = 1; RETURN; END")]
    public void ModuleBody_IsDefinedNotExecuted_SoNotFlagged(string sql) => Assert.Empty(Analyze(sql).Findings);

    [Fact]
    public void WhereCurrentOf_IsAWhereClause()
    {
        const string sql = "DECLARE c CURSOR FOR SELECT Id FROM dbo.Customers; OPEN c; FETCH NEXT FROM c; DELETE FROM dbo.Customers WHERE CURRENT OF c;";
        Assert.Empty(Analyze(sql).Findings);
    }

    [Fact]
    public void StatementInsideAControlFlowBlock_IsStillFlagged()
    {
        Assert.Single(Analyze("IF 1 = 1 BEGIN DELETE FROM dbo.Customers END").Findings);
        Assert.Single(Analyze("WHILE 1 = 1 BEGIN DELETE TOP (100) FROM dbo.Log; IF @@ROWCOUNT = 0 BREAK; END").Findings);
    }

    [Fact]
    public void EveryOffendingStatementIsReported_WithLineAndTarget()
    {
        const string sql = "SELECT 1;\r\nUPDATE dbo.A SET X = 1;\r\nGO\r\nDELETE FROM [Sales].[B];\r\nDELETE FROM dbo.C WHERE Id = 1;";
        var findings = Analyze(sql).Findings;

        Assert.Equal(2, findings.Count);
        Assert.Equal(("UPDATE", "dbo.A", 2), (findings[0].Kind, findings[0].Target, findings[0].Line));
        Assert.Equal(("DELETE", "Sales.B", 4), (findings[1].Kind, findings[1].Target, findings[1].Line));
        Assert.StartsWith("UPDATE dbo.A SET X = 1", findings[0].Snippet);
    }

    [Fact]
    public void ScriptThatDoesNotParse_IsReportedAsUnchecked_NotAsSafe()
    {
        var result = Analyze("DELETE FROM dbo.Customers WHERE");
        Assert.True(result.ParseFailed);
        Assert.Empty(result.Findings);

        Assert.True(Analyze(":setvar db Prod\r\nDELETE FROM $(db).dbo.Customers").ParseFailed);
    }

    [Fact]
    public void LongStatement_SnippetIsCollapsedAndTruncated()
    {
        string sql = "UPDATE dbo.A\r\n   SET " + string.Join(",\r\n       ", Enumerable.Range(1, 40).Select(i => $"C{i} = {i}"));
        string snippet = Analyze(sql).Findings.Single().Snippet;

        Assert.DoesNotContain("\n", snippet);
        Assert.True(snippet.Length <= 101);
        Assert.EndsWith("…", snippet);
    }
}
