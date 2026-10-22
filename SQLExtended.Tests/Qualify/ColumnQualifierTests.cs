using Microsoft.SqlServer.TransactSql.ScriptDom;
using SQLExtended.Qualify;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace SQLExtended.Tests.Qualify;

/// <summary>
/// Tests for <c>ColumnQualifier</c> — Qualify Columns (GitHub issue #3).
///
/// Every wrong answer this pass can give is SQL that still runs: a column prefixed with the wrong table's alias binds
/// to that table when it has a column of the same name, and a select-list alias qualified in ORDER BY sorts by the
/// underlying column instead. So each test asserts the exact text, and <see cref="Run"/> re-parses every output.
/// </summary>
public class ColumnQualifierTests(ITestOutputHelper output)
{
    private static readonly Dictionary<string, string[]> Schema = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Person"] = ["PersonId", "FName", "LName", "AddressId"],
        ["Address"] = ["AddressId", "City", "Street"],
        ["Orders"] = ["OrderId", "PersonId", "OrderDate", "Day"],
        ["OrderLine"] = ["OrderId", "LineNo", "Qty"],
        ["#Staging"] = ["StagingId", "Payload"],
        ["@Ids"] = ["Id"],
    };

    private static IReadOnlyCollection<string> Lookup(QualifierTableName t) =>
        t.Server == null && (t.Schema == null || t.Schema.Equals("dbo", StringComparison.OrdinalIgnoreCase)) && Schema.TryGetValue(t.Name, out var cols) ? cols : null;

    private ColumnQualifierResult Run(string sql)
    {
        var result = ColumnQualifier.Qualify(sql, Lookup);
        output.WriteLine(result.Success ? result.Sql : "FAILED: " + result.ErrorMessage);
        Assert.True(result.Success, result.ErrorMessage);

        new TSql170Parser(true).Parse(new StringReader(result.Sql), out var errors);
        Assert.Empty(errors);
        return result;
    }

    private string Qualify(string sql) => Run(sql).Sql;

    [Fact]
    public void IssueExample_AliasesTablesAndQualifiesColumns()
    {
        const string sql = "SELECT \n  FName\n  , LName\n  , City \nFROM Person \nINNER JOIN Address ON Person.AddressId = Address.AddressId";
        var result = Run(sql);
        Assert.Equal("SELECT \n  p.FName\n  , p.LName\n  , a.City \nFROM Person p \nINNER JOIN Address a ON p.AddressId = a.AddressId", result.Sql);
        Assert.Equal(2, result.TablesAliased);
        Assert.Equal(3, result.ColumnsQualified);
        Assert.Equal(0, result.ColumnsLeft);
    }

    [Fact]
    public void IssueExample_ExistingAliasesAreUsed()
    {
        const string sql = "SELECT FName, LName, City FROM Person p INNER JOIN Address a ON p.AddressId = a.AddressId";
        Assert.Equal("SELECT p.FName, p.LName, a.City FROM Person p INNER JOIN Address a ON p.AddressId = a.AddressId", Qualify(sql));
    }

    [Fact]
    public void ExistingAliasWithAs_IsKept()
    {
        Assert.Equal("SELECT per.FName FROM dbo.Person AS per", Qualify("SELECT FName FROM dbo.Person AS per"));
    }

    [Fact]
    public void RunningTwice_ChangesNothingTheSecondTime()
    {
        string once = Qualify("SELECT FName, City FROM Person JOIN Address ON Person.AddressId = Address.AddressId WHERE City = 'X'");
        var twice = Run(once);
        Assert.Equal(once, twice.Sql);
        Assert.Equal(0, twice.TablesAliased);
        Assert.Equal(0, twice.ColumnsQualified);
    }

    [Fact]
    public void AmbiguousColumn_IsLeftAsWritten()
    {
        var result = Run("SELECT AddressId, City FROM Person p JOIN Address a ON p.AddressId = a.AddressId");
        Assert.Equal("SELECT AddressId, a.City FROM Person p JOIN Address a ON p.AddressId = a.AddressId", result.Sql);
        Assert.Equal(1, result.ColumnsLeft);
    }

    [Fact]
    public void UnknownColumn_IsLeftAsWritten()
    {
        var result = Run("SELECT FName, NoSuchColumn FROM Person");
        Assert.Equal("SELECT p.FName, NoSuchColumn FROM Person p", result.Sql);
        Assert.Equal(1, result.ColumnsLeft);
    }

    [Fact]
    public void TableWithUnknownColumns_StopsBareColumnsInThatScope()
    {
        // FName is Person's as far as we know — but Mystery might have one too, and then the SQL is ambiguous today
        // and binding it to p would be a guess.
        Assert.Equal("SELECT FName FROM Person p CROSS JOIN dbo.Mystery m", Qualify("SELECT FName FROM Person CROSS JOIN dbo.Mystery"));
    }

    [Fact]
    public void QualifiedByTableName_IsRewrittenToTheNewAlias()
    {
        Assert.Equal("SELECT p.FName, p.* FROM dbo.Person p WHERE p.PersonId = 1",
            Qualify("SELECT dbo.Person.FName, Person.* FROM dbo.Person WHERE Person.PersonId = 1"));
    }

    [Fact]
    public void BracketedNames_AreHandled()
    {
        Assert.Equal("SELECT p.[FName] FROM [dbo].[Person] p WHERE p.[PersonId] = 1",
            Qualify("SELECT [FName] FROM [dbo].[Person] WHERE [Person].[PersonId] = 1"));
    }

    [Fact]
    public void SelfJoin_GetsTwoAliases_AndBareColumnsStayAmbiguous()
    {
        var result = Run("SELECT FName FROM Person JOIN Person AS boss ON boss.PersonId = Person.PersonId");
        Assert.Equal("SELECT FName FROM Person p JOIN Person AS boss ON boss.PersonId = p.PersonId", result.Sql);
        Assert.Equal(1, result.ColumnsLeft);
    }

    [Fact]
    public void GeneratedAlias_AvoidsNamesInUse()
    {
        // "p" is already the alias of Orders, and "a" is a table name in the subquery (whose own alias then avoids it too).
        Assert.Equal("SELECT p2.FName FROM Person p2 JOIN dbo.Orders p ON 1 = 1 WHERE EXISTS (SELECT 1 FROM a a2)",
            Qualify("SELECT FName FROM Person JOIN dbo.Orders p ON 1 = 1 WHERE EXISTS (SELECT 1 FROM a)"));
    }

    [Fact]
    public void SeparateStatements_EachStartTheirAliasesAgain()
    {
        Assert.Equal("SELECT p.FName FROM Person p;\nSELECT p.LName FROM Person p;", Qualify("SELECT FName FROM Person;\nSELECT LName FROM Person;"));
    }

    [Fact]
    public void OrderBy_SelectAliasIsNotQualified()
    {
        // ORDER BY City sorts by the alias (which is FName), not by Address.City.
        var result = Run("SELECT FName AS City, LName FROM Person JOIN Address ON Person.AddressId = Address.AddressId ORDER BY City, LName");
        Assert.Equal("SELECT p.FName AS City, p.LName FROM Person p JOIN Address a ON p.AddressId = a.AddressId ORDER BY City, p.LName", result.Sql);
    }

    [Fact]
    public void CorrelatedSubquery_ResolvesInnermostFirst()
    {
        Assert.Equal("SELECT p.FName FROM Person p WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.PersonId = p.PersonId AND p.LName = 'x')",
            Qualify("SELECT FName FROM Person WHERE EXISTS (SELECT 1 FROM Orders WHERE Orders.PersonId = Person.PersonId AND LName = 'x')"));
    }

    [Fact]
    public void CorrelatedSubquery_BareColumnInBothScopesBindsInner()
    {
        // PersonId is in Orders (inner) and Person (outer); SQL Server takes the inner one.
        Assert.Equal("SELECT p.FName FROM Person p WHERE p.PersonId IN (SELECT o.PersonId FROM Orders o)",
            Qualify("SELECT FName FROM Person WHERE PersonId IN (SELECT PersonId FROM Orders)"));
    }

    [Fact]
    public void DerivedTable_ColumnsComeFromItsSelectList()
    {
        Assert.Equal("SELECT x.Total, p.FName FROM Person p JOIN (SELECT o.PersonId, COUNT(*) AS Total FROM Orders o GROUP BY o.PersonId) x ON x.PersonId = p.PersonId",
            Qualify("SELECT Total, FName FROM Person JOIN (SELECT PersonId, COUNT(*) AS Total FROM Orders GROUP BY PersonId) x ON x.PersonId = Person.PersonId"));
    }

    [Fact]
    public void DerivedTable_CannotSeeItsSiblings()
    {
        // FName is only in Person, a sibling of the derived table — it is not visible inside it, so it is left.
        var result = Run("SELECT 1 FROM Person JOIN (SELECT FName FROM Orders) x ON 1 = 1");
        Assert.Equal("SELECT 1 FROM Person p JOIN (SELECT FName FROM Orders o) x ON 1 = 1", result.Sql);
        Assert.Equal(1, result.ColumnsLeft);
    }

    [Fact]
    public void CrossApply_CanSeeTheLeftSide()
    {
        Assert.Equal("SELECT p.FName, x.OrderId FROM Person p CROSS APPLY (SELECT TOP 1 o.OrderId FROM Orders o WHERE o.PersonId = p.PersonId AND p.LName = 'x') x",
            Qualify("SELECT FName, OrderId FROM Person CROSS APPLY (SELECT TOP 1 OrderId FROM Orders WHERE Orders.PersonId = Person.PersonId AND LName = 'x') x"));
    }

    [Fact]
    public void Cte_ColumnsComeFromItsDefinition_AndItCanBeAliased()
    {
        Assert.Equal("WITH Recent AS (SELECT o.PersonId, o.OrderDate FROM Orders o) SELECT p.FName, r.OrderDate FROM Person p JOIN Recent r ON r.PersonId = p.PersonId",
            Qualify("WITH Recent AS (SELECT PersonId, OrderDate FROM Orders) SELECT FName, OrderDate FROM Person JOIN Recent ON Recent.PersonId = Person.PersonId"));
    }

    [Fact]
    public void Cte_WithColumnList()
    {
        Assert.Equal("WITH c (Who) AS (SELECT p.FName FROM Person p) SELECT c2.Who FROM c c2",
            Qualify("WITH c (Who) AS (SELECT FName FROM Person) SELECT Who FROM c"));
    }

    [Fact]
    public void Union_EachBranchIsItsOwnScope_AndTheOrderByIsLeft()
    {
        Assert.Equal("SELECT p.FName FROM Person p UNION SELECT a.City FROM Address a ORDER BY FName",
            Qualify("SELECT FName FROM Person UNION SELECT City FROM Address ORDER BY FName"));
    }

    [Fact]
    public void DatePartArgument_IsNotTreatedAsAColumn()
    {
        // Orders has a column called Day; DATEADD's first argument is still the datepart keyword.
        Assert.Equal("SELECT DATEADD(Day, 1, o.OrderDate), o.Day FROM Orders o", Qualify("SELECT DATEADD(Day, 1, OrderDate), Day FROM Orders"));
    }

    [Fact]
    public void WindowOrderBy_IsAnOrdinaryColumnPosition()
    {
        // OrderDate is not a select alias here; the ORDER BY inside OVER is qualified like any other column.
        Assert.Equal("SELECT ROW_NUMBER() OVER (PARTITION BY o.PersonId ORDER BY o.OrderDate) AS OrderDate FROM Orders o",
            Qualify("SELECT ROW_NUMBER() OVER (PARTITION BY PersonId ORDER BY OrderDate) AS OrderDate FROM Orders"));
    }

    [Fact]
    public void ContainsTable_ColumnListIsLeftBare()
    {
        // The select-list FName is left too: CONTAINSTABLE's own columns (KEY, RANK) are not known here.
        Assert.Equal("SELECT FName FROM Person p JOIN CONTAINSTABLE(Person, (FName, LName), 'x') ct ON ct.[KEY] = p.PersonId",
            Qualify("SELECT FName FROM Person JOIN CONTAINSTABLE(Person, (FName, LName), 'x') ct ON ct.[KEY] = Person.PersonId"));
    }

    [Fact]
    public void TableHint_AliasGoesBeforeWith()
    {
        Assert.Equal("SELECT p.FName FROM Person p WITH (NOLOCK)", Qualify("SELECT FName FROM Person WITH (NOLOCK)"));
    }

    [Fact]
    public void TableSample_AliasGoesBeforeIt()
    {
        Assert.Equal("SELECT p.FName FROM Person p TABLESAMPLE (10 PERCENT)", Qualify("SELECT FName FROM Person TABLESAMPLE (10 PERCENT)"));
    }

    [Fact]
    public void ForSystemTime_AliasGoesAfterIt()
    {
        Assert.Equal("SELECT p.FName FROM Person FOR SYSTEM_TIME AS OF '2026-01-01' p", Qualify("SELECT FName FROM Person FOR SYSTEM_TIME AS OF '2026-01-01'"));
    }

    [Fact]
    public void TempTableAndTableVariable()
    {
        Assert.Equal("SELECT s.Payload, i.Id FROM #Staging s JOIN @Ids i ON i.Id = s.StagingId",
            Qualify("SELECT Payload, Id FROM #Staging JOIN @Ids ON Id = StagingId"));
    }

    [Fact]
    public void DmlTargets_AreNotTouched_ButTheirSubqueriesAre()
    {
        Assert.Equal("UPDATE Person SET FName = 'x' WHERE PersonId IN (SELECT o.PersonId FROM Orders o WHERE o.OrderDate > '2026-01-01')",
            Qualify("UPDATE Person SET FName = 'x' WHERE PersonId IN (SELECT PersonId FROM Orders WHERE OrderDate > '2026-01-01')"));
    }

    [Fact]
    public void InsertSelect_ColumnListIsNotQualified()
    {
        Assert.Equal("INSERT INTO OrderLine (OrderId, Qty) SELECT o.OrderId, 1 FROM Orders o",
            Qualify("INSERT INTO OrderLine (OrderId, Qty) SELECT OrderId, 1 FROM Orders"));
    }

    [Fact]
    public void IfExistsSubquery_IsQualified()
    {
        Assert.Equal("IF EXISTS (SELECT 1 FROM Person p WHERE p.FName = 'x') PRINT 'y';", Qualify("IF EXISTS (SELECT 1 FROM Person WHERE FName = 'x') PRINT 'y';"));
    }

    [Fact]
    public void ReservedWordColumn_IsPrefixedWithoutRequoting()
    {
        Assert.Equal("SELECT ol.[LineNo] FROM OrderLine ol", Qualify("SELECT [LineNo] FROM OrderLine"));
    }

    [Fact]
    public void CommentsAndLayout_AreKept()
    {
        const string sql = "SELECT FName -- first\n     , City /* where */\nFROM Person -- people\nJOIN Address ON Address.AddressId = Person.AddressId";
        Assert.Equal("SELECT p.FName -- first\n     , a.City /* where */\nFROM Person p -- people\nJOIN Address a ON a.AddressId = p.AddressId", Qualify(sql));
    }

    [Fact]
    public void Function_TableValuedWithoutAlias_IsAliased()
    {
        Assert.Equal("SELECT * FROM dbo.GetThings(1) gt", Qualify("SELECT * FROM dbo.GetThings(1)"));
    }

    [Fact]
    public void ParseError_ChangesNothing()
    {
        var result = ColumnQualifier.Qualify("SELECT FName FROM Person WHERE", Lookup);
        Assert.False(result.Success);
        Assert.Contains("does not parse", result.ErrorMessage);
    }
}
