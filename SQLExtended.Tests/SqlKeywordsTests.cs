using SQLExtended.IntelliSense;
using System.Linq;
using Xunit;

namespace SQLExtended.Tests;

public class SqlKeywordsTests
{
    [Fact]
    public void DetectContext_EmptyText_ReturnsStatementStart()
    {
        var context = SqlKeywords.DetectContext("");
        Assert.True((context & KeywordContext.StatementStart) != 0);
    }

    [Fact]
    public void DetectContext_AfterSelect_ReturnsAfterSelect()
    {
        var context = SqlKeywords.DetectContext("SELECT ");
        Assert.True((context & KeywordContext.AfterSelect) != 0);
    }

    [Fact]
    public void DetectContext_AfterSelectDistinct_ReturnsAfterSelect()
    {
        var context = SqlKeywords.DetectContext("SELECT DISTINCT ");
        Assert.True((context & KeywordContext.AfterSelect) != 0);
    }

    [Fact]
    public void DetectContext_AfterFrom_ReturnsAfterFrom()
    {
        var context = SqlKeywords.DetectContext("SELECT * FROM Customers c ");
        Assert.True((context & KeywordContext.AfterFrom) != 0);
    }

    [Fact]
    public void DetectContext_AfterWhere_ReturnsAfterWhere()
    {
        var context = SqlKeywords.DetectContext("SELECT * FROM Customers WHERE ");
        Assert.True((context & KeywordContext.AfterWhere) != 0);
    }

    [Fact]
    public void DetectContext_AfterAnd_ReturnsAfterWhere()
    {
        var context = SqlKeywords.DetectContext("SELECT * FROM Customers WHERE Active = 1 AND ");
        Assert.True((context & KeywordContext.AfterWhere) != 0);
    }

    [Fact]
    public void DetectContext_AfterOrderBy_ReturnsAfterOrderBy()
    {
        var context = SqlKeywords.DetectContext("SELECT * FROM Customers ORDER BY ");
        Assert.True((context & KeywordContext.AfterOrderBy) != 0);
    }

    [Fact]
    public void DetectContext_AfterGroupBy_ReturnsAfterGroupBy()
    {
        var context = SqlKeywords.DetectContext("SELECT * FROM Customers GROUP BY ");
        Assert.True((context & KeywordContext.AfterGroupBy) != 0);
    }

    [Fact]
    public void DetectContext_AfterSemicolon_ReturnsStatementStart()
    {
        var context = SqlKeywords.DetectContext("SELECT 1;");
        Assert.True((context & KeywordContext.StatementStart) != 0);
    }

    [Fact]
    public void DetectContext_AfterUpdate_ReturnsAfterUpdate()
    {
        var context = SqlKeywords.DetectContext("UPDATE Customers ");
        Assert.True((context & KeywordContext.AfterUpdate) != 0);
    }

    [Fact]
    public void DetectContext_AfterCommaInSelect_ReturnsExpressionOrAfterSelect()
    {
        var context = SqlKeywords.DetectContext("SELECT col1,");
        Assert.True((context & KeywordContext.AfterSelect) != 0 ||
                     (context & KeywordContext.Expression) != 0);
    }

    [Fact]
    public void GetKeywordsForContext_StatementStart_IncludesSelectAndInsert()
    {
        var keywords = SqlKeywords.GetKeywordsForContext(KeywordContext.StatementStart);
        var texts = keywords.Select(k => k.Text).ToList();

        Assert.Contains("SELECT", texts);
        Assert.Contains("INSERT INTO", texts);
        Assert.Contains("UPDATE", texts);
        Assert.Contains("DELETE", texts);
        Assert.Contains("DECLARE", texts);
        Assert.Contains("IF", texts);
        Assert.Contains("BEGIN", texts);
    }

    [Fact]
    public void GetKeywordsForContext_AfterSelect_IncludesFromAndDistinct()
    {
        var keywords = SqlKeywords.GetKeywordsForContext(KeywordContext.AfterSelect);
        var texts = keywords.Select(k => k.Text).ToList();

        Assert.Contains("FROM", texts);
        Assert.Contains("DISTINCT", texts);
        Assert.Contains("TOP", texts);
        Assert.Contains("INTO", texts);
    }

    [Fact]
    public void GetKeywordsForContext_AfterFrom_IncludesJoinsAndWhere()
    {
        var keywords = SqlKeywords.GetKeywordsForContext(KeywordContext.AfterFrom);
        var texts = keywords.Select(k => k.Text).ToList();

        Assert.Contains("INNER JOIN", texts);
        Assert.Contains("LEFT JOIN", texts);
        Assert.Contains("WHERE", texts);
        Assert.Contains("ORDER BY", texts);
        Assert.Contains("GROUP BY", texts);
    }

    [Fact]
    public void GetKeywordsForContext_AfterWhere_IncludesAndOrOperators()
    {
        var keywords = SqlKeywords.GetKeywordsForContext(KeywordContext.AfterWhere);
        var texts = keywords.Select(k => k.Text).ToList();

        Assert.Contains("AND", texts);
        Assert.Contains("OR", texts);
        Assert.Contains("IN", texts);
        Assert.Contains("BETWEEN", texts);
        Assert.Contains("LIKE", texts);
        Assert.Contains("IS NULL", texts);
        Assert.Contains("IS NOT NULL", texts);
    }

    [Fact]
    public void GetKeywordsForContext_AfterOrderBy_IncludesAscDesc()
    {
        var keywords = SqlKeywords.GetKeywordsForContext(KeywordContext.AfterOrderBy);
        var texts = keywords.Select(k => k.Text).ToList();

        Assert.Contains("ASC", texts);
        Assert.Contains("DESC", texts);
        Assert.Contains("OFFSET", texts);
    }

    [Fact]
    public void GetKeywordsForContext_StatementStart_ExcludesAscDesc()
    {
        var keywords = SqlKeywords.GetKeywordsForContext(KeywordContext.StatementStart);
        var texts = keywords.Select(k => k.Text).ToList();

        Assert.DoesNotContain("ASC", texts);
        Assert.DoesNotContain("DESC", texts);
        Assert.DoesNotContain("ON", texts);
    }

    [Fact]
    public void GetKeywordsForContext_AfterInsert_IncludesValues()
    {
        var keywords = SqlKeywords.GetKeywordsForContext(KeywordContext.AfterInsert);
        var texts = keywords.Select(k => k.Text).ToList();

        Assert.Contains("VALUES", texts);
        Assert.Contains("DEFAULT VALUES", texts);
    }
}
