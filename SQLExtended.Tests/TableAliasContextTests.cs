using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

/// <summary>
/// The table position that takes an alias ("FROM dbo.Company c"). An alias offered where the grammar has no
/// room for one inserts SQL that does not parse, with the parser blaming whatever comes next — so every position
/// is checked twice: what the analyzer says, and what ScriptDom does with "dbo.T t" committed there.
/// </summary>
public class TableAliasContextTests
{
    [Theory]
    [InlineData("SELECT * FROM ", " WHERE t.Id = 1")]
    [InlineData("SELECT * FROM dbo.A a INNER JOIN ", " ON t.Id = a.Id")]
    [InlineData("SELECT * FROM dbo.A a LEFT OUTER JOIN ", " ON t.Id = a.Id")]
    [InlineData("SELECT * FROM dbo.A a CROSS JOIN ", "")]
    [InlineData("UPDATE t SET Id = 1 FROM ", "")]
    [InlineData("DELETE t FROM ", "")]
    [InlineData("SELECT Id INTO #x FROM ", "")]
    public void AliasPositions_AcceptAlias_AndParse(string before, string after)
    {
        Assert.True(Analyze(before).AcceptsAlias);
        Assert.True(Parses(before + "dbo.T t" + after));
    }

    [Theory]
    [InlineData("INSERT INTO ", " (Id) VALUES (1)")]
    [InlineData("UPDATE ", " SET Id = 1")]
    [InlineData("DELETE FROM ", " WHERE Id = 1")]
    [InlineData("DELETE TOP (10) FROM ", " WHERE Id = 1")]
    [InlineData("TRUNCATE TABLE ", "")]
    public void NonAliasPositions_RejectAlias_AndAliasDoesNotParse(string before, string after)
    {
        Assert.False(Analyze(before).AcceptsAlias);
        Assert.False(Parses(before + "dbo.T t" + after));
    }

    [Fact]
    public void QualifiedPartial_StillAcceptsAlias()
    {
        Assert.True(Analyze("SELECT * FROM dbo.Comp").AcceptsAlias);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("\r", false)]
    [InlineData(" WHERE c.ACTIVE = 1", false)]
    [InlineData(" WITH (NOLOCK)", false)]
    [InlineData(" INNER JOIN dbo.X x ON 1 = 1", false)]
    [InlineData(";", false)]
    [InlineData(")", false)]
    [InlineData(" GO", false)]
    [InlineData("any WHERE x = 1", false)]       // rest of the name under the cursor, then a keyword
    [InlineData(" c WHERE c.ACTIVE = 1", true)]
    [InlineData("any c", true)]
    [InlineData(" AS c", true)]
    [InlineData(" [c]", true)]
    [InlineData("dbo.Company c", true)]        // cursor before an existing reference
    public void IsFollowedByAlias(string textAfter, bool expected)
    {
        Assert.Equal(expected, JoinClauseBuilder.IsFollowedByAlias(textAfter));
    }

    private static SqlContextAnalyzer.AnalysisResult Analyze(string sql)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.TableName, result.Type);
        return result;
    }

    private static bool Parses(string sql)
    {
        using var reader = new StringReader(sql);
        new TSql170Parser(true).Parse(reader, out var errors);
        return errors.Count == 0;
    }
}
