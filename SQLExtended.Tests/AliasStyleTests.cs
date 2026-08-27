using SQLExtended.Formatting;
using Xunit;
using Xunit.Abstractions;

namespace SQLExtended.Tests;

/// <summary>
/// Tests for FormatterOptions.AliasStyle.
///
/// The AS option used to run a bare "identifier identifier" regex over the whole script, which matched
/// far more than aliases — "SET ANSI_NULLS ON" became "SET AS ANSI_NULLS ON", "IS NULL" became
/// "IS AS NULL", and comment prose was rewritten too. It is a no-op now: ScriptDom's generator already
/// emits AS before every alias. NoAS had the mirror-image bug, stripping the AS out of CAST(x AS INT).
/// </summary>
public class AliasStyleTests
{
    private readonly ITestOutputHelper _output;

    public AliasStyleTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static FormatterOptions Options(AliasStyleOption style) => new FormatterOptions
    {
        KeywordCase = CasingOption.Upper,
        IndentStyle = IndentStyleOption.Spaces,
        IndentSize = 4,
        AliasStyle = style,
        BracketQuoting = BracketQuotingOption.Unchanged,
        TrailingSemicolon = SemicolonOption.Unchanged,
    };

    private string Format(string sql, AliasStyleOption style)
    {
        var result = new SqlFormatterService(Options(style)).Format(sql);
        _output.WriteLine($"=== {style} (success={result.Success}) ===");
        _output.WriteLine(result.FormattedSql);
        Assert.True(result.Success, result.ErrorMessage);
        return result.FormattedSql;
    }

    /// <summary>Formats, then re-parses the output. Any AS that was added or removed in the wrong
    /// place shows up here as SQL that no longer parses.</summary>
    private string FormatAndReparse(string sql, AliasStyleOption style)
    {
        string formatted = Format(sql, style);
        var reparse = new SqlFormatterService(Options(AliasStyleOption.Unchanged)).Format(formatted);
        Assert.True(reparse.Success, $"{style} output does not re-parse: {reparse.ErrorMessage}");
        return formatted;
    }

    private const string ReportedProc = @"USE [Reporting]
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
-- =============================================
-- Author:        Alex Rivera
-- Description:    re-deploy alternate freight report
-- =============================================
CREATE PROCEDURE [dbo].[GEN_Exception_Report]
(@Sitename nvarchar(100))
AS
BEGIN
SELECT mc.description AS 'Description', m.Ordernbr, t.Team
FROM SalesArchive.dbo.tblnotes AS fn
JOIN SalesArchive.dbo.main AS m ON fn.ordernbr=m.ordernbr
LEFT JOIN SalesArchive.dbo.tblteam AS t ON fn.team=t.nbr
LEFT JOIN (
    SELECT '#Imports' = COUNT(CASE WHEN (ls.linestatus IS NULL or ls.Linestatus = '') THEN sol1.ItemNbr ELSE NULL END)
    , sol1.OrderNbr
    FROM SalesArchive.dbo.tblSubOrderLine2 sol1
    LEFT JOIN SalesArchive.dbo.tblLineStatus ls ON sol1.LineStatus = ls.Nbr
    GROUP BY sol1.OrderNbr
) Imports ON fn.OrderNbr = Imports.OrderNbr
WHERE mc.description = 'Alternate Freight Carrier' AND fn.cancellation IS NULL
END
";

    /// <summary>Every AS in a script that the alias passes must never touch.</summary>
    private const string NonAliasAs = @"DECLARE @x AS INT;
CREATE TYPE dbo.MyList AS TABLE (Id INT);
GO
CREATE VIEW dbo.V WITH SCHEMABINDING AS SELECT 1 AS One;
GO
CREATE PROCEDURE dbo.P WITH EXECUTE AS OWNER AS
BEGIN
    WITH cte AS (SELECT CAST(a.Id AS BIGINT) AS BigId FROM dbo.T AS a)
    SELECT TRY_CAST(c.BigId AS NVARCHAR(50)) AS Txt, x.v AS Val
    FROM cte AS c
    CROSS APPLY (SELECT 1 AS v) AS x
    INNER JOIN dbo.Other AS o ON o.Id = c.BigId
    WHERE o.Name IS NOT NULL;
END
";

    // ───────────────────────────────────────────────
    //  AliasStyle = AS — the reported corruption
    // ───────────────────────────────────────────────

    [Fact]
    public void AS_DoesNotInsertAsIntoStatementsOrComments()
    {
        var result = FormatAndReparse(ReportedProc, AliasStyleOption.AS);

        Assert.DoesNotContain("SET AS ", result);
        Assert.DoesNotContain("IS AS NULL", result);
        Assert.DoesNotContain("Alex AS Rivera", result);
        Assert.DoesNotContain("freight AS report", result);

        // and the things that were right are still right
        Assert.Contains("SET ANSI_NULLS ON", result);
        Assert.Contains("IS NULL", result);
        Assert.Contains("-- Author:        Alex Rivera", result);
    }

    [Fact]
    public void AS_ProducesTheSameOutputAsUnchanged()
    {
        // ScriptDom's generator already emits AS before every alias (the AST doesn't record whether
        // the source had one), so there is nothing for the AS option to add. If this ever fails,
        // ScriptDom's behaviour changed — don't "fix" it by reinstating a text pass.
        Assert.Equal(Format(ReportedProc, AliasStyleOption.Unchanged), Format(ReportedProc, AliasStyleOption.AS));
        Assert.Equal(Format(NonAliasAs, AliasStyleOption.Unchanged), Format(NonAliasAs, AliasStyleOption.AS));
    }

    [Fact]
    public void AS_AddsAsToAliasesThatLackedIt()
    {
        var result = FormatAndReparse("SELECT c.Name n FROM dbo.Customers c JOIN dbo.Orders o ON o.Id = c.Id;", AliasStyleOption.AS);

        Assert.Contains("c.Name AS n", result);
        Assert.Contains("dbo.Customers AS c", result);
        Assert.Contains("dbo.Orders AS o", result);
    }

    // ───────────────────────────────────────────────
    //  AliasStyle = NoAS
    // ───────────────────────────────────────────────

    [Fact]
    public void NoAS_RemovesTableAndColumnAliasesOnly()
    {
        var result = FormatAndReparse(
            "SELECT CAST(c.Id AS INT) AS Ident, c.Name AS n FROM dbo.Customers AS c LEFT JOIN dbo.Orders AS o ON o.CustomerId = c.Id;",
            AliasStyleOption.NoAS);

        Assert.Contains("AS INT", result);          // the CAST keeps its AS
        Assert.Contains(") Ident", result);
        Assert.Contains("c.Name n", result);
        Assert.Contains("dbo.Customers c", result);
        Assert.Contains("dbo.Orders o", result);
    }

    [Fact]
    public void NoAS_LeavesEveryNonAliasAsAlone()
    {
        var result = FormatAndReparse(NonAliasAs, AliasStyleOption.NoAS);

        Assert.Contains("DECLARE @x AS INT", result);
        Assert.Contains("AS TABLE", result);
        Assert.Contains("EXECUTE AS OWNER", result);
        Assert.Contains("CAST (a.Id AS BIGINT)", result);
        Assert.Contains("TRY_CAST (c.BigId AS NVARCHAR(50))", result);
        Assert.Contains("AS (SELECT", result);      // the CTE body

        // ...while the aliases in the same script did lose theirs
        Assert.Contains("FROM dbo.T a", result);
        Assert.Contains("FROM cte c", result);
        Assert.Contains("INNER JOIN dbo.Other o", result);
    }

    [Fact]
    public void NoAS_HandlesDerivedTableAndQuotedAliases()
    {
        var result = FormatAndReparse(ReportedProc, AliasStyleOption.NoAS);

        Assert.Contains("mc.description 'Description'", result);
        Assert.Contains(") Imports", result);
        Assert.Contains("FROM SalesArchive.dbo.tblnotes fn", result);
        Assert.DoesNotContain(" AS ", result);
    }

    // ───────────────────────────────────────────────
    //  AliasStyle = ColumnEquals (regression guard for the shared walker)
    // ───────────────────────────────────────────────

    [Fact]
    public void ColumnEquals_StillLeavesNonAliasAsAlone()
    {
        var result = FormatAndReparse(NonAliasAs, AliasStyleOption.ColumnEquals);

        Assert.Contains("BigId = CAST (a.Id AS BIGINT)", result);
        Assert.Contains("Txt = TRY_CAST (c.BigId AS NVARCHAR(50))", result);
        Assert.Contains("FROM dbo.T AS a", result);   // table aliases untouched
        Assert.Contains("DECLARE @x AS INT", result);
    }

    /// <summary>
    /// A warehouse SELECT list is mostly quoted and hash-prefixed aliases, and while those were skipped the
    /// option looked like it only worked on half the query — every AS the user was watching for stayed where
    /// it was. Re-parsing is what says the rewritten form is real SQL rather than a comparison: an alias to
    /// the left of "=" and a predicate against a literal are the same characters.
    /// </summary>
    [Fact]
    public void ColumnEquals_HandlesEverySpellingAnAliasArrivesIn()
    {
        var result = FormatAndReparse(
            @"SELECT SUM(t.a) AS #Ongoing
     , ISNULL(SUM(t.b), 0) AS 'PKG_#QTY'
     , t.c AS 'Split ship'
     , t.d AS [PRIOR ORDERID]
     , t.e AS Plain
     , t.f AS [Simple]
FROM dbo.T AS t;", AliasStyleOption.ColumnEquals);

        Assert.Contains("#Ongoing = SUM(t.a)", result);
        Assert.Contains("'PKG_#QTY' = ISNULL(SUM(t.b), 0)", result);
        Assert.Contains("'Split ship' = t.c", result);
        Assert.Contains("[PRIOR ORDERID] = t.d", result);
        Assert.Contains("Plain = t.e", result);
        Assert.Contains("Simple = t.f", result);      // brackets it never needed are dropped
        int selectAt = result.IndexOf("SELECT");
        Assert.DoesNotContain(" AS ", result.Substring(selectAt, result.IndexOf("FROM") - selectAt));   // nothing left for AS to do
    }

    /// <summary>
    /// Re-quoting the alias would be a rename. Bracketed names are the only thing IdentifierCase touches, so
    /// turning 'Split ship' into [Split ship] hands it to that pass — and under IdentifierCase = Upper the
    /// result set's column heading comes back as SPLIT SHIP. Choosing an alias style must not rename a column.
    /// </summary>
    [Fact]
    public void ColumnEquals_DoesNotRenameTheColumnItMoves()
    {
        var options = Options(AliasStyleOption.ColumnEquals);
        options.IdentifierCase = CasingOption.Upper;

        var result = new SqlFormatterService(options).Format("SELECT t.c AS 'Split ship' FROM dbo.T AS t;");
        _output.WriteLine(result.FormattedSql);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Contains("'Split ship' = t.c", result.FormattedSql);
    }

    /// <summary>
    /// This is the only pass that writes to the head of an item, and a comment sitting there swallows what
    /// it writes: the alias lands on the comment's line and the expression it names starts on the line below.
    /// </summary>
    [Fact]
    public void ColumnEquals_DoesNotPutTheAliasBehindALineComment()
    {
        var result = FormatAndReparse(@"SELECT t.a AS First
     -- CHANGE 2: TRY_CAST guards the conversion
     , ISNULL(t.b, 0) AS 'Ongoing Qty'
FROM dbo.T AS t;", AliasStyleOption.ColumnEquals);

        Assert.Contains("-- CHANGE 2: TRY_CAST guards the conversion", result);
        Assert.DoesNotContain("= -- CHANGE 2", result);
        Assert.Contains("'Ongoing Qty' = ISNULL(t.b, 0)", result);
    }

    [Fact]
    public void NoAS_ReachesAHashPrefixedAliasToo()
    {
        var result = FormatAndReparse("SELECT SUM(t.a) AS #Ongoing FROM dbo.T AS t;", AliasStyleOption.NoAS);

        Assert.Contains("SUM(t.a) #Ongoing", result);
    }
}
