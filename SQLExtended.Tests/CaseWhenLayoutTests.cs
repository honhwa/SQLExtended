using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLExtended.Formatting;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;
using Xunit.Abstractions;

namespace SQLExtended.Tests;

/// <summary>
/// Tests for FormatterOptions.CaseWhenLayout.
///
/// ScriptDom has no CASE layout of its own: the whole expression comes back on one line, broken only where
/// a WHEN's condition is a multi-part boolean — and that break is indented to the column the condition
/// started in, so a CASE with several WHENs staircases hundreds of characters to the right. The pass
/// flattens the region and re-emits it, which is why every test here **re-parses the output**: a reflow that
/// drops a keyword, joins two clauses, or pulls the tail of the expression into a "--" comment still leaves
/// text that Assert.Contains is perfectly happy with.
/// </summary>
public class CaseWhenLayoutTests
{
    private readonly ITestOutputHelper _output;

    public CaseWhenLayoutTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static FormatterOptions Options(CaseWhenLayoutOption layout) => new FormatterOptions
    {
        KeywordCase = CasingOption.Upper,
        IndentStyle = IndentStyleOption.Spaces,
        IndentSize = 4,
        CaseWhenLayout = layout,
        AliasStyle = AliasStyleOption.Unchanged,
        BracketQuoting = BracketQuotingOption.Unchanged,
        TrailingSemicolon = SemicolonOption.Unchanged,
    };

    private string Format(string sql, CaseWhenLayoutOption layout, Action<FormatterOptions> tweak = null)
    {
        var options = Options(layout);
        tweak?.Invoke(options);

        var result = new SqlFormatterService(options).Format(sql);
        _output.WriteLine($"=== {layout} (success={result.Success}) ===");
        _output.WriteLine(result.FormattedSql);
        Assert.True(result.Success, result.ErrorMessage);

        Reparse(result.FormattedSql);
        return result.FormattedSql;
    }

    /// <summary>Parses the formatted text, failing with the parser's own message. A reflow that commented
    /// out a clause leaves it present in the text — only the parser notices.</summary>
    private static TSqlFragment Reparse(string sql)
    {
        var parser = new TSql170Parser(true);
        IList<ParseError> errors;
        TSqlFragment fragment;
        using (var reader = new StringReader(sql))
            fragment = parser.Parse(reader, out errors);

        Assert.True(errors.Count == 0, errors.Count == 0 ? "" : $"output does not re-parse: line {errors[0].Line}: {errors[0].Message}\n{sql}");
        return fragment;
    }

    private static string[] Lines(string sql) => sql.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

    private static int IndentOf(string line) => line.Length - line.TrimStart().Length;

    private static string LineWith(string sql, string text) =>
        Lines(sql).FirstOrDefault(l => l.Contains(text)) ?? throw new Xunit.Sdk.XunitException($"no line contains \"{text}\":\n{sql}");

    private const string TwoWhens =
        "SELECT CASE WHEN o.Status = 1 THEN 'New' WHEN o.Status = 2 THEN 'Open' ELSE 'Closed' END AS Label FROM dbo.Orders AS o;";

    [Fact]
    public void Unchanged_IsANoOp()
    {
        var options = Options(CaseWhenLayoutOption.Unchanged);
        string baseline = new SqlFormatterService(options).Format(TwoWhens).FormattedSql;

        Assert.Equal(baseline, Format(TwoWhens, CaseWhenLayoutOption.Unchanged));
        Assert.Contains("CASE WHEN o.Status = 1 THEN 'New' WHEN o.Status = 2", baseline);
    }

    [Fact]
    public void Stacked_PutsEveryWhenAndElseOnItsOwnLine()
    {
        string result = Format(TwoWhens, CaseWhenLayoutOption.Stacked);
        var lines = Lines(result);

        Assert.Single(lines, l => l.TrimEnd().EndsWith("CASE"));
        Assert.Equal(2, lines.Count(l => l.TrimStart().StartsWith("WHEN ")));
        Assert.Single(lines, l => l.TrimStart().StartsWith("ELSE "));
        Assert.Contains(lines, l => l.TrimStart().StartsWith("END"));
    }

    [Fact]
    public void Stacked_AlignsTheBodyUnderTheCaseKeywordAndBringsEndBackToIt()
    {
        string result = Format(TwoWhens, CaseWhenLayoutOption.Stacked);

        string caseLine = LineWith(result, "CASE");
        int caseColumn = caseLine.IndexOf("CASE", StringComparison.Ordinal);

        // Everything the pass emits is measured from the column the CASE keyword occupies, not from the
        // line's indent: a nested CASE starts mid-line ("ISNULL(CASE") and there is no other anchor.
        Assert.Equal(caseColumn + 4, IndentOf(LineWith(result, "WHEN o.Status = 1")));
        Assert.Equal(caseColumn + 4, IndentOf(LineWith(result, "ELSE 'Closed'")));
        Assert.Equal(caseColumn, IndentOf(Lines(result).First(l => l.TrimStart().StartsWith("END"))));
    }

    [Fact]
    public void Stacked_KeepsTheAliasOnTheEndLine()
    {
        string result = Format(TwoWhens, CaseWhenLayoutOption.Stacked);
        Assert.Contains("END AS Label", result);
    }

    [Fact]
    public void WhenAligned_KeepsTheFirstWhenOnTheCaseLineAndAlignsTheRestUnderIt()
    {
        string result = Format(TwoWhens, CaseWhenLayoutOption.WhenAligned);

        string caseLine = LineWith(result, "CASE WHEN o.Status = 1");
        int whenColumn = caseLine.IndexOf("WHEN", StringComparison.Ordinal);

        Assert.Equal(whenColumn, IndentOf(LineWith(result, "WHEN o.Status = 2")));
        Assert.Equal(whenColumn, IndentOf(LineWith(result, "ELSE 'Closed'")));
        Assert.Equal(caseLine.IndexOf("CASE", StringComparison.Ordinal),
                     IndentOf(Lines(result).First(l => l.TrimStart().StartsWith("END"))));
    }

    [Fact]
    public void NestedCaseIsReflowedAgainstItsOwnColumn()
    {
        string result = Format(
            "SELECT CASE WHEN a = 1 THEN 1 ELSE (CASE WHEN b = 2 THEN 2 ELSE 3 END) END AS X FROM dbo.T;",
            CaseWhenLayoutOption.Stacked);

        string elseLine = LineWith(result, "ELSE (CASE");
        int innerCaseColumn = elseLine.IndexOf("CASE", StringComparison.Ordinal);

        Assert.Equal(innerCaseColumn + 4, IndentOf(LineWith(result, "WHEN b = 2")));
        Assert.Equal(innerCaseColumn, IndentOf(LineWith(result, "END)")));
    }

    [Fact]
    public void ASimpleCaseKeepsItsInputExpressionOnTheCaseLine()
    {
        string result = Format(
            "SELECT CASE o.Status WHEN 1 THEN 'New' WHEN 2 THEN 'Open' ELSE 'Closed' END AS Label FROM dbo.Orders AS o;",
            CaseWhenLayoutOption.Stacked);

        Assert.Contains("CASE o.Status", LineWith(result, "CASE"));
        Assert.Equal(2, Lines(result).Count(l => l.TrimStart().StartsWith("WHEN ")));
    }

    [Fact]
    public void TheStaircaseScriptDomProducesForAMultiPartConditionIsDiscarded()
    {
        // The one break ScriptDom does make is indented to the column the condition started in, and the
        // columns compound across WHENs. Flattening the region before re-emitting it is what undoes that;
        // a pass that only inserted line breaks would inherit the runaway indent.
        string result = Format(
            "SELECT CASE WHEN (a = 1 AND b = 2) THEN 1 WHEN (c = 3 AND d = 4) THEN 2 WHEN (e = 5 AND f = 6) THEN 3 ELSE 0 END AS X FROM dbo.T;",
            CaseWhenLayoutOption.Stacked);

        var whenLines = Lines(result).Where(l => l.TrimStart().StartsWith("WHEN ")).ToList();
        Assert.Equal(3, whenLines.Count);
        Assert.Single(whenLines.Select(IndentOf).Distinct());
        Assert.Contains("WHEN (c = 3 AND d = 4) THEN 2", result);   // the condition is back on one line
    }

    // ───────────────────────────────────────────────
    //  Comments — the failure this file has shipped twice
    // ───────────────────────────────────────────────

    [Fact]
    public void ACommentInsideTheCaseIsKeptAndLandsOnALineOfItsOwn()
    {
        string result = Format(@"SELECT CASE
    -- statuses come from dbo.OrderStatus
    WHEN o.Status = 1 THEN 'New'
    ELSE 'Closed'
END AS Label
FROM dbo.Orders AS o;", CaseWhenLayoutOption.Stacked);

        Assert.Contains("-- statuses come from dbo.OrderStatus", result);

        // Flattening the region would have pulled the rest of the CASE into the comment. It re-parses
        // above; this is what says the WHEN survived as code rather than as prose.
        string commentLine = LineWith(result, "-- statuses");
        Assert.EndsWith("-- statuses come from dbo.OrderStatus", commentLine.TrimEnd());
        Assert.Contains(Lines(result), l => l.TrimStart().StartsWith("WHEN o.Status = 1 THEN 'New'"));
    }

    [Fact]
    public void ACommentIsKeptWhereverInTheCaseItWasWritten()
    {
        // The positions that break are not the ones anyone thinks to write a case for, so walk one down
        // every line of the same CASE.
        string[] lines =
        {
            "SELECT CASE",
            "    WHEN o.Status = 1 THEN 'New'",
            "    WHEN o.Status = 2 THEN 'Open'",
            "    ELSE 'Closed'",
            "END AS Label",
            "FROM dbo.Orders AS o;"
        };

        for (int i = 0; i < lines.Length; i++)
        {
            var withComment = (string[])lines.Clone();
            withComment[i] = withComment[i] + " -- note " + i;

            string result = Format(string.Join(Environment.NewLine, withComment), CaseWhenLayoutOption.Stacked);
            Assert.Contains("-- note " + i, result);
        }
    }

    // ───────────────────────────────────────────────
    //  Everything a CASE can sit inside
    // ───────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM dbo.T AS t WHERE CASE WHEN t.a = 1 THEN 1 WHEN t.b = 2 THEN 1 ELSE 0 END = 1;")]
    [InlineData("SELECT * FROM dbo.A AS a JOIN dbo.B AS b ON b.Id = CASE WHEN a.Kind = 1 THEN a.X WHEN a.Kind = 2 THEN a.Y ELSE a.Z END;")]
    [InlineData("SELECT t.a, SUM(CASE WHEN t.b = 1 THEN 1 WHEN t.b = 2 THEN 2 ELSE 0 END) AS N FROM dbo.T AS t GROUP BY t.a ORDER BY CASE WHEN t.a = 1 THEN 0 WHEN t.a = 2 THEN 1 ELSE 2 END;")]
    [InlineData("UPDATE dbo.T SET a = CASE WHEN b = 1 THEN 'x' WHEN b = 2 THEN 'y' ELSE 'z' END WHERE Id = 1;")]
    [InlineData("INSERT INTO dbo.T (a, b) VALUES (CASE WHEN 1 = 1 THEN 1 WHEN 2 = 2 THEN 2 ELSE 3 END, 2);")]
    [InlineData("INSERT INTO dbo.T (a, b) SELECT CASE WHEN s.x = 1 THEN 1 WHEN s.x = 2 THEN 2 ELSE 3 END, s.y FROM dbo.S AS s;")]
    [InlineData("WITH cte AS (SELECT CASE WHEN a = 1 THEN 1 WHEN a = 2 THEN 2 ELSE 0 END AS N FROM dbo.T) SELECT * FROM cte;")]
    [InlineData("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.B AS b WHERE b.Id = a.Id) THEN 1 WHEN a.X > 0 THEN 2 ELSE 0 END AS N FROM dbo.A AS a;")]
    [InlineData("SELECT CASE WHEN a.X IN (SELECT b.X FROM dbo.B AS b) THEN 1 WHEN a.X > 0 THEN 2 ELSE 0 END AS N FROM dbo.A AS a;")]
    public void EveryPlaceACaseCanSitStillReparses(string sql)
    {
        Format(sql, CaseWhenLayoutOption.Stacked);
        Format(sql, CaseWhenLayoutOption.WhenAligned);
    }

    [Fact]
    public void MergeWhenClausesAreNotCaseClauses()
    {
        // MERGE's WHEN MATCHED lives outside any CASE, so the scan must never reach it — and the CASE in
        // the same statement must still be reflowed.
        string result = Format(@"MERGE dbo.Target AS t
USING dbo.Source AS s ON s.Id = t.Id
WHEN MATCHED THEN UPDATE SET t.v = CASE WHEN s.v = 1 THEN 'a' WHEN s.v = 2 THEN 'b' ELSE 'c' END
WHEN NOT MATCHED THEN INSERT (Id, v) VALUES (s.Id, s.v);", CaseWhenLayoutOption.Stacked);

        Assert.Contains("WHEN MATCHED THEN UPDATE", result);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", result);
        Assert.Equal(2, Lines(result).Count(l => l.TrimStart().StartsWith("WHEN s.v")));
    }

    [Fact]
    public void KeywordsInsideLiteralsAndBracketedNamesAreNotClauses()
    {
        string result = Format(
            "SELECT CASE WHEN t.[END] = 'WHEN a THEN b' THEN 'CASE WHEN' WHEN t.[CASE] = 1 THEN 'x' ELSE 'y' END AS [ELSE] FROM dbo.T AS t;",
            CaseWhenLayoutOption.Stacked);

        Assert.Contains("'WHEN a THEN b'", result);
        Assert.Contains("'CASE WHEN'", result);
        Assert.Equal(2, Lines(result).Count(l => l.TrimStart().StartsWith("WHEN t.")));
    }

    [Fact]
    public void ALiteralSpanningLinesIsCopiedThroughUntouched()
    {
        string result = Format("SELECT CASE WHEN a = 1 THEN 'one\r\ntwo' WHEN a = 2 THEN 'b' ELSE 'c' END AS X FROM dbo.T;",
            CaseWhenLayoutOption.Stacked);

        Assert.Contains("'one\r\ntwo'", result);
    }

    [Fact]
    public void ABeginEndBlockAroundTheCaseIsNotMistakenForItsEnd()
    {
        string result = Format(@"CREATE PROCEDURE dbo.P
AS
BEGIN
    SELECT CASE WHEN a = 1 THEN 1 WHEN a = 2 THEN 2 ELSE 0 END AS X FROM dbo.T;
END", CaseWhenLayoutOption.Stacked);

        Assert.Equal(2, Lines(result).Count(l => l.TrimStart().StartsWith("WHEN a =")));
    }

    // ───────────────────────────────────────────────
    //  Interaction with the passes on either side
    // ───────────────────────────────────────────────

    [Fact]
    public void TheItemAfterAMultiLineCaseStillGetsItsLeadingComma()
    {
        // The comma pass compares the next item's indent against the line it is coming off — which, for a
        // reflowed CASE, is the END rather than the line the item started on. Before it learned to compare
        // against the item's own indent, the comma stayed stranded at the end of the END line and the
        // column below it began with none.
        string result = Format(
            "SELECT CASE WHEN a = 1 THEN 1 WHEN a = 2 THEN 2 ELSE 0 END AS N, t.Other, t.Third FROM dbo.T AS t;",
            CaseWhenLayoutOption.Stacked,
            o => o.CommaPosition = CommaPositionOption.LeadingComma);

        Assert.DoesNotContain("END AS N,", result);
        Assert.Contains(", t.Other", result);
        Assert.Contains(", t.Third", result);
    }

    [Fact]
    public void TheSemicolonPassStillFindsTheEndOfTheStatement()
    {
        // The semicolon pass reads line ends, and a reflowed CASE gives it several lines that end in nothing
        // in particular — including one that is just "END". A semicolon on the wrong one splits the
        // expression in half.
        string result = Format(
            "SELECT CASE WHEN a = 1 THEN 1 WHEN a = 2 THEN 2 ELSE 0 END AS N FROM dbo.T; SELECT CASE WHEN b = 1 THEN 1 WHEN b = 2 THEN 2 ELSE 0 END AS M FROM dbo.U;",
            CaseWhenLayoutOption.Stacked,
            o => o.TrailingSemicolon = SemicolonOption.Always);

        Assert.Equal(2, Lines(result).Count(l => l.TrimEnd().EndsWith(";")));
    }

    [Fact]
    public void TabIndentedScriptsStayTabIndented()
    {
        string result = Format(TwoWhens, CaseWhenLayoutOption.Stacked, o => o.IndentStyle = IndentStyleOption.Tabs);

        string whenLine = LineWith(result, "WHEN o.Status = 1");
        Assert.Contains("\t", whenLine.Substring(0, whenLine.Length - whenLine.TrimStart().Length));
    }

    [Fact]
    public void TheAliasIsLiftedBeforeTheBodyIsAlignedToTheCase()
    {
        // ColumnEquals moves the alias to the front of the item, which moves the CASE keyword sideways.
        // Reflowing first left the WHENs and the END lined up on the column the CASE used to be in.
        string result = Format(TwoWhens, CaseWhenLayoutOption.Stacked, o => o.AliasStyle = AliasStyleOption.ColumnEquals);

        Assert.Contains("Label = CASE", result);

        string caseLine = LineWith(result, "Label = CASE");
        int caseColumn = caseLine.IndexOf("CASE", StringComparison.Ordinal);
        Assert.Equal(caseColumn + 4, IndentOf(LineWith(result, "WHEN o.Status = 1")));
        Assert.Equal(caseColumn, IndentOf(Lines(result).First(l => l.TrimStart().StartsWith("END"))));
    }
}
