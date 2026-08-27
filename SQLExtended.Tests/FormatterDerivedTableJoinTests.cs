using System.Collections.Generic;
using System.IO;
using SQLExtended.Formatting;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;
using Xunit.Abstractions;

namespace SQLExtended.Tests;

/// <summary>
/// Joins to a derived table, which is where the line-based FROM/JOIN passes used to lose track.
/// ScriptDom emits such a join across as many lines as the subquery body needs:
///
///     FROM A AS a
///          LEFT OUTER JOIN
///          (SELECT x,
///                  y
///           FROM B) AS bb
///          ON bb.x = a.Id
///
/// so the line the ON has to be merged onto is the one carrying ") AS bb", not a JOIN line — and the
/// subquery's own FROM/WHERE are a query of their own that the outer alignment must not read as its own.
/// Both failures are silent: the SQL stays valid and only the layout is wrong.
/// </summary>
public class FormatterDerivedTableJoinTests
{
    private readonly ITestOutputHelper _output;
    public FormatterDerivedTableJoinTests(ITestOutputHelper output) => _output = output;

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

    /// <summary>The reported symptom: the ON stayed on a line of its own for subquery joins only.</summary>
    [Fact]
    public void OnClause_MergesOntoTheDerivedTablesAliasLine()
    {
        var result = Format("select a.Id from A a left join (select x, y from B) bb on bb.x = a.Id");

        Assert.Contains(") AS bb ON bb.x = a.Id", result);
        Assert.DoesNotContain("\nON bb.x", result);
    }

    /// <summary>
    /// The collateral damage from the same input: the subquery's own FROM became the alignment target,
    /// so every join *after* it was indented to wherever that inner FROM happened to sit.
    /// </summary>
    [Fact]
    public void JoinsAfterADerivedTable_StayAlignedWithTheOuterFrom()
    {
        var sql = "select a.Id from A a " +
                  "left join (select x, y from B) bb on bb.x = a.Id " +
                  "left join C c on c.Id = a.Id " +
                  "left join (select p from D) dd on dd.p = a.Id";

        var result = Format(sql);

        Assert.Contains("\nFROM A AS a\n", result);
        Assert.Contains("\nLEFT JOIN (SELECT x", result);
        Assert.Contains("\nLEFT JOIN C AS c ON c.Id = a.Id\n", result);
        Assert.Contains("\nLEFT JOIN (SELECT p", result);
    }

    /// <summary>
    /// The subquery is a query in its own right, so its joins align to its own FROM — the point of
    /// keying the alignment target by paren depth rather than skipping nested lines outright.
    /// </summary>
    [Fact]
    public void JoinsInsideADerivedTable_AlignWithTheirOwnFrom()
    {
        var sql = "select a.Id from A a left join (select e.x from E e left join F f on f.Id = e.Id) bb on bb.x = a.Id";

        var result = Format(sql);
        var lines = result.Split('\n');

        string innerFrom = System.Array.Find(lines, l => l.TrimStart().StartsWith("FROM E"));
        string innerJoin = System.Array.Find(lines, l => l.TrimStart().StartsWith("LEFT JOIN F"));
        Assert.NotNull(innerFrom);
        Assert.NotNull(innerJoin);
        Assert.Equal(innerFrom.Length - innerFrom.TrimStart().Length,
                     innerJoin.Length - innerJoin.TrimStart().Length);
    }

    /// <summary>
    /// A JOIN whose ON never arrives must not adopt the next ON it sees — here the CROSS JOIN sits
    /// between an inner join and the following one's ON.
    /// </summary>
    [Fact]
    public void CrossJoin_DoesNotStealTheFollowingOn()
    {
        var result = Format("select a.Id from A a cross join B b inner join C c on c.Id = a.Id");

        Assert.Contains("CROSS JOIN B AS b", result);
        Assert.Contains("INNER JOIN C AS c ON c.Id = a.Id", result);
        Assert.DoesNotContain("CROSS JOIN B AS b ON", result);
    }

    /// <summary>
    /// The subquery's WHERE used to cancel the outer query's alignment target, and its SELECT/FROM used
    /// to replace it. A join after a subquery containing all three is the case that proves neither happens.
    /// </summary>
    [Fact]
    public void DerivedTableContainingWhereAndGroupBy_DoesNotDisturbTheOuterQuery()
    {
        var sql = "select a.Id from A a " +
                  "left join (select EmployeeCode, sum(Quantity) as Qty from V where Code like '070' " +
                  "group by EmployeeCode) al on al.EmployeeCode = a.Id " +
                  "left join Z z on z.Id = a.Id where a.Deleted is null";

        var result = Format(sql);

        Assert.Contains(") AS al ON al.EmployeeCode = a.Id", result);
        Assert.Contains("\nLEFT JOIN Z AS z ON z.Id = a.Id\n", result);
        Assert.Contains("\nWHERE a.Deleted IS NULL", result);
    }

    /// <summary>
    /// Merging an ON onto a line that ends in a comment would comment the clause out — the join would
    /// still be there and would silently become a cross join.
    /// </summary>
    [Fact]
    public void OnClause_IsNotMergedIntoALineComment()
    {
        var result = Format("select a.Id from A a left join (select x from B) bb -- the subquery\n on bb.x = a.Id");

        Assert.DoesNotContain("-- the subquery ON", result);
        Assert.Contains("ON bb.x = a.Id", result);
    }
}
