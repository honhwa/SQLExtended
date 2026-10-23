using Microsoft.SqlServer.TransactSql.ScriptDom;
using SQLExtended.Qualify;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace SQLExtended.Tests.Qualify;

/// <summary>
/// Tests for <c>ColumnQualifier.ExpandStar</c> — Tab after a SELECT-list <c>*</c> (GitHub issue #4).
///
/// The caret is marked with <c>|</c> in each input. Every output is re-parsed, and the edits the editor applies are
/// checked to produce the same text as <see cref="StarExpansionResult.Sql"/>, since the editor never sees that string.
/// </summary>
public class StarExpansionTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, string[]> Schema = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Person"] = ["PersonId", "FName", "LName", "AddressId"],
        ["Address"] = ["AddressId", "City"],
        ["Warehouse"] = ["Id", "Ongoing Qty", "Order"],
        ["#Staging"] = ["StagingId", "Payload"],
        ["@Ids"] = ["Id"],
    };

    private static IReadOnlyCollection<string> Lookup(QualifierTableName t) =>
        t.Server == null && (t.Schema == null || t.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase)) && Schema.TryGetValue(t.Name, out var cols) ? cols : null;

    private static readonly StarListLayout SameLine = new();

    private StarExpansionResult Run(string sqlWithCaret, StarListLayout layout = null)
    {
        int caret = sqlWithCaret.IndexOf('|');
        string sql = sqlWithCaret.Remove(caret, 1);
        var result = ColumnQualifier.ExpandStar(sql, caret, Lookup, layout ?? SameLine);
        output.WriteLine(result.Success ? result.Sql : "FAILED: " + result.ErrorMessage);
        if (!result.Success)
            return result;

        new TSql170Parser(true).Parse(new StringReader(result.Sql), out var errors);
        Assert.Empty(errors);

        var applied = new StringBuilder(sql);
        foreach (var (start, length, text) in result.Edits.OrderByDescending(e => e.Start))
            applied.Remove(start, length).Insert(start, text);
        Assert.Equal(result.Sql, applied.ToString());
        return result;
    }

    private string Expand(string sqlWithCaret, StarListLayout layout = null)
    {
        var result = Run(sqlWithCaret, layout);
        Assert.True(result.Success, result.ErrorMessage);
        return result.Sql;
    }

    private void AssertRefused(string sqlWithCaret, string reason = null)
    {
        var result = Run(sqlWithCaret);
        Assert.False(result.Success);
        if (reason != null)
            Assert.Contains(reason, result.ErrorMessage);
    }

    [Fact]
    public void IssueExample_AliasesTheTableAndPrefixesEveryColumn()
    {
        var result = Run("SELECT *| \n  FROM dbo.Person");
        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("SELECT p.PersonId, p.FName, p.LName, p.AddressId \n  FROM dbo.Person p", result.Sql);
        Assert.Equal(4, result.ColumnsExpanded);
        Assert.Equal(1, result.TablesAliased);
    }

    [Fact]
    public void ExpansionEnd_IsJustAfterTheLastColumn()
    {
        var result = Run("SELECT *| FROM dbo.Person WHERE 1 = 1");
        Assert.EndsWith("p.AddressId", result.Sql.Substring(0, result.ExpansionEnd));
    }

    [Fact]
    public void Stacked_LeadingCommas_PullTheCommaBackUnderTheFirstColumn()
    {
        var layout = new StarListLayout { Stacked = true, LeadingCommas = true, Indent = StarListLayout.IndentFor("SELECT "), NewLine = "\n" };
        Assert.Equal("SELECT p.PersonId\n     , p.FName\n     , p.LName\n     , p.AddressId\n  FROM dbo.Person p", Expand("SELECT *|\n  FROM dbo.Person", layout));
    }

    [Fact]
    public void Stacked_LeadingCommas_KeepIndent()
    {
        var layout = new StarListLayout { Stacked = true, LeadingCommas = true, LeadingCommaKeepIndent = true, Indent = "\t", NewLine = "\n" };
        Assert.Equal("SELECT\n\tp.PersonId\n\t, p.FName\n\t, p.LName\n\t, p.AddressId\nFROM Person p", Expand("SELECT\n\t*|\nFROM Person", layout));
    }

    [Fact]
    public void Stacked_TrailingCommas()
    {
        var layout = new StarListLayout { Stacked = true, Indent = "       ", NewLine = "\n" };
        Assert.Equal("SELECT p.PersonId,\n       p.FName,\n       p.LName,\n       p.AddressId FROM Person p", Expand("SELECT *| FROM Person", layout));
    }

    [Fact]
    public void IndentFor_KeepsTabsAndBlanksEverythingElse()
    {
        Assert.Equal("\t       ", StarListLayout.IndentFor("\tSELECT "));
    }

    [Fact]
    public void Join_ExpandsEveryTableInFromOrder_AndFollowsTheNewAliasesIntoTheOn()
    {
        Assert.Equal(
            "SELECT p.PersonId, p.FName, p.LName, p.AddressId, a.AddressId, a.City FROM Person p JOIN Address a ON p.AddressId = a.AddressId",
            Expand("SELECT *| FROM Person JOIN Address ON Person.AddressId = Address.AddressId"));
    }

    [Fact]
    public void ExistingAlias_IsUsed()
    {
        Assert.Equal("SELECT per.PersonId, per.FName, per.LName, per.AddressId FROM dbo.Person AS per", Expand("SELECT *| FROM dbo.Person AS per"));
    }

    [Fact]
    public void AliasStar_ExpandsOnlyThatTable_AndLeavesTheOthersAlone()
    {
        Assert.Equal(
            "SELECT a.AddressId, a.City FROM Person JOIN Address a ON Person.AddressId = a.AddressId",
            Expand("SELECT a.*| FROM Person JOIN Address a ON Person.AddressId = a.AddressId"));
    }

    [Fact]
    public void TableNameStar_GivesThatTableAnAlias()
    {
        Assert.Equal(
            "SELECT a.AddressId, a.City, Person.FName FROM Person JOIN Address a ON Person.AddressId = a.AddressId",
            Expand("SELECT Address.*|, Person.FName FROM Person JOIN Address ON Person.AddressId = Address.AddressId"));
    }

    [Fact]
    public void OtherColumns_AreLeftAsWritten()
    {
        Assert.Equal(
            "SELECT p.PersonId, p.FName, p.LName, p.AddressId, FName FROM Person p WHERE LName = 'x' ORDER BY FName",
            Expand("SELECT *|, FName FROM Person WHERE LName = 'x' ORDER BY FName"));
    }

    [Fact]
    public void SecondStatement_IsTheOneExpanded()
    {
        Assert.Equal(
            "SELECT * FROM Address;\nSELECT p.PersonId, p.FName, p.LName, p.AddressId FROM Person p;",
            Expand("SELECT * FROM Address;\nSELECT *| FROM Person;"));
    }

    [Fact]
    public void Subquery_ExpandsOnlyTheInnerStar()
    {
        Assert.Equal(
            "SELECT * FROM Person WHERE EXISTS (SELECT a.AddressId, a.City FROM Address a WHERE a.AddressId = Person.AddressId)",
            Expand("SELECT * FROM Person WHERE EXISTS (SELECT *| FROM Address WHERE Address.AddressId = Person.AddressId)"));
    }

    [Fact]
    public void DerivedTable_ExpandsToItsSelectList()
    {
        Assert.Equal("SELECT d.FName, d.L FROM (SELECT FName, LName AS L FROM Person) d", Expand("SELECT *| FROM (SELECT FName, LName AS L FROM Person) d"));
    }

    [Fact]
    public void DerivedTable_WithAnUnnamedColumn_IsRefused()
    {
        AssertRefused("SELECT *| FROM (SELECT FName, COUNT(*) FROM Person GROUP BY FName) d", "not known");
    }

    [Fact]
    public void Cte_ExpandsToItsColumns()
    {
        Assert.Equal(
            "WITH c AS (SELECT FName FROM Person) SELECT c2.FName FROM c c2",
            Expand("WITH c AS (SELECT FName FROM Person) SELECT *| FROM c"));
    }

    [Fact]
    public void NamesThatNeedIt_AreBracketed()
    {
        Assert.Equal("SELECT w.Id, w.[Ongoing Qty], w.[Order] FROM Warehouse w", Expand("SELECT *| FROM Warehouse"));
    }

    [Fact]
    public void TempTableAndTableVariable_AreAliased()
    {
        Assert.Equal("SELECT s.StagingId, s.Payload FROM #Staging s", Expand("SELECT *| FROM #Staging"));
        Assert.Equal("SELECT i.Id FROM @Ids i", Expand("SELECT *| FROM @Ids"));
    }

    [Fact]
    public void UnknownTable_IsRefused_AndSaysWhich()
    {
        AssertRefused("SELECT *| FROM Person JOIN Mystery m ON m.Id = Person.PersonId", "the columns of m are not known");
    }

    [Fact]
    public void Count_IsNotAStar()
    {
        AssertRefused("SELECT COUNT(*|) FROM Person", "not after a *");
    }

    [Fact]
    public void Multiplication_IsNotAStar()
    {
        AssertRefused("SELECT PersonId *| 2 FROM Person", "not after a *");
    }

    [Fact]
    public void NoFrom_IsRefused()
    {
        AssertRefused("SELECT *|", null);
    }

    [Fact]
    public void Unparseable_IsRefused()
    {
        AssertRefused("SELECT *| FROM Person WHERE", "does not parse");
    }
}
