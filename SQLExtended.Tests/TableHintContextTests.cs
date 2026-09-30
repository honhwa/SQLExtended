using Microsoft.SqlServer.TransactSql.ScriptDom;
using SQLExtended.IntelliSense;
using System.IO;
using System.Linq;
using Xunit;

namespace SQLExtended.Tests;

public class TableHintContextTests
{
    private static SqlContextAnalyzer.AnalysisResult AnalyzeAtEnd(string sql)
        => SqlContextAnalyzer.Analyze(sql, sql.Length);

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders WITH (")]
    [InlineData("SELECT * FROM dbo.Orders WITH(NOL")]
    [InlineData("SELECT * FROM dbo.Orders o WITH (")]
    [InlineData("SELECT * FROM dbo.Orders AS o WITH (NOLOCK, ")]
    [InlineData("SELECT * FROM [Sales].[dbo].[Order Lines] ol WITH (")]
    [InlineData("SELECT * FROM dbo.Orders o\nJOIN dbo.Customer c WITH (")]
    [InlineData("SELECT * FROM dbo.Orders o WITH (NOLOCK), dbo.Customer c WITH (")]
    [InlineData("SELECT * FROM #work w WITH (")]
    [InlineData("UPDATE dbo.Orders WITH (")]
    [InlineData("DELETE FROM dbo.Orders WITH (")]
    [InlineData("INSERT INTO dbo.Orders WITH (")]
    [InlineData("MERGE dbo.Orders AS tgt WITH (")]
    [InlineData("SELECT * FROM dbo.Orders WITH (INDEX (IX_Orders), ")]
    [InlineData("SELECT * FROM dbo.Orders OPTION (TABLE HINT (dbo.Orders, ")]
    public void Analyze_AtTableHintSlot_ReturnsTableHint(string sql)
    {
        Assert.Equal(SqlContextAnalyzer.CompletionType.TableHint, AnalyzeAtEnd(sql).Type);
    }

    [Theory]
    [InlineData("WITH cte (")]                                                   // CTE column list
    [InlineData(";WITH cte AS (")]                                               // CTE body
    [InlineData("CREATE INDEX IX ON dbo.Orders (CustomerId) WITH (")]            // index options
    [InlineData("ALTER INDEX ALL ON dbo.Orders REBUILD WITH (")]
    [InlineData("SELECT * FROM dbo.Orders WITH (NOLOCK) ")]                      // list closed
    [InlineData("SELECT * FROM dbo.Orders WITH (INDEX (")]                       // an index name, not a hint
    [InlineData("SELECT * FROM dbo.Orders OPTION (TABLE HINT (")]                // the object comes first
    public void Analyze_OutsideTableHintSlot_IsNotTableHint(string sql)
    {
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.TableHint, AnalyzeAtEnd(sql).Type);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders WITH (INDEX (", null, "dbo", "Orders")]
    [InlineData("SELECT * FROM dbo.Orders WITH (INDEX(IX_", null, "dbo", "Orders")]
    [InlineData("SELECT * FROM dbo.Orders o WITH (NOLOCK, INDEX (", null, "dbo", "Orders")]
    [InlineData("SELECT * FROM dbo.Orders o WITH (INDEX (IX_A, ", null, "dbo", "Orders")]
    [InlineData("SELECT * FROM Sales.dbo.[Order Lines] ol WITH (FORCESEEK (", "Sales", "dbo", "Order Lines")]
    [InlineData("SELECT * FROM dbo.Orders o JOIN Customer c WITH (INDEX (", null, null, "Customer")]
    [InlineData("UPDATE o WITH (INDEX (", null, null, "o")]   // an alias - the source resolves it via the FROM clause
    public void Analyze_InIndexHint_ReturnsIndexHintNameForTheHintedTable(string sql, string db, string schema, string table)
    {
        var r = AnalyzeAtEnd(sql);
        Assert.Equal(SqlContextAnalyzer.CompletionType.IndexHintName, r.Type);
        Assert.Equal(db, r.TargetDatabase);
        Assert.Equal(schema, r.TargetSchema);
        Assert.Equal(table, r.TargetTable);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Orders WITH (INDEX (IX_A ")]            // after a name
    [InlineData("SELECT * FROM dbo.Orders WITH (FORCESEEK (IX_A, ")]       // FORCESEEK takes a single index
    [InlineData("SELECT * FROM dbo.Orders WITH (FORCESEEK (IX_A (")]       // its column list
    [InlineData("CREATE INDEX (")]                                          // not a table hint
    [InlineData("SELECT * FROM dbo.Orders WHERE x IN (SELECT INDEX (")]
    public void Analyze_OutsideIndexHintSlot_IsNotIndexHintName(string sql)
    {
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.IndexHintName, AnalyzeAtEnd(sql).Type);
    }

    // A wrong entry inserts SQL that does not parse, and nothing reports that until the query runs.
    [Fact]
    public void EveryTableHint_WithItsArgument_Parses()
    {
        foreach (var hint in SqlQueryHints.TableHints.Select(h => h.Keyword))
        {
            string sql = hint switch
            {
                "INDEX" => "SELECT * FROM dbo.T WITH (INDEX (IX_T));",
                "SPATIAL_WINDOW_MAX_CELLS" => "SELECT * FROM dbo.T WITH (SPATIAL_WINDOW_MAX_CELLS = 512);",
                "KEEPIDENTITY" or "KEEPDEFAULTS" or "IGNORE_CONSTRAINTS" or "IGNORE_TRIGGERS"
                    => $"INSERT INTO dbo.T WITH ({hint}) SELECT * FROM OPENROWSET(BULK 'c:\\f.dat', FORMATFILE = 'c:\\f.fmt') AS b;",
                _ => $"SELECT * FROM dbo.T WITH ({hint});",
            };
            AssertParses(sql);
        }
    }

    private static void AssertParses(string sql)
    {
        new TSql170Parser(true).Parse(new StringReader(sql), out var errors);
        Assert.True(errors.Count == 0, $"{sql}\n{string.Join("\n", errors.Select(e => e.Message))}");
    }
}
