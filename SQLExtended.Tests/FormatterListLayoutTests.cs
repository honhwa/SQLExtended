using System.Collections.Generic;
using System.IO;
using SQLExtended.Formatting;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;
using Xunit.Abstractions;

namespace SQLExtended.Tests;

/// <summary>
/// The switches that decide how a list of fields is laid out and how a call is spelled:
/// <see cref="FormatterOptions.BuiltInFunctionCase"/>, <see cref="FormatterOptions.InsertOpenParenthesisOnSameLine"/>,
/// leading commas inside the reflowed INSERT/VALUES lists, and
/// <see cref="FormatterOptions.AlignSetWithUpdate"/>.
///
/// All four fail quietly: the output is still valid, still formatted, and only differs from what was
/// asked for — so the assertions here are on the exact text, and the ones that could produce invalid
/// SQL re-parse the result (the same thing the alias tests do, and for the same reason).
/// </summary>
public class FormatterListLayoutTests
{
    private readonly ITestOutputHelper _output;
    public FormatterListLayoutTests(ITestOutputHelper output) => _output = output;

    /// <summary>Leading-comma / stacked profile, with the four switches on.</summary>
    private static FormatterOptions Profile() => new FormatterOptions
    {
        KeywordCase = CasingOption.Upper,
        IndentStyle = IndentStyleOption.Spaces,
        IndentSize = 4,
        CommaPosition = CommaPositionOption.LeadingComma,
        LeadingCommaKeepIndent = true,
        SelectColumnLayout = SelectColumnLayoutOption.StackedFirstOnNewLine,
        AliasStyle = AliasStyleOption.AS,
        AlignColumnDefinitionFields = false,
        BlankLinesBetweenStatements = 1,
        MaxLineWidth = 120,
        NewLineBeforeCloseParenthesis = true,
        BuiltInFunctionCase = CasingOption.Upper,
        InsertOpenParenthesisOnSameLine = true,
        InsertColumnsPerLine = 1,
        InsertValuesPerLine = 1,
        AlignSetWithUpdate = true,
    };

    private string Format(string sql, FormatterOptions opts = null)
    {
        var result = new SqlFormatterService(opts ?? Profile()).Format(sql);
        _output.WriteLine(result.FormattedSql);
        Assert.True(result.Success, result.ErrorMessage);
        return result.FormattedSql.Replace("\r\n", "\n");
    }

    /// <summary>Formats and re-parses, so a pass that produces text which no longer compiles is caught.</summary>
    private string FormatAndReparse(string sql, FormatterOptions opts = null)
    {
        string formatted = Format(sql, opts);
        IList<ParseError> errors;
        using (var reader = new StringReader(formatted))
            new TSql170Parser(true).Parse(reader, out errors);
        Assert.True(errors.Count == 0, errors.Count == 0 ? "" : $"re-parse failed: {errors[0].Message}");
        return formatted;
    }

    // --- built-in function casing -------------------------------------------------------------

    [Fact]
    public void BuiltInFunctions_AreUppercased()
    {
        var sql = "select row_number() over (partition by a order by b) as rn, sum(x) as s, avg(y) as a2, " +
                  "lag(z, 1) over (order by b) as lg, dense_rank() over (order by c) as dr, getdate() as d from T";

        var result = FormatAndReparse(sql);

        Assert.Contains("ROW_NUMBER() OVER", result);
        Assert.Contains("SUM(x)", result);
        Assert.Contains("AVG(y)", result);
        Assert.Contains("LAG(z, 1) OVER", result);
        Assert.Contains("DENSE_RANK() OVER", result);
        Assert.Contains("GETDATE()", result);
    }

    /// <summary>
    /// The two guards that make the pass safe. Most built-in names are also good column names, and
    /// only the call position separates them: "o.Count" is qualified and not called, and
    /// "dbo.Count(1)" is a user function that happens to share a built-in's name.
    /// </summary>
    [Fact]
    public void QualifiedNamesAndBareColumns_AreNotTouched()
    {
        var sql = "select o.Count, o.Max, dbo.Count(1), dbo.getdate(1) from T o";

        var result = FormatAndReparse(sql);

        Assert.Contains("o.Count", result);
        Assert.Contains("o.Max", result);
        Assert.Contains("dbo.Count(1)", result);
        Assert.Contains("dbo.getdate(1)", result);
    }

    /// <summary>A bare Regex.Replace over the script would case the word inside a literal or a comment.</summary>
    [Fact]
    public void FunctionNamesInsideStringsAndComments_AreNotTouched()
    {
        var sql = "-- call getdate() here\nselect x from T where Note = 'sum(1) and getdate()'";

        var result = FormatAndReparse(sql);

        Assert.Contains("-- call getdate() here", result);
        Assert.Contains("'sum(1) and getdate()'", result);
    }

    [Fact]
    public void BuiltInFunctionCase_Lower_LowercasesThem()
    {
        var opts = Profile();
        opts.BuiltInFunctionCase = CasingOption.Lower;

        var result = FormatAndReparse("select SUM(x), GETDATE(), Row_Number() over (order by a) from T", opts);

        Assert.Contains("sum(x)", result);
        Assert.Contains("getdate()", result);
        Assert.Contains("row_number() OVER", result);
    }

    [Fact]
    public void BuiltInFunctionCase_Unchanged_LeavesThemAsTyped()
    {
        var opts = Profile();
        opts.BuiltInFunctionCase = CasingOption.Unchanged;

        var result = FormatAndReparse("select SUM(x), getdate(), Row_Number() over (order by a) from T", opts);

        Assert.Contains("SUM(x)", result);
        Assert.Contains("getdate()", result);
        Assert.Contains("Row_Number() OVER", result);
    }

    // --- INSERT column list ------------------------------------------------------------------

    [Fact]
    public void InsertColumns_OpenParenOnTableLine_WithLeadingCommas()
    {
        var sql = "insert into #ttOrderSummary (OrderId, CustomerId, OrderDate, TotalAmount, Status) " +
                  "select o.OrderId, o.CustomerId, o.OrderDate, sum(d.Amount), o.Status from Orders o " +
                  "group by o.OrderId, o.CustomerId, o.OrderDate, o.Status";

        var expected =
            "INSERT INTO #ttOrderSummary (\n" +
            "    OrderId\n" +
            "    , CustomerId\n" +
            "    , OrderDate\n" +
            "    , TotalAmount\n" +
            "    , Status\n" +
            ")\n";

        Assert.StartsWith(expected, FormatAndReparse(sql));
    }

    [Fact]
    public void InsertValues_OpenParenOnValuesLine_WithLeadingCommas()
    {
        var result = FormatAndReparse("insert into dbo.Foo (Id, Name, Created) values (1, 'a', getdate())");

        Assert.Contains(
            "VALUES (\n" +
            "    1\n" +
            "    , 'a'\n" +
            "    , GETDATE()\n" +
            ")", result);
    }

    /// <summary>
    /// The bracket option and the comma option are independent: with trailing commas and four columns
    /// per line the list still wraps at the same items, only the separators move.
    /// </summary>
    [Fact]
    public void InsertColumns_TrailingCommas_StillPackPerLine()
    {
        var opts = Profile();
        opts.CommaPosition = CommaPositionOption.TrailingComma;
        opts.InsertColumnsPerLine = 4;

        var result = FormatAndReparse(
            "insert into #tt (OrderId, CustomerId, OrderDate, TotalAmount, Status) select 1, 2, 3, 4, 5", opts);

        Assert.StartsWith(
            "INSERT INTO #tt (\n" +
            "    OrderId, CustomerId, OrderDate, TotalAmount,\n" +
            "    Status\n" +
            ")\n", result);
    }

    /// <summary>
    /// The pre-existing option, which is a different layout: it also pulls the first column up onto
    /// the table line and the closing bracket onto the last one. The new switch wins when both are on.
    /// </summary>
    [Fact]
    public void InsertParenthesesOnSameLine_IsStillAvailable_AndOutrankedByTheNewSwitch()
    {
        var opts = Profile();
        opts.CommaPosition = CommaPositionOption.TrailingComma;
        opts.InsertOpenParenthesisOnSameLine = false;
        opts.InsertParenthesesOnSameLine = true;
        opts.InsertColumnsPerLine = 2;

        var pulled = FormatAndReparse("insert into #tt (A, B, C, D) select 1, 2, 3, 4", opts);
        Assert.StartsWith("INSERT INTO #tt (A, B,\n    C, D)\n", pulled);

        opts.InsertOpenParenthesisOnSameLine = true;
        var bracketOnly = FormatAndReparse("insert into #tt (A, B, C, D) select 1, 2, 3, 4", opts);
        Assert.StartsWith("INSERT INTO #tt (\n    A, B,\n    C, D\n)\n", bracketOnly);
    }

    [Fact]
    public void CreateTableColumns_KeepLeadingCommas()
    {
        var result = FormatAndReparse(
            "create table dbo.Foo (Id int not null, Name nvarchar(50) null, Created datetime null)");

        Assert.Contains(
            "CREATE TABLE dbo.Foo (\n" +
            "    Id INT NOT NULL\n" +
            "    , Name NVARCHAR(50) NULL\n" +
            "    , Created DATETIME NULL\n" +
            ")", result);
    }

    // --- UPDATE / SET ------------------------------------------------------------------------

    [Fact]
    public void SetClause_IsLeftAlignedWithUpdate()
    {
        var sql = "update s set s.LastOrderDate = o.OrderDate, s.TotalSpend = o.TotalAmount, " +
                  "s.Status = 'Reviewed', s.ModifiedDate = getdate() " +
                  "from #ttOrderSummary s join Orders o on o.OrderId = s.OrderId";

        var expected =
            "UPDATE s\n" +
            "SET s.LastOrderDate = o.OrderDate\n" +
            "    , s.TotalSpend = o.TotalAmount\n" +
            "    , s.Status = 'Reviewed'\n" +
            "    , s.ModifiedDate = GETDATE()\n";

        Assert.StartsWith(expected, FormatAndReparse(sql));
    }

    /// <summary>
    /// The shift is relative to the UPDATE, not to the left margin — an UPDATE nested in a procedure
    /// body keeps its own indentation and only loses the extra level SET was carrying.
    /// </summary>
    [Fact]
    public void SetClause_NestedInProcedure_KeepsTheEnclosingIndent()
    {
        var sql = "create procedure dbo.P as begin update s set s.A = 1, s.B = 2 from T s where s.Id > 0; end";

        var result = FormatAndReparse(sql);

        Assert.Contains(
            "    UPDATE s\n" +
            "    SET s.A = 1\n" +
            "        , s.B = 2\n" +
            "    FROM T AS s\n", result);
    }

    /// <summary>The continuation indent follows the indent settings, not the four characters of "SET ".</summary>
    [Fact]
    public void SetClause_UsesTheConfiguredIndentUnit()
    {
        var opts = Profile();
        opts.IndentStyle = IndentStyleOption.Tabs;

        var result = FormatAndReparse(
            "update s set s.A = o.A, s.B = o.B from #tt s", opts);

        Assert.StartsWith("UPDATE s\nSET s.A = o.A\n\t, s.B = o.B\n", result);
    }

    [Fact]
    public void SetClause_Unaligned_KeepsScriptDomsIndentedLayout()
    {
        var opts = Profile();
        opts.AlignSetWithUpdate = false;

        var result = FormatAndReparse("update s set s.A = o.A, s.B = o.B from #tt s", opts);

        Assert.StartsWith("UPDATE s\n    SET s.A = o.A\n        , s.B = o.B\n", result);
    }

    /// <summary>The FROM/WHERE that follow the set clause are a different clause and must not move with it.</summary>
    [Fact]
    public void SetClause_DoesNotDragTheFollowingClauses()
    {
        var result = FormatAndReparse("update s set s.A = 1 from #tt s where s.Id > 0");

        Assert.Contains("\nFROM #tt AS s\n", result);
        Assert.Contains("\nWHERE s.Id > 0", result);
    }
}
