using SQLExtended.Rainbow;
using System.Linq;
using Xunit;

namespace SQLExtended.Tests.Rainbow;

public class RainbowPairScannerTests
{
    // --- depth ---

    [Fact]
    public void Scan_NestedDerivedTables_AssignsDepthsOutsideIn()
    {
        const string sql = "SELECT * FROM (SELECT * FROM (SELECT 1 AS x) a) b";

        var pairs = RainbowPairScanner.Scan(sql);

        Assert.Equal(4, pairs.Count);
        Assert.Equal([0, 1, 1, 0], pairs.Select(p => p.Depth).ToArray());
        Assert.Equal([true, true, false, false], pairs.Select(p => p.IsOpen).ToArray());
        Assert.All(pairs, p => Assert.True(p.IsMatched));
    }

    [Fact]
    public void Scan_SiblingCalls_BothSitAtDepthZero()
    {
        const string sql = "SELECT LEN('a'), ISNULL(b, 0) FROM t";

        var pairs = RainbowPairScanner.Scan(sql);

        Assert.Equal(4, pairs.Count);
        Assert.All(pairs, p => Assert.Equal(0, p.Depth));
    }

    [Fact]
    public void Scan_ReportsOffsetOfTheActualCharacter()
    {
        const string sql = "SELECT * FROM (SELECT 1) a";

        var pairs = RainbowPairScanner.Scan(sql);

        Assert.All(pairs, p => Assert.Equal(1, p.Length));
        Assert.Equal('(', sql[pairs[0].Start]);
        Assert.Equal(')', sql[pairs[1].Start]);
    }

    [Fact]
    public void Scan_ReturnsPairsInPositionOrder()
    {
        const string sql = "SELECT ((1)), (2), (((3)))";

        var pairs = RainbowPairScanner.Scan(sql);

        Assert.Equal(pairs.Select(p => p.Start).OrderBy(s => s).ToArray(), pairs.Select(p => p.Start).ToArray());
    }

    // --- the cases a character-scanning implementation gets wrong ---
    //
    // Each of these contains a parenthesis that is not a parenthesis. They are the reason this
    // reads the ScriptDom token stream rather than the text, so they are asserted individually
    // (a single combined script would pass while three of the four rules were broken).

    [Fact]
    public void Scan_ParenInsideStringLiteral_IsNotAParen()
    {
        Assert.Empty(RainbowPairScanner.Scan("SELECT 'a ( b' AS x"));
        Assert.Empty(RainbowPairScanner.Scan("SELECT N'a ) b' AS x"));
    }

    [Fact]
    public void Scan_ParenInsideSingleLineComment_IsNotAParen()
    {
        Assert.Empty(RainbowPairScanner.Scan("SELECT 1 -- ( unclosed forever\r\nSELECT 2"));
    }

    [Fact]
    public void Scan_ParenInsideBlockComment_IsNotAParen()
    {
        Assert.Empty(RainbowPairScanner.Scan("SELECT /* ( */ 1"));
    }

    [Fact]
    public void Scan_ParenInsideBracketedIdentifier_IsNotAParen()
    {
        Assert.Empty(RainbowPairScanner.Scan("SELECT [col (x)] FROM t"));
    }

    [Fact]
    public void Scan_ParenInsideQuotedIdentifier_IsNotAParen()
    {
        Assert.Empty(RainbowPairScanner.Scan("SELECT \"col (x)\" FROM t"));
    }

    [Fact]
    public void Scan_RealParensBesideMaskedOnes_AreStillFound()
    {
        // The masking rules must not swallow the surrounding script.
        const string sql = "SELECT ISNULL([col (x)], 'a ( b') FROM t -- (";

        var pairs = RainbowPairScanner.Scan(sql);

        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.True(p.IsMatched));
        Assert.Equal('(', sql[pairs[0].Start]);
        Assert.Equal(')', sql[pairs[1].Start]);
    }

    // --- mid-typing states ---

    [Fact]
    public void Scan_UnclosedParen_IsReportedUnmatched()
    {
        var pairs = RainbowPairScanner.Scan("SELECT * FROM (SELECT 1");

        var pair = Assert.Single(pairs);
        Assert.True(pair.IsOpen);
        Assert.False(pair.IsMatched);
    }

    [Fact]
    public void Scan_StrayCloseParen_IsReportedUnmatched()
    {
        var pairs = RainbowPairScanner.Scan("SELECT 1)");

        var pair = Assert.Single(pairs);
        Assert.False(pair.IsOpen);
        Assert.False(pair.IsMatched);
    }

    [Fact]
    public void Scan_MatchedPairsAroundAnUnmatchedOne_KeepTheirOwnState()
    {
        // '(' at depth 0 never closes; the inner pair closes normally.
        var pairs = RainbowPairScanner.Scan("SELECT * FROM (SELECT ISNULL(a, 0) FROM t");

        Assert.Equal(3, pairs.Count);
        Assert.False(pairs[0].IsMatched);
        Assert.True(pairs[1].IsMatched);
        Assert.True(pairs[2].IsMatched);
        Assert.Equal(1, pairs[1].Depth);
    }

    [Fact]
    public void Scan_UnterminatedStringLiteral_DoesNotThrowAndMatchesNothing()
    {
        // The lexer reports an error and returns what it read. Whatever it decides the trailing
        // text is, nothing in it may come back as a matched pair.
        var pairs = RainbowPairScanner.Scan("SELECT 'abc ( def");

        Assert.DoesNotContain(pairs, p => p.IsMatched);
    }

    [Fact]
    public void Scan_UnterminatedBlockComment_DoesNotThrowAndMatchesNothing()
    {
        var pairs = RainbowPairScanner.Scan("SELECT 1 /* ( unclosed");

        Assert.DoesNotContain(pairs, p => p.IsMatched);
    }

    [Fact]
    public void Scan_DepthDoesNotResetAtBatchSeparator()
    {
        // A batch that leaves a paren open is already unmatched, so GO needs no special rule.
        var pairs = RainbowPairScanner.Scan("SELECT * FROM (SELECT 1\r\nGO\r\nSELECT 2");

        var pair = Assert.Single(pairs);
        Assert.False(pair.IsMatched);
    }

    // --- input guards ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SELECT 1")]
    public void Scan_NoParens_ReturnsEmpty(string sql)
    {
        Assert.Empty(RainbowPairScanner.Scan(sql));
    }

    // --- palette cycling ---

    [Fact]
    public void ColorIndex_CyclesOnceDepthPassesThePalette()
    {
        var indexes = Enumerable.Range(0, 9).Select(d => RainbowPairScanner.ColorIndex(d, 4)).ToArray();

        Assert.Equal([0, 1, 2, 3, 0, 1, 2, 3, 0], indexes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ColorIndex_NonPositiveLevelCount_CollapsesToOneColour(int levels)
    {
        Assert.Equal(0, RainbowPairScanner.ColorIndex(0, levels));
        Assert.Equal(0, RainbowPairScanner.ColorIndex(5, levels));
    }

    [Fact]
    public void ColorIndex_LevelCountBeyondThePalette_ClampsToWhatExists()
    {
        Assert.Equal(RainbowPairScanner.MaxSupportedLevels - 1, RainbowPairScanner.ColorIndex(RainbowPairScanner.MaxSupportedLevels - 1, 99));
        Assert.Equal(0, RainbowPairScanner.ColorIndex(RainbowPairScanner.MaxSupportedLevels, 99));
    }

    [Fact]
    public void ColorIndex_NeverReturnsAnIndexOutsideThePalette()
    {
        for (int depth = 0; depth < 50; depth++)
            for (int levels = 1; levels <= RainbowPairScanner.MaxSupportedLevels; levels++)
            {
                int index = RainbowPairScanner.ColorIndex(depth, levels);
                Assert.InRange(index, 0, levels - 1);
            }
    }

    // --- blocks (opt-in) ---

    [Fact]
    public void Scan_BlocksOff_LeavesBeginEndAlone()
    {
        Assert.Empty(RainbowPairScanner.Scan("BEGIN SELECT 1 END"));
    }

    [Fact]
    public void Scan_NestedBeginEnd_AssignsDepthsOutsideIn()
    {
        const string sql = "BEGIN SELECT 1 BEGIN SELECT 2 END END";

        var pairs = RainbowPairScanner.Scan(sql, includeBlocks: true);

        Assert.Equal(4, pairs.Count);
        Assert.All(pairs, p => Assert.Equal(RainbowKind.Block, p.Kind));
        Assert.All(pairs, p => Assert.True(p.IsMatched));
        Assert.Equal([0, 1, 1, 0], pairs.Select(p => p.Depth).ToArray());
    }

    [Fact]
    public void Scan_CaseExpression_PairsWithItsEnd()
    {
        const string sql = "SELECT CASE WHEN 1 = 1 THEN 'a' ELSE 'b' END AS x";

        var pairs = RainbowPairScanner.Scan(sql, includeBlocks: true);

        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.True(p.IsMatched));
        Assert.Equal("CASE", sql.Substring(pairs[0].Start, pairs[0].Length));
        Assert.Equal("END", sql.Substring(pairs[1].Start, pairs[1].Length));
    }

    [Fact]
    public void Scan_TryCatch_PairsEachHalfSeparately()
    {
        const string sql = "BEGIN TRY SELECT 1 END TRY BEGIN CATCH SELECT 2 END CATCH";

        var pairs = RainbowPairScanner.Scan(sql, includeBlocks: true);

        Assert.Equal(4, pairs.Count);
        Assert.All(pairs, p => Assert.True(p.IsMatched));

        // Only the BEGIN/END keyword is coloured, never the TRY or CATCH word beside it.
        Assert.All(pairs, p => Assert.Contains(sql.Substring(p.Start, p.Length), new[] { "BEGIN", "END" }));
    }

    [Fact]
    public void Scan_TryContainingCatchKeywordOrder_DoesNotCrossPair()
    {
        // The CATCH block opens after the TRY block has closed, so both sit at depth 0.
        const string sql = "BEGIN TRY SELECT 1 END TRY BEGIN CATCH SELECT 2 END CATCH";

        var pairs = RainbowPairScanner.Scan(sql, includeBlocks: true);

        Assert.All(pairs, p => Assert.Equal(0, p.Depth));
    }

    [Fact]
    public void Scan_TryWrappingABeginBlock_NestsIt()
    {
        const string sql = "BEGIN TRY BEGIN SELECT 1 END END TRY BEGIN CATCH SELECT 2 END CATCH";

        var pairs = RainbowPairScanner.Scan(sql, includeBlocks: true);

        var opens = pairs.Where(p => p.IsOpen).Select(p => p.Depth).ToArray();
        Assert.Equal([0, 1, 0], opens);
        Assert.All(pairs, p => Assert.True(p.IsMatched));
    }

    [Theory]
    [InlineData("BEGIN TRAN SELECT 1 COMMIT")]
    [InlineData("BEGIN TRANSACTION SELECT 1 ROLLBACK")]
    [InlineData("BEGIN DISTRIBUTED TRANSACTION SELECT 1 COMMIT")]
    public void Scan_BeginTransaction_IsNotABlock(string sql)
    {
        // The killer case: pushing this would swallow the next real END and shift the colour of
        // every block after it. COMMIT/ROLLBACK close a transaction, never END.
        Assert.Empty(RainbowPairScanner.Scan(sql, includeBlocks: true));
    }

    [Fact]
    public void Scan_TransactionInsideABlock_LeavesTheBlockPairingIntact()
    {
        const string sql = "BEGIN BEGIN TRAN SELECT 1 COMMIT END";

        var pairs = RainbowPairScanner.Scan(sql, includeBlocks: true);

        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.True(p.IsMatched));
        Assert.Equal(0, pairs[0].Depth);
    }

    [Fact]
    public void Scan_BeginAtomic_IsABlock()
    {
        // Natively compiled procedures: ATOMIC does end with END, unlike TRAN.
        const string sql = "BEGIN ATOMIC WITH (TRANSACTION ISOLATION LEVEL = SNAPSHOT, LANGUAGE = N'us_english') SELECT 1 END";

        var pairs = RainbowPairScanner.Scan(sql, includeBlocks: true);

        var blocks = pairs.Where(p => p.Kind == RainbowKind.Block).ToArray();
        Assert.Equal(2, blocks.Length);
        Assert.All(blocks, p => Assert.True(p.IsMatched));
    }

    [Fact]
    public void Scan_ServiceBrokerConversationStatements_AreNotBlocks()
    {
        Assert.Empty(RainbowPairScanner.Scan("BEGIN DIALOG @h FROM SERVICE a TO SERVICE 'b'", includeBlocks: true));
        Assert.Empty(RainbowPairScanner.Scan("END CONVERSATION @h", includeBlocks: true));
    }

    [Fact]
    public void Scan_BeginKeywordInsideACommentOrString_IsNotABlock()
    {
        Assert.Empty(RainbowPairScanner.Scan("SELECT 'BEGIN' -- END", includeBlocks: true));
        Assert.Empty(RainbowPairScanner.Scan("SELECT [BEGIN] FROM [END]", includeBlocks: true));
    }

    [Fact]
    public void Scan_CommentBetweenBeginAndTry_StillReadsAsBeginTry()
    {
        const string sql = "BEGIN /* here we go */ TRY SELECT 1 END TRY BEGIN CATCH SELECT 2 END CATCH";

        var pairs = RainbowPairScanner.Scan(sql, includeBlocks: true);

        Assert.Equal(4, pairs.Count);
        Assert.All(pairs, p => Assert.True(p.IsMatched));
    }

    [Fact]
    public void Scan_UnclosedBegin_IsReportedUnmatched()
    {
        var pairs = RainbowPairScanner.Scan("BEGIN SELECT 1", includeBlocks: true);

        var pair = Assert.Single(pairs);
        Assert.Equal(RainbowKind.Block, pair.Kind);
        Assert.False(pair.IsMatched);
    }

    [Fact]
    public void Scan_StrayEnd_IsReportedUnmatched()
    {
        var pairs = RainbowPairScanner.Scan("SELECT 1 END", includeBlocks: true);

        var pair = Assert.Single(pairs);
        Assert.False(pair.IsMatched);
        Assert.False(pair.IsOpen);
    }

    [Fact]
    public void Scan_EndTryClosingOverAnUnclosedBegin_LeavesTheBeginUnmatched()
    {
        // The inner BEGIN never closes; END TRY unwinds past it rather than mis-pairing with it.
        const string sql = "BEGIN TRY BEGIN SELECT 1 END TRY BEGIN CATCH SELECT 2 END CATCH";

        var pairs = RainbowPairScanner.Scan(sql, includeBlocks: true);

        var inner = pairs.First(p => p.IsOpen && p.Depth == 1);
        Assert.False(inner.IsMatched);
        Assert.True(pairs[0].IsMatched); // BEGIN TRY still pairs with its END TRY
    }

    // --- the two passes together ---

    [Fact]
    public void Scan_ParensAndBlocks_CountDepthSeparately()
    {
        // The CASE is inside two parentheses but is the outermost block, so it stays at block depth 0.
        const string sql = "SELECT * FROM (SELECT (CASE WHEN 1 = 1 THEN 2 END) AS x) y";

        var pairs = RainbowPairScanner.Scan(sql, includeBlocks: true);

        var block = pairs.Single(p => p.Kind == RainbowKind.Block && p.IsOpen);
        Assert.Equal(0, block.Depth);

        var parenDepths = pairs.Where(p => p.Kind == RainbowKind.Parenthesis && p.IsOpen).Select(p => p.Depth).ToArray();
        Assert.Equal([0, 1], parenDepths);
    }

    [Fact]
    public void Scan_MixedPass_StillReturnsPositionOrder()
    {
        const string sql = "BEGIN SELECT ISNULL(CASE WHEN 1 = 1 THEN 2 END, 0) END";

        var pairs = RainbowPairScanner.Scan(sql, includeBlocks: true);

        Assert.Equal(pairs.Select(p => p.Start).OrderBy(s => s).ToArray(), pairs.Select(p => p.Start).ToArray());
        Assert.All(pairs, p => Assert.True(p.IsMatched));
    }
}
