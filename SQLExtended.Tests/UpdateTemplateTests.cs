using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SQLExtended.Cache.Models;
using SQLExtended.Formatting;
using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

/// <summary>
/// The "(all columns) SET col = val" template after "UPDATE dbo.T ". What fails silently here is the WHERE: a
/// template that loses it, or keys it on the wrong column, still parses and updates far more than one row.
/// </summary>
public class UpdateTemplateTests
{
    private static readonly List<CachedColumn> Company = new()
    {
        Col("ID", "int", identity: true),
        Col("NAME", "varchar"),
        Col("ACTIVE", "bit"),
        Col("CREATED", "datetime"),
        Col("FULL_NAME", "varchar", computed: true),
        Col("VER", "timestamp"),
    };

    [Fact]
    public void SetsEveryAssignableColumn_AndKeysTheWhereOnThePrimaryKey()
    {
        string body = Build(Company, new[] { "ID" });

        Assert.Equal(
            "SET\r\n" +
            "    NAME = '',\r\n" +
            "    ACTIVE = 0,\r\n" +
            "    CREATED = GETDATE()\r\n" +
            "WHERE ID = 0",
            body);
    }

    [Fact]
    public void NoPrimaryKey_WhereMatchesNothing()
    {
        Assert.EndsWith("WHERE 1 = 0", Build(Company, null));
    }

    [Fact]
    public void CompositeKey_LeftOutOfSet_AndJoinedWithAndInKeyOrder()
    {
        var cols = new List<CachedColumn> { Col("ITEM", "int"), Col("ORDER_ID", "int"), Col("QTY", "int") };
        string body = Build(cols, new[] { "ORDER_ID", "ITEM" });

        Assert.DoesNotContain("ITEM = 0,", body);
        Assert.Contains("    QTY = 0\r\n", body);
        Assert.EndsWith("WHERE ORDER_ID = 0 AND ITEM = 0", body);
    }

    [Fact]
    public void OnlyKeyColumns_NoTemplate()
    {
        Assert.Null(Build(new List<CachedColumn> { Col("ID", "int") }, new[] { "ID" }));
    }

    [Fact]
    public void LeadingComma_AndLowerCaseKeywords()
    {
        string body = UpdateTemplateBuilder.Build(Company, new[] { "ID" }, v => "0", "\t", leadingComma: true, CasingOption.Lower);

        Assert.StartsWith("set\r\n\tNAME = 0\r\n\t, ACTIVE = 0\r\n", body);
        Assert.EndsWith("where ID = 0", body);
    }

    [Fact]
    public void ReservedAndSpacedNamesAreBracketed()
    {
        var cols = new List<CachedColumn> { Col("Id", "int"), Col("Order", "int"), Col("Ongoing Qty", "int") };
        string body = Build(cols, new[] { "Id" });

        Assert.Contains("[Order] = 0", body);
        Assert.Contains("[Ongoing Qty] = 0", body);
    }

    [Fact]
    public void CommittedTemplateParses()
    {
        string sql = "UPDATE dbo.Company " + Build(Company, new[] { "ID" });
        using var reader = new StringReader(sql);
        new TSql170Parser(true).Parse(reader, out var errors);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("UPDATE Company ", null, null, "Company")]
    [InlineData("UPDATE dbo.Company ", null, "dbo", "Company")]
    [InlineData("UPDATE [Db].dbo.[The Company] ", "Db", "dbo", "The Company")]
    public void UpdateTablePosition_IsTheTemplateContext(string sql, string db, string schema, string table)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);

        Assert.Equal(SqlContextAnalyzer.CompletionType.UpdateSetTemplate, result.Type);
        Assert.Equal(db, result.TargetDatabase);
        Assert.Equal(schema, result.TargetSchema);
        Assert.Equal(table, result.TargetTable);
    }

    [Theory]
    [InlineData("UPDATE ")]
    [InlineData("UPDATE dbo.T SET ")]
    [InlineData("UPDATE STATISTICS ")]
    [InlineData("UPDATE dbo.T SET a = 1 ")]
    [InlineData("ALTER TABLE dbo.T ADD CONSTRAINT FK FOREIGN KEY (a) REFERENCES dbo.U (a) ON UPDATE CASCADE ")]
    [InlineData("ALTER TABLE dbo.T ADD CONSTRAINT FK FOREIGN KEY (a) REFERENCES dbo.U (a) ON UPDATE NO ")]
    [InlineData("GRANT UPDATE ON ")]
    [InlineData("UPDATE dbo.T")]
    public void OtherPositions_AreNotTheTemplateContext(string sql)
    {
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.UpdateSetTemplate, SqlContextAnalyzer.Analyze(sql, sql.Length).Type);
    }

    private static string Build(IReadOnlyList<CachedColumn> cols, IReadOnlyList<string> keys) =>
        UpdateTemplateBuilder.Build(cols, keys, Placeholder, "    ", leadingComma: false, CasingOption.Upper);

    // A stand-in for SqlCompletionSource.DefaultValueFor, which lives with the editor assemblies.
    private static string Placeholder(CachedColumn c) => c.DataType switch
    {
        "varchar" => "''",
        "datetime" => "GETDATE()",
        _ => "0"
    };

    private static CachedColumn Col(string name, string type, bool identity = false, bool computed = false) =>
        new() { ColumnName = name, DataType = type, IsIdentity = identity, IsComputed = computed };
}
