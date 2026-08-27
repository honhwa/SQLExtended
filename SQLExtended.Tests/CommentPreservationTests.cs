using SQLExtended.Formatting;
using Xunit;
using Xunit.Abstractions;

namespace SQLExtended.Tests;

/// <summary>
/// The formatter must not edit comment prose. The semicolon passes used to, because they were
/// line-based and didn't check whether there was any code in front of the "--": a header line
/// reading "-- Example  EXEC dbo.Foo @Id = 1;" came back as "; -- Example  EXEC dbo.Foo @Id = 1",
/// which both wrecked the header and injected a stray statement terminator.
/// </summary>
public class CommentPreservationTests
{
    private readonly ITestOutputHelper _output;

    public CommentPreservationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private string Format(string sql, FormatterOptions options)
    {
        var result = new SqlFormatterService(options).Format(sql);
        _output.WriteLine(result.FormattedSql);
        Assert.True(result.Success, result.ErrorMessage);
        return result.FormattedSql;
    }

    /// <summary>The proc header snippet, which is the shape this has to keep working for.</summary>
    private const string ProcHeader =
        "-- =================================================================================================\r\n" +
        "--  Procedure    dbo.usp_GetOrderShipmentHistory\r\n" +
        "--  Purpose      Shipment history for one order, most recent first.\r\n" +
        "-- -------------------------------------------------------------------------------------------------\r\n" +
        "--  Parameters   @OrderNbr  Customer order number. Required.\r\n" +
        "--  Returns      One row per shipment, ordered by ShipDate DESC.\r\n" +
        "--  Example      EXEC dbo.usp_GetOrderShipmentHistory @OrderNbr = 12345;\r\n" +
        "-- =================================================================================================\r\n";

    private const string Body =
        "CREATE PROCEDURE dbo.usp_GetOrderShipmentHistory @OrderNbr INT\r\n" +
        "AS\r\n" +
        "BEGIN\r\n" +
        "    SELECT m.Ordernbr FROM dbo.main AS m WHERE m.Ordernbr = @OrderNbr;\r\n" +
        "END\r\n";

    [Theory]
    [InlineData(SemicolonOption.Unchanged)]
    [InlineData(SemicolonOption.Always)]
    [InlineData(SemicolonOption.Never)]
    public void WholeLineCommentEndingInSemicolonIsLeftAlone(SemicolonOption semicolons)
    {
        var options = new FormatterOptions { TrailingSemicolon = semicolons };

        var result = Format(ProcHeader + Body, options);

        Assert.Contains("--  Example      EXEC dbo.usp_GetOrderShipmentHistory @OrderNbr = 12345;", result);
        Assert.DoesNotContain("; --", result);
    }

    [Theory]
    [InlineData(IndentStyleOption.Tabs)]
    [InlineData(IndentStyleOption.Spaces)]
    public void ProcHeaderSurvivesFormattingUnchanged(IndentStyleOption indent)
    {
        // Every header line starts at column 0, so ApplyIndentStyle has no leading whitespace to
        // rewrite and the alignment inside the comment is preserved whichever indent style is set.
        var options = new FormatterOptions { IndentStyle = indent };

        var result = Format(ProcHeader + Body, options);

        foreach (var line in ProcHeader.Split(new[] { "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries))
            Assert.Contains(line, result);
    }

    [Fact]
    public void TrailingCommentAfterCodeStillGetsItsSemicolonMovedBack()
    {
        // The behaviour the whole-line guard must not have broken: ScriptDom parks the terminator
        // after an inline comment, and it belongs in front of it.
        var options = new FormatterOptions { TrailingSemicolon = SemicolonOption.Always };

        var result = Format("SELECT 1 -- trailing note\r\n", options);

        Assert.Contains("; -- trailing note", result);
    }

    /// <summary>
    /// A comment belongs on the line the author put it on. ScriptDom moves them **both ways** — it splits a
    /// comment that trailed a column definition onto its own line, and it pulls a comment that was on its
    /// own line up onto the end of the line above — and the generated text it produces for the two arrangements
    /// is identical, so only the source can say which is which. These pin both directions; the pair
    /// <see cref="TrailingCommentInAColumnListIsRejoined"/> /
    /// <see cref="OwnLineCommentInAColumnListStaysOnItsOwnLine"/> is the point, because ScriptDom's output
    /// for those two inputs is the same byte for byte.
    /// </summary>
    private const string ColumnList =
        "CREATE TABLE dbo.T\r\n" +
        "(\r\n" +
        "    Name NVARCHAR(500) NULL,{0}\r\n" +
        "    DOB SMALLDATETIME NULL,{1}\r\n" +
        "    Id INT NOT NULL\r\n" +
        ");\r\n";

    [Fact]
    public void TrailingCommentInAColumnListIsRejoined()
    {
        var result = Format(string.Format(ColumnList, "   -- from main", "   -- from main"), new FormatterOptions());

        // ScriptDom split both onto their own line; the source had them trailing, so both go back. The code
        // in front of the comment varies with the comma and casing options, so what is asserted is that
        // there *is* code in front of it — which is the whole of "trailing".
        Assert.Equal(2, CountLinesWhere(result, IsTrailingFromMain));
        Assert.Equal(0, CountLinesWhere(result, line => line.Trim() == "-- from main"));
    }

    private static bool IsTrailingFromMain(string line)
    {
        int at = line.IndexOf("-- from main", System.StringComparison.Ordinal);
        return at > 0 && line.Substring(0, at).Trim().Length > 0;
    }

    [Fact]
    public void OwnLineCommentInAColumnListStaysOnItsOwnLine()
    {
        // Same generated text as the case above — the only difference is where the source had the comments.
        string sql =
            "CREATE TABLE dbo.T\r\n" +
            "(\r\n" +
            "    -- from main\r\n" +
            "    Name NVARCHAR(500) NULL,\r\n" +
            "    -- from main\r\n" +
            "    DOB SMALLDATETIME NULL,\r\n" +
            "    Id INT NOT NULL\r\n" +
            ");\r\n";

        var result = Format(sql, new FormatterOptions());

        Assert.DoesNotContain("NULL -- from main", result);
        Assert.Equal(2, CountLinesWhere(result, line => line.Trim() == "-- from main"));
    }

    [Fact]
    public void ABlockOfOwnLineCommentsStaysOneCommentPerLine()
    {
        // The reported shape: notes documenting removed joins, which came back concatenated onto the end of
        // the FROM line — where they can no longer be un-commented one at a time. ScriptDom pulls the first
        // of them up onto the FROM line; the other two it leaves alone and the old pass glued them on too.
        string sql =
            "SELECT a.x\r\n" +
            "FROM dbo.A a\r\n" +
            "-- REMOVED: LEFT JOIN one\r\n" +
            "-- REMOVED: LEFT JOIN two\r\n" +
            "-- REMOVED: LEFT JOIN three\r\n" +
            "INNER JOIN dbo.B b ON a.id = b.id;\r\n";

        var result = Format(sql, new FormatterOptions { JoinOnSameLine = true, AlignFromAndJoins = true });

        Assert.Equal(3, CountLinesWhere(result, line => line.TrimStart().StartsWith("-- REMOVED:")));
        Assert.DoesNotContain("AS a -- REMOVED", result);
    }

    [Fact]
    public void ACommentTrailingCodeStaysTrailingEvenWhenItsLineIsMergedAway()
    {
        // The ON line's comment is genuinely trailing, and JoinOnSameLine folds that whole line into the
        // JOIN above it. The comment has to ride along rather than being pushed onto a line of its own.
        string sql =
            "SELECT a.x\r\n" +
            "FROM dbo.A a\r\n" +
            "INNER JOIN dbo.B b\r\n" +
            "    ON a.id = b.id   -- existence filter\r\n" +
            "WHERE a.x = 1;\r\n";

        var result = Format(sql, new FormatterOptions { JoinOnSameLine = true });

        AssertNothingCommentedOut(result);
        Assert.Contains("ON a.id = b.id -- existence filter", result);
    }

    [Fact]
    public void CollectTrailingCommentsCountsOnlyCommentsWithCodeInFrontOfThem()
    {
        string sql =
            "SELECT 1,   -- trailing once\r\n" +
            "       2,   -- trailing twice\r\n" +
            "       -- own line\r\n" +
            "       3,   -- trailing once\r\n" +
            "       4;\r\n";

        var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql170Parser(true);
        System.Collections.Generic.IList<Microsoft.SqlServer.TransactSql.ScriptDom.ParseError> errors;
        Microsoft.SqlServer.TransactSql.ScriptDom.TSqlFragment fragment;
        using (var reader = new System.IO.StringReader(sql))
            fragment = parser.Parse(reader, out errors);
        Assert.Empty(errors);

        var trailing = SqlFormatterService.CollectTrailingComments(fragment.ScriptTokenStream);

        // Counted, not collected into a set: the repeated text is rejoined as many times as it was trailing.
        Assert.Equal(2, trailing["-- trailing once"]);
        Assert.Equal(1, trailing["-- trailing twice"]);
        Assert.False(trailing.ContainsKey("-- own line"));
    }

    private static int CountLinesWhere(string text, System.Func<string, bool> predicate)
    {
        int count = 0;
        foreach (var line in text.Split(new[] { "\r\n" }, System.StringSplitOptions.None))
            if (predicate(line)) count++;
        return count;
    }

    /// <summary>
    /// The other half of the same hazard, and the one that turns working SQL into SQL that no longer
    /// parses: a pass that <em>appends</em> to a line whose tail is a "--" comment buries whatever it
    /// appended inside that comment. Both cases below were reported as "the comments end up commenting
    /// out required code", and neither announces itself — the parse error surfaces lines away from the
    /// comment, and where the result still parses it silently means something else.
    ///
    /// Every case is asserted by <b>re-parsing the formatted output</b>. Assertions on the text alone
    /// pass just as happily when a clause has been commented out, because the clause is still there.
    /// </summary>
    private static void AssertNothingCommentedOut(string formatted)
    {
        var parser = new Microsoft.SqlServer.TransactSql.ScriptDom.TSql170Parser(true);
        System.Collections.Generic.IList<Microsoft.SqlServer.TransactSql.ScriptDom.ParseError> errors;
        using (var reader = new System.IO.StringReader(formatted))
            parser.Parse(reader, out errors);

        Assert.True(errors.Count == 0,
            errors.Count == 0 ? "" : $"formatted output no longer parses: line {errors[0].Line}: {errors[0].Message}\r\n{formatted}");
    }

    [Fact]
    public void CommentAtTheEndOfAJoinLineDoesNotSwallowTheOnClause()
    {
        // JoinOnSameLine merges the ON onto the JOIN line it is waiting for. With a comment already
        // sitting at the end of that line the whole ON clause lands inside it, and the join becomes a
        // cross join — or, as here, the next clause keyword makes it a parse error instead.
        var options = new FormatterOptions { JoinOnSameLine = true };

        var result = Format(
            "SELECT a.x\r\n" +
            "FROM dbo.A a\r\n" +
            "INNER JOIN dbo.B b   -- existence filter only\r\n" +
            "    ON a.id = b.id\r\n" +
            "WHERE a.x = 1;\r\n", options);

        AssertNothingCommentedOut(result);
        Assert.Contains("ON a.id = b.id", result);
    }

    [Fact]
    public void CommentBetweenTwoCtesDoesNotSwallowTheSecondCteHeader()
    {
        // ScriptDom leaves the comment trailing the ")," that separates two CTEs
        // ("FROM dbo.A AS a), -- note"). The stacked-CTE pass builds each header by joining lines, so
        // that comment became the CTE's name and the header was emitted as ", -- note Second AS (" —
        // the entire second CTE commented out.
        var options = new FormatterOptions { CteStackedLayout = true };

        var result = Format(
            "WITH First AS (\r\n" +
            "    SELECT a.Id FROM dbo.A a   -- trailing note\r\n" +
            "), Second AS (\r\n" +
            "    SELECT b.Id FROM dbo.B b\r\n" +
            ")\r\n" +
            "SELECT f.Id FROM First f LEFT JOIN Second s ON f.Id = s.Id;\r\n", options);

        AssertNothingCommentedOut(result);
        Assert.Contains("Second AS (", result);
        Assert.Contains("-- trailing note", result);

        // The comment is lifted onto its own line rather than dropped — losing it would also "fix" the
        // parse error, and silently discarding a comment is not a fix.
        foreach (var line in result.Split(new[] { "\r\n" }, System.StringSplitOptions.None))
        {
            int comment = line.IndexOf("--", System.StringComparison.Ordinal);
            if (comment < 0) continue;
            Assert.DoesNotContain("AS (", line.Substring(comment));
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CommentOnEveryLineOfACteScriptNeverCommentsOutCode(bool cteStacked, bool derivedStacked)
    {
        // The positions that break are not the ones anybody would think to write a case for, so this
        // walks a comment down every line of the script instead.
        const string Sql =
            "WITH First AS (\r\n" +
            "    SELECT a.Id FROM dbo.A a\r\n" +
            "), Second AS (\r\n" +
            "    SELECT b.Id FROM dbo.B b\r\n" +
            ")\r\n" +
            "SELECT f.Id\r\n" +
            "FROM First f\r\n" +
            "LEFT JOIN Second s\r\n" +
            "    ON f.Id = s.Id;";

        var options = new FormatterOptions { CteStackedLayout = cteStacked, DerivedTableStackedLayout = derivedStacked, JoinOnSameLine = true };
        var lines = Sql.Split(new[] { "\r\n" }, System.StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            var mutated = (string[])lines.Clone();
            mutated[i] = lines[i] + "   -- note " + i;

            var result = new SqlFormatterService(options).Format(string.Join("\r\n", mutated));
            Assert.True(result.Success, result.ErrorMessage);

            _output.WriteLine($"--- comment on line {i + 1} ---");
            _output.WriteLine(result.FormattedSql);
            AssertNothingCommentedOut(result.FormattedSql);
        }
    }
}
