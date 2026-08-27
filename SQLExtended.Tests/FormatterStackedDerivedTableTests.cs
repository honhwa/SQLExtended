using System.Collections.Generic;
using System.IO;
using SQLExtended.Formatting;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;
using Xunit.Abstractions;

namespace SQLExtended.Tests;

/// <summary>
/// <see cref="FormatterOptions.DerivedTableStackedLayout"/> — a subquery in FROM/JOIN/APPLY reflowed to
/// the shape <see cref="FormatterOptions.CteStackedLayout"/> gives a CTE.
///
/// Two halves are worth pinning separately: what gets reflowed (a table reference, and nothing that merely
/// looks like one — an IN list, an EXISTS, a scalar subquery in a SELECT list all contain "(SELECT" and
/// none of them may be moved), and where the finished block ends up once the FROM/JOIN alignment pass has
/// had it. Both fail as layout rather than as an error, and the tests re-parse the output so a reflow that
/// loses a bracket fails here instead of in a query window.
/// </summary>
public class FormatterStackedDerivedTableTests
{
    private readonly ITestOutputHelper _output;
    public FormatterStackedDerivedTableTests(ITestOutputHelper output) => _output = output;

    private static FormatterOptions Profile() => new FormatterOptions
    {
        KeywordCase = CasingOption.Upper,
        IndentStyle = IndentStyleOption.Spaces,
        IndentSize = 4,
        CommaPosition = CommaPositionOption.LeadingComma,
        LeadingCommaKeepIndent = true,
        SelectColumnLayout = SelectColumnLayoutOption.StackedFirstOnNewLine,
        JoinLayout = JoinLayoutOption.NewLine,
        JoinOnSameLine = true,
        NormalizeJoinKeywords = true,
        AlignFromAndJoins = true,
        WhereConditionLayout = WhereConditionLayoutOption.NewLinePerCondition,
        IndentBetweenConditions = false,
        AliasStyle = AliasStyleOption.AS,
        CteStackedLayout = true,
        DerivedTableStackedLayout = true,
        NewLineBeforeCloseParenthesis = true,
        BlankLinesBetweenStatements = 1,
        MaxLineWidth = 120,
    };

    private string Format(string sql, FormatterOptions opts = null)
    {
        var result = new SqlFormatterService(opts ?? Profile()).Format(sql);
        _output.WriteLine(result.FormattedSql);
        Assert.True(result.Success, result.ErrorMessage);

        string formatted = result.FormattedSql.Replace("\r\n", "\n");
        IList<ParseError> errors;
        using (var reader = new StringReader(formatted))
            new TSql170Parser(true).Parse(reader, out errors);
        Assert.True(errors.Count == 0, errors.Count == 0 ? "" : $"re-parse failed: {errors[0].Message}");
        return formatted;
    }

    // --- what the layout looks like -----------------------------------------------------------

    [Fact]
    public void JoinToDerivedTable_IsStacked()
    {
        var sql = "select a.Id from A a left join (select x, y from B) bb on bb.x = a.Id";

        var expected =
            "FROM A AS a\n" +
            "LEFT JOIN (\n" +
            "    SELECT\n" +
            "        x\n" +
            "        , y\n" +
            "    FROM B\n" +
            ") AS bb ON bb.x = a.Id;";

        Assert.EndsWith(expected, Format(sql));
    }

    /// <summary>
    /// The body is emitted in whole indent units rather than at a column, which is what makes it come out
    /// right under tabs — ScriptDom's own alignment lands on columns that are not multiples of anything.
    /// </summary>
    [Fact]
    public void StackedBody_UsesTheConfiguredIndentUnit()
    {
        var opts = Profile();
        opts.IndentStyle = IndentStyleOption.Tabs;

        var result = Format("select a.Id from A a left join (select x, y from B where y > 1) bb on bb.x = a.Id", opts);

        Assert.EndsWith(
            "LEFT JOIN (\n" +
            "\tSELECT\n" +
            "\t\tx\n" +
            "\t\t, y\n" +
            "\tFROM B\n" +
            "\tWHERE y > 1\n" +
            ") AS bb ON bb.x = a.Id;", result);
    }

    [Fact]
    public void DerivedTableDirectlyInFrom_IsStacked()
    {
        var result = Format("select t.Id from (select x as Id, y from B where y > 1) t where t.Id > 0");

        Assert.Contains("FROM (\n    SELECT\n", result);
        Assert.Contains("\n) AS t\nWHERE t.Id > 0", result);
    }

    /// <summary>ScriptDom keeps an APPLY on the FROM line, so a keyword anchored to column zero misses it.</summary>
    [Fact]
    public void CrossApply_IsStacked()
    {
        var result = Format("select a.Id from A a cross apply (select top 1 z from Z where Z.Id = a.Id order by z desc) zz");

        Assert.Contains("FROM A AS a CROSS APPLY (\n", result);
        Assert.Contains("\n) AS zz;", result);
    }

    [Fact]
    public void NestedDerivedTables_AreEachStackedAtTheirOwnLevel()
    {
        var sql = "select a.Id from A a left join (select b.x from B b " +
                  "left join (select q from Q) qq on qq.q = b.x) bb on bb.x = a.Id";

        var result = Format(sql);

        Assert.Contains(
            "LEFT JOIN (\n" +
            "    SELECT\n" +
            "        b.x\n" +
            "    FROM B AS b\n" +
            "    LEFT JOIN (\n" +
            "        SELECT\n" +
            "            q\n" +
            "        FROM Q\n" +
            "    ) AS qq ON qq.q = b.x\n" +
            ") AS bb ON bb.x = a.Id;", result);
    }

    /// <summary>
    /// The block is laid out before the FROM/JOIN alignment pass runs, so the body and the closing ")"
    /// have to travel with the "LEFT JOIN (" line when that pass pulls it out to the FROM's column. Left
    /// behind, they stay in the column ScriptDom had the opener in and the block reads as broken.
    /// </summary>
    [Fact]
    public void StackedBlock_MovesWithItsOpenerWhenJoinsAreAligned()
    {
        var result = Format("select a.Id from A a left join (select x, y from B) bb on bb.x = a.Id " +
                            "left join C c on c.Id = a.Id");

        foreach (var expected in new[] { "\nLEFT JOIN (\n", "\n    SELECT\n", "\n) AS bb ON bb.x = a.Id\n", "\nLEFT JOIN C AS c ON c.Id = a.Id" })
            Assert.Contains(expected, result);
    }

    // --- what must not be reflowed ------------------------------------------------------------

    /// <summary>
    /// These three all contain "(SELECT" and none is a table reference. Reflowing one would move a
    /// predicate or a column expression into a shape that reads as a join.
    /// </summary>
    [Theory]
    [InlineData("select a.Id from A a where a.Id in (select x from B where x > 1 and x < 99 and x <> 5)", "IN (SELECT")]
    [InlineData("select a.Id from A a where exists (select 1 from B b where b.Id = a.Id and b.Flag = 1 and b.Other = 2)", "EXISTS (SELECT")]
    [InlineData("select a.Id, (select sum(l.Qty) from Lines l where l.OrderId = a.Id and l.Active = 1) as Total from A a", "(SELECT SUM")]
    public void SubqueriesThatAreNotTableReferences_AreLeftAlone(string sql, string keptInline)
    {
        Assert.Contains(keptInline, Format(sql));
    }

    /// <summary>A "(select …" inside a string literal is text, and the scanner has to skip it.</summary>
    [Fact]
    public void ParenthesisInsideAStringLiteral_IsNotTreatedAsASubquery()
    {
        var result = Format("select a.Id from A a where a.Note = '(select x from B' and a.Other = 1");

        Assert.Contains("'(select x from B'", result);
    }

    [Fact]
    public void Off_LeavesScriptDomsLayout()
    {
        var opts = Profile();
        opts.DerivedTableStackedLayout = false;

        var result = Format("select a.Id from A a left join (select x, y from B) bb on bb.x = a.Id", opts);

        Assert.Contains("LEFT JOIN (SELECT x", result);
        Assert.DoesNotContain("LEFT JOIN (\n", result);
    }

    // --- the query this came from -------------------------------------------------------------

    [Fact]
    public void JoinsToDerivedTablesInsideAWiderStatement_AllStack()
    {
        var sql = @"SELECT 'FTE_Date' = v.PeriodStart
FROM
    DW.dbo.vTransCurrentMaster
    LEFT JOIN DW.dbo.vEmployee ON (vTransCurrentMaster.EmployeeCode = vEmployee.EmployeeCode)
    LEFT JOIN

                    (SELECT
                    'EmployeeCode' = CPC.EmployeeCode,
                    'Percentage' = PercentageSplit/100
                    FROM
                    DW.[dbo].[vCurrentProportionalCosting] CPC
                    LEFT JOIN DW.[dbo].vEmployee Emp ON (CPC.EmployeeCode=Emp.EmployeeCode)
                    WHERE
                    TerminationDate IS NULL) CC ON (vTransCurrentMaster.PaySequence = CC.PaySequence)
    LEFT JOIN
                (SELECT EmployeeCode, PaySequence, sum(Quantity) AS Qty
                FROM DW.dbo.[vEmployeeAllowanceView]
                WHERE AllowanceCode like '070' GROUP BY EmployeeCode, PaySequence)
                AL ON (vTransCurrentMaster.Employeecode = AL.Employeecode)
WHERE v.PeriodStart >= '2011-01-01'";

        var result = Format(sql);

        // Both derived tables stacked, both aliases carrying their ON, and the outer clauses at the margin.
        Assert.Contains("\nLEFT JOIN (\n    SELECT\n", result);
        Assert.Contains("\n) AS CC ON (vTransCurrentMaster.PaySequence = CC.PaySequence)\n", result);
        Assert.Contains("\n) AS AL ON (vTransCurrentMaster.Employeecode = AL.Employeecode)\n", result);
        Assert.Contains("\nWHERE v.PeriodStart >= '2011-01-01'", result);

        // The inner query's own FROM/JOIN sit one level in, not at the outer margin.
        Assert.Contains("\n    FROM DW.[dbo].[vCurrentProportionalCosting] AS CPC\n", result);
        Assert.Contains("\n    LEFT JOIN DW.[dbo].vEmployee AS Emp ON (CPC.EmployeeCode = Emp.EmployeeCode)\n", result);
    }
}
