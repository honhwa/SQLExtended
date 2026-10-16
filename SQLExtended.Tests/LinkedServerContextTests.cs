using SQLExtended.Cache;
using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

/// <summary>
/// Pins the linked-server half of IntelliSense that runs without a connection: which positions take a linked server
/// name, that four-part names are still read as table positions and keep their server part, and how the rows the
/// remote catalog procedures return are filtered and shown. Each of these fails silently — a wrong context offers
/// the wrong list, and a lost server part looks a local table up under the remote table's name and finds nothing.
/// </summary>
public class LinkedServerContextTests
{
    [Theory]
    [InlineData("SELECT * FROM OPENQUERY(")]
    [InlineData("SELECT * FROM OPENQUERY( ")]
    [InlineData("SELECT * FROM openquery(LNK")]
    [InlineData("SELECT * FROM OPENQUERY([LNK\\INS")]
    [InlineData("EXEC ('SELECT 1') AT ")]
    [InlineData("EXECUTE (N'SELECT f(1), '')''') AT LN")]
    [InlineData("EXEC (@sql) AT [Remote")]
    public void LinkedServerPositions_ReturnLinkedServerName(string sql)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.LinkedServerName, result.Type);
    }

    [Theory]
    [InlineData("SELECT GETDATE() AT ")]               // AT TIME ZONE, not a linked server
    [InlineData("SELECT x FROM dbo.T WHERE a = OPENQUERY")]
    public void OtherPositions_DoNotReturnLinkedServerName(string sql)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.LinkedServerName, result.Type);
    }

    [Theory]
    [InlineData("SELECT * FROM LNK.")]
    [InlineData("SELECT * FROM LNK.Sales.")]
    [InlineData("SELECT * FROM LNK.Sales.dbo.")]
    [InlineData("SELECT * FROM [LNK\\INS].[Sales].[dbo].Ord")]
    [InlineData("SELECT * FROM LNK.Sales..")]
    public void FourPartNames_AreTablePositions(string sql)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.TableName, result.Type);
    }

    [Theory]
    [InlineData("EXEC LNK.")]
    [InlineData("EXEC LNK.Sales.dbo.")]
    public void FourPartExec_IsAProcedurePosition(string sql)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.ProcedureName, result.Type);
    }

    [Theory]
    [InlineData("SELECT * FROM LNK.Sales.dbo.", new[] { "LNK", "Sales", "dbo" })]
    [InlineData("SELECT * FROM [LNK\\INS].[Sales].[dbo].Ord", new[] { "LNK\\INS", "Sales", "dbo" })]
    [InlineData("SELECT * FROM LNK.Sales..", new[] { "LNK", "Sales", "" })]
    public void GetQualifierParts_KeepsTheServer(string textBefore, string[] expected)
    {
        Assert.Equal(expected, SqlCompletionContext.GetQualifierParts(textBefore).ToArray());
    }

    [Theory]
    [InlineData("SELECT o. FROM LNK.Sales.dbo.Orders o")]                 // parsed by ScriptDom
    [InlineData("SELECT * FROM LNK.Sales.dbo.Orders o WHERE o.")]        // incomplete: the regex fallback
    [InlineData("SELECT * FROM [LNK].[Sales].[dbo].[Orders] AS o WHERE")]
    public void FourPartReference_KeepsEveryPart(string sql)
    {
        var t = Assert.Single(AliasResolver.Resolve(sql));
        Assert.Equal("LNK", t.Server);
        Assert.Equal("Sales", t.Database);
        Assert.Equal("dbo", t.Schema);
        Assert.Equal("Orders", t.Table);
        Assert.Equal("o", t.Alias);
    }

    [Theory]
    [InlineData("SELECT * FROM Sales.dbo.Orders o WHERE o.", null, "Sales", "dbo")]
    [InlineData("SELECT * FROM dbo.Orders o WHERE o.", null, null, "dbo")]
    [InlineData("SELECT * FROM LNK.Sales..Orders o WHERE o.", "LNK", "Sales", null)]
    public void ShorterReferences_AreUnchanged(string sql, string server, string database, string schema)
    {
        var t = Assert.Single(AliasResolver.Resolve(sql));
        Assert.Equal(server, t.Server);
        Assert.Equal(database, t.Database);
        Assert.Equal(schema, t.Schema);
        Assert.Equal("Orders", t.Table);
    }

    [Theory]
    [InlineData("nvarchar", 50, null, "nvarchar(50)")]
    [InlineData("nvarchar", 1073741823, null, "nvarchar(max)")]
    [InlineData("varbinary", 0, null, "varbinary(max)")]
    [InlineData("decimal", 18, 2, "decimal(18,2)")]
    [InlineData("int", 10, 0, "int")]
    [InlineData("VARCHAR2", 4000, null, "VARCHAR2(4000)")]
    [InlineData("datetime2", 27, 7, "datetime2")]
    [InlineData("", 1, null, "")]
    public void FormatType_ReadsSizeInCharacters(string type, int? size, int? digits, string expected)
    {
        Assert.Equal(expected, LinkedServerSql.FormatType(type, size, digits));
    }

    [Theory]
    [InlineData("dbo", "TABLE", true)]
    [InlineData("dbo", "VIEW", true)]
    [InlineData("SCOTT", "SYNONYM", true)]
    [InlineData("sys", "VIEW", false)]                  // a SQL Server target's catalog views
    [InlineData("INFORMATION_SCHEMA", "VIEW", false)]
    [InlineData("SYS", "SYSTEM TABLE", false)]
    [InlineData("dbo", "SYSTEM VIEW", false)]
    public void IsUserObject_LeavesOutTheCatalog(string schema, string type, bool expected)
    {
        Assert.Equal(expected, LinkedServerSql.IsUserObject(schema, type));
    }
}
