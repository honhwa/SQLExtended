using Microsoft.SqlServer.TransactSql.ScriptDom;
using SQLExtended.IntelliSense;
using System.IO;
using System.Linq;
using Xunit;

namespace SQLExtended.Tests;

public class QueryHintContextTests
{
    private static SqlContextAnalyzer.AnalysisResult AnalyzeAtEnd(string sql)
        => SqlContextAnalyzer.Analyze(sql, sql.Length);

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders OPTION (")]
    [InlineData("SELECT * FROM dbo.Orders OPTION(")]
    [InlineData("SELECT * FROM dbo.Orders\nOPTION (RECOM")]
    [InlineData("SELECT * FROM dbo.Orders OPTION (MAXDOP 1, ")]
    [InlineData("SELECT * FROM dbo.Orders WHERE Id = @id OPTION (OPTIMIZE FOR (@id = 1), ")]
    [InlineData("SELECT * FROM dbo.Orders OPTION (USE HINT ('DISABLE_PARAMETER_SNIFFING'), ")]
    [InlineData("UPDATE dbo.Orders SET Total = 0 OPTION (")]
    public void Analyze_AtOptionHintSlot_ReturnsQueryHint(string sql)
    {
        Assert.Equal(SqlContextAnalyzer.CompletionType.QueryHint, AnalyzeAtEnd(sql).Type);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders OPTION (MAXDOP ")]                    // the hint's argument
    [InlineData("SELECT * FROM dbo.Orders OPTION (OPTIMIZE FOR (")]             // a parameter list
    [InlineData("SELECT * FROM dbo.Orders OPTION (RECOMPILE) ")]                // clause closed
    [InlineData("SELECT * FROM dbo.Orders WHERE Name = 'OPTION (' + ")]         // inside a string earlier
    [InlineData("SELECT dbo.MyOPTION(")]                                        // not the keyword
    public void Analyze_OutsideOptionHintSlot_IsNotQueryHint(string sql)
    {
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.QueryHint, AnalyzeAtEnd(sql).Type);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders OPTION (USE HINT (")]
    [InlineData("SELECT * FROM dbo.Orders OPTION (USE HINT ('")]
    [InlineData("SELECT * FROM dbo.Orders OPTION (USE HINT ('DISA")]
    [InlineData("SELECT * FROM dbo.Orders OPTION (USE HINT (N'")]
    [InlineData("SELECT * FROM dbo.Orders OPTION (RECOMPILE, USE HINT ('DISABLE_OPTIMIZER_ROWGOAL', ")]
    [InlineData("SELECT * FROM dbo.Orders OPTION (USE HINT ('DISABLE_OPTIMIZER_ROWGOAL', 'ENA")]
    public void Analyze_AtUseHintSlot_ReturnsUseHintName(string sql)
    {
        Assert.Equal(SqlContextAnalyzer.CompletionType.UseHintName, AnalyzeAtEnd(sql).Type);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders OPTION (USE HINT ('DISABLE_OPTIMIZER_ROWGOAL' ")]
    [InlineData("SELECT * FROM dbo.Orders OPTION (USE HINT ('DISABLE_OPTIMIZER_ROWGOAL') ")]
    public void Analyze_OutsideUseHintSlot_IsNotUseHintName(string sql)
    {
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.UseHintName, AnalyzeAtEnd(sql).Type);
    }

    // A wrong entry in either list inserts SQL that does not parse, and nothing reports that until the query runs.

    [Fact]
    public void EveryQueryHint_WithItsArgument_Parses()
    {
        foreach (var hint in SqlQueryHints.Hints.Select(h => h.Keyword))
        {
            string clause = hint switch
            {
                "MAXDOP" or "MAXRECURSION" or "FAST" or "QUERYTRACEON" => $"{hint} 1",
                "MAX_GRANT_PERCENT" or "MIN_GRANT_PERCENT" => $"{hint} = 10",
                "OPTIMIZE FOR" => "OPTIMIZE FOR (@p = 1)",
                "USE HINT" => "USE HINT ('DISABLE_PARAMETER_SNIFFING')",
                "USE PLAN" => "USE PLAN N'<ShowPlanXML/>'",
                "TABLE HINT" => "TABLE HINT (dbo.T, NOLOCK)",
                _ => hint,
            };
            AssertParses($"DECLARE @p int; SELECT * FROM dbo.T WHERE c = @p OPTION ({clause});");
        }
    }

    [Fact]
    public void EveryUseHintName_Parses()
    {
        foreach (var name in SqlQueryHints.UseHintNames.Select(h => h.Keyword))
            AssertParses($"SELECT * FROM dbo.T OPTION (USE HINT ('{name}'));");
    }

    private static void AssertParses(string sql)
    {
        new TSql170Parser(true).Parse(new StringReader(sql), out var errors);
        Assert.True(errors.Count == 0, $"{sql}\n{string.Join("\n", errors.Select(e => e.Message))}");
    }
}
