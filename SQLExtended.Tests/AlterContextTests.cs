using SQLExtended.IntelliSense;
using System.Linq;
using Xunit;

namespace SQLExtended.Tests;

public class AlterContextTests
{
    private static SqlContextAnalyzer.AnalysisResult AnalyzeAtEnd(string sql)
        => SqlContextAnalyzer.Analyze(sql, sql.Length);

    // --- ALTER <object kind> ---

    [Theory]
    [InlineData("ALTER ")]
    [InlineData("ALTER PROC")]
    [InlineData("SELECT 1;\nALTER ")]
    public void Analyze_AfterAlter_ReturnsAlterTarget(string sql)
    {
        Assert.Equal(SqlContextAnalyzer.CompletionType.AlterTarget, AnalyzeAtEnd(sql).Type);
    }

    [Fact]
    public void AlterTargets_IncludeCommonObjectKinds()
    {
        var kinds = SqlAlterCommands.Targets.Select(t => t.Keyword).ToList();
        Assert.Contains("TABLE", kinds);
        Assert.Contains("VIEW", kinds);
        Assert.Contains("PROCEDURE", kinds);
        Assert.Contains("FUNCTION", kinds);
        Assert.Contains("INDEX", kinds);
        Assert.Contains("DATABASE", kinds);
    }

    // --- ALTER TABLE <name>: table name vs. action ---

    [Theory]
    [InlineData("ALTER TABLE ")]          // expecting the table name
    [InlineData("ALTER TABLE Cust")]      // typing the table name
    [InlineData("ALTER TABLE dbo.")]      // schema-qualified, expecting name
    public void Analyze_AlterTableBeforeName_ReturnsTableName(string sql)
    {
        // Bare "ALTER TABLE " (and partial name) is object-name completion, not an action.
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.AlterTableAction, AnalyzeAtEnd(sql).Type);
    }

    [Theory]
    [InlineData("ALTER TABLE Customers ")]
    [InlineData("ALTER TABLE dbo.Customers ")]
    [InlineData("ALTER TABLE [dbo].[Customers] ")]
    [InlineData("ALTER TABLE Customers AL")]   // typing an action
    public void Analyze_AfterAlterTableName_ReturnsTableAction(string sql)
    {
        Assert.Equal(SqlContextAnalyzer.CompletionType.AlterTableAction, AnalyzeAtEnd(sql).Type);
    }

    [Fact]
    public void Analyze_AfterActionKeyword_IsNotTableAction()
    {
        // Once an action is chosen, we should not keep offering actions.
        string sql = "ALTER TABLE Customers ADD ";
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.AlterTableAction, AnalyzeAtEnd(sql).Type);
    }

    [Fact]
    public void AlterTableActions_IncludeCoreActions()
    {
        var actions = SqlAlterCommands.TableActions.Select(a => a.Keyword).ToList();
        Assert.Contains("ADD", actions);
        Assert.Contains("ALTER COLUMN", actions);
        Assert.Contains("DROP COLUMN", actions);
        Assert.Contains("DROP CONSTRAINT", actions);
        Assert.Contains("ADD CONSTRAINT", actions);
    }

    [Fact]
    public void AllAlterClauses_HaveKeywordAndDescription()
    {
        foreach (var c in SqlAlterCommands.Targets
            .Concat(SqlAlterCommands.TableActions)
            .Concat(SqlAlterCommands.IndexActions)
            .Concat(SqlAlterCommands.IndexNameHints))
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Keyword));
            Assert.False(string.IsNullOrWhiteSpace(c.Description));
        }
    }

    // --- ALTER INDEX ---

    [Theory]
    [InlineData("ALTER INDEX ")]
    [InlineData("ALTER INDEX A")]   // typing ALL / an index name
    public void Analyze_AfterAlterIndex_ReturnsIndexName(string sql)
    {
        Assert.Equal(SqlContextAnalyzer.CompletionType.AlterIndexName, AnalyzeAtEnd(sql).Type);
    }

    [Theory]
    [InlineData("ALTER INDEX IX_Foo ON ")]
    [InlineData("ALTER INDEX ALL ON ")]
    [InlineData("ALTER INDEX [IX_Foo] ON ")]
    public void Analyze_AlterIndexOn_ReturnsTableName(string sql)
    {
        Assert.Equal(SqlContextAnalyzer.CompletionType.TableName, AnalyzeAtEnd(sql).Type);
    }

    [Theory]
    [InlineData("ALTER INDEX IX_Foo ON Customers ")]
    [InlineData("ALTER INDEX ALL ON dbo.Customers ")]
    [InlineData("ALTER INDEX [IX_Foo] ON [dbo].[Customers] ")]
    [InlineData("ALTER INDEX IX_Foo ON Customers RE")]   // typing an action
    public void Analyze_AfterAlterIndexOnTable_ReturnsIndexAction(string sql)
    {
        Assert.Equal(SqlContextAnalyzer.CompletionType.AlterIndexAction, AnalyzeAtEnd(sql).Type);
    }

    [Fact]
    public void Analyze_AfterIndexActionKeyword_IsNotIndexAction()
    {
        string sql = "ALTER INDEX ALL ON Customers REBUILD ";
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.AlterIndexAction, AnalyzeAtEnd(sql).Type);
    }

    [Fact]
    public void IndexActions_IncludeCoreActions()
    {
        var actions = SqlAlterCommands.IndexActions.Select(a => a.Keyword).ToList();
        Assert.Contains("REBUILD", actions);
        Assert.Contains("REORGANIZE", actions);
        Assert.Contains("DISABLE", actions);
        Assert.Contains("SET", actions);
    }
}
