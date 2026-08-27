using System.Collections.Generic;
using System.Linq;
using SQLExtended.ResultsGrid;
using Xunit;

namespace SQLExtended.Tests;

public class InsertScriptGeneratorTests
{
    private static ResultGridData Grid(string[] names, string[] types, params string[][] rows)
    {
        var data = new ResultGridData { ColumnNames = names, SqlTypes = types };
        data.Rows.AddRange(rows);
        return data;
    }

    private static string Generate(params ResultGridData[] sets) => InsertScriptGenerator.Generate(sets);

    [Fact]
    public void BasicScript_UsesProvidedTypes_AndQuotesByCategory()
    {
        string script = Generate(Grid(
            new[] { "Id", "Name" },
            new[] { "int", "nvarchar(50)" },
            new[] { "1", "Alice" },
            new[] { "2", "Bob" }));

        Assert.Contains("CREATE TABLE #Results (", script);
        Assert.Contains("[Id] int NULL,", script);
        Assert.Contains("[Name] nvarchar(50) NULL", script);
        Assert.Contains("INSERT INTO #Results ([Id], [Name])", script);
        Assert.Contains("(1, N'Alice')", script);
        Assert.Contains("(2, N'Bob');", script);
        Assert.Contains("IF OBJECT_ID('tempdb..#Results') IS NOT NULL DROP TABLE #Results;", script);
    }

    [Fact]
    public void NullCells_EmitNullLiteral()
    {
        string script = Generate(Grid(
            new[] { "A" }, new[] { "int" },
            new string[] { null },
            new[] { "5" }));

        Assert.Contains("(NULL)", script);
        Assert.Contains("(5)", script);
    }

    [Fact]
    public void SingleQuotes_AreEscaped()
    {
        string script = Generate(Grid(
            new[] { "A" }, new[] { "varchar(20)" },
            new[] { "O'Brien" }));

        Assert.Contains("N'O''Brien'", script);
    }

    [Fact]
    public void NonNumericValueInNumericColumn_IsQuoted()
    {
        string script = Generate(Grid(
            new[] { "A" }, new[] { "money" },
            new[] { "1,234.50" }));

        Assert.Contains("N'1,234.50'", script);
    }

    [Fact]
    public void BinaryValues_EmitRawHexLiterals()
    {
        string script = Generate(Grid(
            new[] { "A" }, new[] { "varbinary(max)" },
            new[] { "0xDEADBEEF" }));

        Assert.Contains("(0xDEADBEEF)", script);
    }

    [Fact]
    public void RowVersionColumn_BecomesBinary8()
    {
        string script = Generate(Grid(
            new[] { "RV" }, new[] { "timestamp" },
            new[] { "0x00000000000007D1" }));

        Assert.Contains("[RV] binary(8) NULL", script);
        Assert.Contains("(0x00000000000007D1)", script);
    }

    [Fact]
    public void UnnamedAndDuplicateColumns_AreSanitized()
    {
        string script = Generate(Grid(
            new[] { "(No column name)", "X", "X" },
            new[] { "int", "int", "int" },
            new[] { "1", "2", "3" }));

        Assert.Contains("[Column1] int NULL", script);
        Assert.Contains("[X] int NULL", script);
        Assert.Contains("[X_2] int NULL", script);
    }

    [Fact]
    public void BracketInColumnName_IsEscaped()
    {
        string script = Generate(Grid(
            new[] { "Weird]Name" }, new[] { "int" },
            new[] { "1" }));

        Assert.Contains("[Weird]]Name]", script);
    }

    [Fact]
    public void MultipleResultSets_GetNumberedTables()
    {
        string script = Generate(
            Grid(new[] { "A" }, new[] { "int" }, new[] { "1" }),
            Grid(new[] { "B" }, new[] { "int" }, new[] { "2" }));

        Assert.Contains("CREATE TABLE #Results1 (", script);
        Assert.Contains("CREATE TABLE #Results2 (", script);
        Assert.Contains("INSERT INTO #Results1 ([A])", script);
        Assert.Contains("INSERT INTO #Results2 ([B])", script);
    }

    [Fact]
    public void RowsBeyondBatchLimit_SplitIntoMultipleInserts()
    {
        var rows = Enumerable.Range(1, 1500).Select(i => new[] { i.ToString() }).ToArray();
        string script = Generate(Grid(new[] { "N" }, new[] { "int" }, rows));

        int insertCount = script.Split(new[] { "INSERT INTO #Results" }, System.StringSplitOptions.None).Length - 1;
        Assert.Equal(2, insertCount);
        Assert.Contains("(1000);", script); // row 1000 terminates the first 1000-row batch
        Assert.Contains("(1500);", script);
    }

    [Fact]
    public void EmptyResultSet_EmitsCreateTableOnly()
    {
        string script = Generate(Grid(new[] { "A" }, new[] { "int" }));

        Assert.Contains("CREATE TABLE #Results (", script);
        Assert.DoesNotContain("INSERT INTO", script);
    }

    // --- Type inference (no SqlTypes available) ---

    [Theory]
    [InlineData("int", "1", "42")]
    [InlineData("bigint", "1", "9999999999")]
    [InlineData("float", "1.5E+10", "2")]
    [InlineData("uniqueidentifier", "6F9619FF-8B86-D011-B42D-00C04FC964FF", "0e984725-c51c-4bf4-9960-e1c80e27aba0")]
    [InlineData("date", "2024-01-15", "2023-12-31")]
    [InlineData("datetime2", "2024-01-15 10:30:00.000", "2024-01-15")]
    [InlineData("varbinary(max)", "0xAB", "0xCD12")]
    public void InferredTypes_MatchExpected(string expectedType, params string[] values)
    {
        var rows = values.Select(v => new[] { v }).ToArray();
        string script = Generate(Grid(new[] { "A" }, null, rows));

        Assert.Contains($"[A] {expectedType} NULL", script);
    }

    [Fact]
    public void InferredDecimal_ComputesPrecisionAndScale()
    {
        string script = Generate(Grid(new[] { "A" }, null,
            new[] { "123.45" },
            new[] { "1.5" }));

        Assert.Contains("[A] decimal(5,2) NULL", script);
    }

    [Fact]
    public void InferredString_UsesMaxLength()
    {
        string script = Generate(Grid(new[] { "A" }, null,
            new[] { "abc" },
            new[] { "abcdef" }));

        Assert.Contains("[A] nvarchar(6) NULL", script);
    }

    [Fact]
    public void AllNullColumn_FallsBackToNvarcharMax()
    {
        string script = Generate(Grid(new[] { "A" }, null, new string[] { null }));

        Assert.Contains("[A] nvarchar(max) NULL", script);
    }
}
