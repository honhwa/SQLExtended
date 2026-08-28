using SQLExtended.Comments;
using System.Linq;
using Xunit;

namespace SQLExtended.Tests.Comments;

public class CommentMarkScannerTests
{
    // --- the four tags ---

    [Theory]
    [InlineData("-- ! something is wrong", CommentMarkKind.Alert)]
    [InlineData("-- ? why a left join", CommentMarkKind.Query)]
    [InlineData("-- todo: index this", CommentMarkKind.Task)]
    [InlineData("-- * the interesting bit", CommentMarkKind.Highlight)]
    public void Scan_LineComment_TagsTheOpeningCharacter(string comment, CommentMarkKind expected)
    {
        var marks = CommentMarkScanner.Scan("SELECT 1\n" + comment);

        var mark = Assert.Single(marks);
        Assert.Equal(expected, mark.Kind);
    }

    [Theory]
    [InlineData("/* ! something is wrong */", CommentMarkKind.Alert)]
    [InlineData("/* ? why a left join */", CommentMarkKind.Query)]
    [InlineData("/* todo: index this */", CommentMarkKind.Task)]
    [InlineData("/* * the interesting bit */", CommentMarkKind.Highlight)]
    public void Scan_BlockComment_TagsTheSameWay(string comment, CommentMarkKind expected)
    {
        var marks = CommentMarkScanner.Scan(comment + "\nSELECT 1");

        var mark = Assert.Single(marks);
        Assert.Equal(expected, mark.Kind);
    }

    [Fact]
    public void Scan_PlainComment_IsLeftAlone()
    {
        var marks = CommentMarkScanner.Scan("-- just a note\n/* and another */\nSELECT 1");

        Assert.Empty(marks);
    }

    [Fact]
    public void Scan_TagWithNoSpaceAfterTheOpener_StillTags()
    {
        var marks = CommentMarkScanner.Scan("--!urgent");

        Assert.Equal(CommentMarkKind.Alert, Assert.Single(marks).Kind);
    }

    [Fact]
    public void Scan_Todo_IsCaseInsensitiveButAWholeWord()
    {
        Assert.Equal(CommentMarkKind.Task, Assert.Single(CommentMarkScanner.Scan("-- TODO drop this")).Kind);
        Assert.Equal(CommentMarkKind.Task, Assert.Single(CommentMarkScanner.Scan("-- ToDo drop this")).Kind);

        // 'todos' is a word that starts with todo, not the tag.
        Assert.Empty(CommentMarkScanner.Scan("-- todos are tracked elsewhere"));
    }

    // --- the span ---

    [Fact]
    public void Scan_ReportsTheWholeComment_FromItsOpener()
    {
        const string sql = "SELECT 1 -- ! careful";

        var mark = Assert.Single(CommentMarkScanner.Scan(sql));

        Assert.Equal("-- ! careful", sql.Substring(mark.Start, mark.Length));
    }

    [Fact]
    public void Scan_LineComment_StopsBeforeTheNewline()
    {
        // The token itself runs to the end of the line. Colouring the newline stretches the tag across the
        // rest of the line on screen, which is the one span mistake that looks like a rendering bug.
        const string sql = "-- ! careful\r\nSELECT 1";

        var mark = Assert.Single(CommentMarkScanner.Scan(sql));

        Assert.Equal("-- ! careful", sql.Substring(mark.Start, mark.Length));
    }

    [Fact]
    public void Scan_MultiLineBlockComment_CoversAllOfIt_CloserIncluded()
    {
        const string sql = "/* ! careful\n   this one bites\n*/\nSELECT 1";

        var mark = Assert.Single(CommentMarkScanner.Scan(sql));

        Assert.Equal("/* ! careful\n   this one bites\n*/", sql.Substring(mark.Start, mark.Length));
    }

    [Fact]
    public void Scan_ReturnsMarksInPositionOrder()
    {
        const string sql = "-- ! one\nSELECT 1\n/* ? two */\n-- todo three";

        var marks = CommentMarkScanner.Scan(sql);

        Assert.Equal([CommentMarkKind.Alert, CommentMarkKind.Query, CommentMarkKind.Task], marks.Select(m => m.Kind).ToArray());
        Assert.Equal(marks.Select(m => m.Start).OrderBy(s => s).ToArray(), marks.Select(m => m.Start).ToArray());
    }

    // --- dividers ---
    //
    // Asserted one form at a time: a script holding every divider shape passes while all but one of the
    // rules are broken, the lesson SqlIdentifierQuotingTests already paid for.

    [Fact]
    public void Scan_StarDividerLine_IsNotAHighlight()
    {
        Assert.Empty(CommentMarkScanner.Scan("-- ******************"));
    }

    [Fact]
    public void Scan_StarDividerBlock_IsNotAHighlight()
    {
        Assert.Empty(CommentMarkScanner.Scan("/*********************/"));
    }

    [Fact]
    public void Scan_BangDividerLine_IsNotAnAlert()
    {
        Assert.Empty(CommentMarkScanner.Scan("-- !!!!!!!!!!"));
    }

    [Fact]
    public void Scan_RepeatedTagWithSomethingToSay_StillTags()
    {
        Assert.Equal(CommentMarkKind.Alert, Assert.Single(CommentMarkScanner.Scan("-- !!! this one matters")).Kind);
    }

    [Fact]
    public void Scan_EmptyBlockComment_IsNotATag()
    {
        Assert.Empty(CommentMarkScanner.Scan("/**/ SELECT 1"));
    }

    // --- masking ---
    //
    // The whole reason this reads the token stream. Each case is asserted on its own, for the same reason
    // the dividers are: one combined script passes while three of the four rules are broken.

    [Fact]
    public void Scan_InsideAStringLiteral_IsNotAComment()
    {
        Assert.Empty(CommentMarkScanner.Scan("SELECT '-- ! not a comment' AS x"));
    }

    [Fact]
    public void Scan_InsideABracketedIdentifier_IsNotAComment()
    {
        Assert.Empty(CommentMarkScanner.Scan("SELECT 1 AS [-- ! not a comment]"));
    }

    [Fact]
    public void Scan_InsideAQuotedIdentifier_IsNotAComment()
    {
        // initialQuotedIdentifiers: true, matching the formatter and the rainbow scanner.
        Assert.Empty(CommentMarkScanner.Scan("SELECT 1 AS \"-- ! not a comment\""));
    }

    [Fact]
    public void Scan_NestedInsideABlockComment_TagsOnlyTheOuterComment()
    {
        // The inner -- is not its own token, so the outer comment's plain opener is what decides.
        Assert.Empty(CommentMarkScanner.Scan("/* -- ! not a tag */"));
    }

    // --- half-typed scripts ---

    [Fact]
    public void Scan_UnterminatedBlockComment_DoesNotTagUntilItIsClosed()
    {
        // Verified against the lexer, not assumed: an unterminated /* produces NO token at all. It is not
        // returned as a half-read MultilineComment — GetTokenStream drops it and reports error 46032
        // through the out-parameter instead. So a block comment being typed stays uncoloured until its */
        // arrives, and there is nothing this scanner can do about it while it reads tokens.
        Assert.Empty(CommentMarkScanner.Scan("SELECT 1\n/* ! still typing"));

        Assert.Equal(CommentMarkKind.Alert, Assert.Single(CommentMarkScanner.Scan("SELECT 1\n/* ! still typing */")).Kind);
    }

    [Fact]
    public void Scan_UnterminatedBlockComment_DoesNotLoseTheCommentsBeforeIt()
    {
        // The dropped comment takes the rest of the stream with it, so what matters is that everything
        // ahead of it still tags — the damage is local to the comment being typed, not the whole window.
        var marks = CommentMarkScanner.Scan("-- ! one\nSELECT 1\n/* ? still typing");

        Assert.Equal(CommentMarkKind.Alert, Assert.Single(marks).Kind);
    }

    [Fact]
    public void Scan_UnterminatedLineComment_AtEndOfFile_StillTags()
    {
        // The single-line form has no such problem: it is closed by the end of the file as well as by a
        // newline, so it tags while it is being typed.
        Assert.Equal(CommentMarkKind.Alert, Assert.Single(CommentMarkScanner.Scan("SELECT 1\n-- ! still typing")).Kind);
    }

    [Fact]
    public void Scan_UnterminatedStringBeforeAComment_DoesNotThrow()
    {
        var marks = CommentMarkScanner.Scan("SELECT 'oops\n-- ! after the break");

        Assert.NotNull(marks);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("SELECT 1 FROM t")]
    [InlineData("--")]
    public void Scan_NothingToFind_ReturnsEmpty(string sql)
    {
        Assert.Empty(CommentMarkScanner.Scan(sql));
    }
}
