using SQLExtended.Comments;
using System.Linq;
using Xunit;

namespace SQLExtended.Tests.Comments;

/// <summary>
/// The banner header pass: which line is chrome, which is a heading, and how a change row is split into
/// columns. Kept apart from <see cref="CommentMarkScannerTests"/>, which covers the comment tags.
/// </summary>
public class CommentBannerTests
{
    /// <summary>The house-style header block, as it actually appears at the top of a procedure.</summary>
    private const string Banner =
        "/********************************************************************************************************\n" +
        "** Description : This is used by the windows service to generate the json for the Elastic Index process.\n" +
        "**\n" +
        "*********************************************************************************************************\n" +
        "** Change History\n" +
        "*********************************************************************************************************\n" +
        "** Date         Author            Ticket        Description\n" +
        "** -----------  -------------    ---------    ---------------------------------------------------------\n" +
        "** 11-Jun-24    AT                NA            Changed UPC Type to exclude INTERLEAVED2OF5 types\n" +
        "** 05-Dec-25    DB                NA            StoreApps changes for IM Project\n" +
        "** 03-Mar-26    DB                            Performance tuning\n" +
        "**\n" +
        "*********************************************************************************************************/\n" +
        "SELECT 1";

    private static string[] TextOf(string sql, CommentMarkKind kind) =>
        CommentMarkScanner.Scan(sql).Where(m => m.Kind == kind).Select(m => sql.Substring(m.Start, m.Length)).ToArray();

    // --- chrome ---

    [Fact]
    public void Banner_IsNotOneFlatHighlight()
    {
        // What this got wrong first: the banner opens with a rule of stars, the `*` tag matched it, and all
        // fifteen lines came back as a single Highlight mark 961 characters long.
        var marks = CommentMarkScanner.Scan(Banner);

        Assert.DoesNotContain(marks, m => m.Kind == CommentMarkKind.Highlight);
        Assert.All(marks, m => Assert.True(m.Length < 120, $"a banner mark should be at most one line, got {m.Length}"));
    }

    [Fact]
    public void Rules_AreTheAllStarLines_DelimitersIncluded()
    {
        var rules = TextOf(Banner, CommentMarkKind.BannerRule);

        Assert.Equal(4, rules.Length);

        // The /* of the opening rule and the */ of the closing one are what make those lines read as the
        // top and bottom of the box, so they are inside the span rather than left bright.
        Assert.StartsWith("/*****", rules[0]);
        Assert.EndsWith("*****/", rules[rules.Length - 1]);
    }

    [Fact]
    public void Prefixes_AreSplitFromTheirLine()
    {
        // The whole point of the prefix role: `**` looks like a rule but sits on a text line, so it has to
        // be able to recede without taking the text beside it along.
        var prefixes = TextOf(Banner, CommentMarkKind.BannerPrefix);

        Assert.All(prefixes, p => Assert.Equal("**", p));

        // Seven content lines plus the two bare ** spacers.
        Assert.Equal(9, prefixes.Length);
    }

    [Fact]
    public void Dashes_AreTheirOwnRole_NotARule()
    {
        var dashes = Assert.Single(TextOf(Banner, CommentMarkKind.BannerDashes));

        Assert.StartsWith("-----------", dashes);
        Assert.DoesNotContain("*", dashes);
    }

    [Fact]
    public void BareSpacerLine_IsPrefixOnly()
    {
        var marks = CommentMarkScanner.Scan("/*********\n**\n*********/");

        Assert.Equal([CommentMarkKind.BannerRule, CommentMarkKind.BannerPrefix, CommentMarkKind.BannerRule], marks.Select(m => m.Kind).ToArray());
    }

    // --- headings ---

    [Fact]
    public void Label_IsTheWordAndItsColon_NeverTheTextAfter()
    {
        Assert.Equal(["Description"], TextOf(Banner, CommentMarkKind.BannerLabel));
        Assert.Equal([":"], TextOf(Banner, CommentMarkKind.BannerPunctuation));
        Assert.Equal(["This is used by the windows service to generate the json for the Elastic Index process."], TextOf(Banner, CommentMarkKind.BannerProse));
    }

    [Fact]
    public void Section_IsAWholeBareHeadingLine()
    {
        Assert.Equal(["Change History"], TextOf(Banner, CommentMarkKind.BannerSection));
    }

    [Fact]
    public void ColumnHeader_IsRecognisedByTheDashesBeneathIt()
    {
        // Not by anything about the row itself — its words are whatever the house style calls the columns.
        Assert.Equal(["Date         Author            Ticket        Description"], TextOf(Banner, CommentMarkKind.BannerColumnHeader));
    }

    [Fact]
    public void ColumnHeader_WithoutDashesBeneath_IsNotOne()
    {
        // The dashes row is the whole signal. Take it away and the same line is just prose.
        const string sql = "/*********\n** Date   Author   Ticket\n*********/";

        Assert.Empty(TextOf(sql, CommentMarkKind.BannerColumnHeader));
        Assert.Equal(["Date   Author   Ticket"], TextOf(sql, CommentMarkKind.BannerProse));
    }

    [Fact]
    public void Section_IsToldFromATableRowByColumnSpacing()
    {
        // The one discriminator that holds without knowing the house style. Being between two rules does
        // not work — the column header row is between two rules as well.
        Assert.Equal(["Change History"], TextOf(Banner, CommentMarkKind.BannerSection));
        Assert.DoesNotContain("Date", string.Join("|", TextOf(Banner, CommentMarkKind.BannerSection)));
    }

    // --- change rows ---

    [Fact]
    public void ChangeRow_SplitsIntoFourColumns()
    {
        Assert.Equal(["11-Jun-24", "05-Dec-25", "03-Mar-26"], TextOf(Banner, CommentMarkKind.BannerDate));
        Assert.Equal(["AT", "DB", "DB"], TextOf(Banner, CommentMarkKind.BannerAuthor));
        Assert.Equal(["NA", "NA"], TextOf(Banner, CommentMarkKind.BannerTicket));
    }

    [Fact]
    public void ChangeRow_Description_RunsToTheEndOfTheLine()
    {
        Assert.Equal(
        [
            "Changed UPC Type to exclude INTERLEAVED2OF5 types",
            "StoreApps changes for IM Project",
            "Performance tuning"
        ], TextOf(Banner, CommentMarkKind.BannerDescription));
    }

    [Fact]
    public void ChangeRow_WithNoTicket_ReadsTheThirdColumnAsTheDescription()
    {
        // The 03-Mar-26 row has three columns, not four. Which one it skipped is decided by content, not
        // position: a ticket is a single token, a description is prose. Counting from the left would
        // colour "Performance tuning" as a ticket on every row laid out like this.
        Assert.Contains("Performance tuning", TextOf(Banner, CommentMarkKind.BannerDescription));
        Assert.DoesNotContain("Performance tuning", TextOf(Banner, CommentMarkKind.BannerTicket));
    }

    [Fact]
    public void ChangeRow_ShortThirdColumn_IsStillATicket()
    {
        // The other side of the same rule — a single token in third place is the ticket, not a description.
        const string sql = "/*********\n** Date  Ticket\n** ----  ----\n** 11-Jun-24  AT  NA\n*********/";

        Assert.Equal(["NA"], TextOf(sql, CommentMarkKind.BannerTicket));
        Assert.Empty(TextOf(sql, CommentMarkKind.BannerDescription));
    }

    [Fact]
    public void ChangeRow_SplitsOnTabs_NotOnCharacterOffsets()
    {
        // Real files mix tabs and spaces in these tables. A column found by counting characters lands
        // somewhere different under every tab-width setting the reader might have.
        const string sql = "/*********\n** Date\tAuthor\tTicket\tDescription\n** ----\t----\t----\t----\n** 11-Jun-24\tAT\tNA\tChanged the thing\n*********/";

        Assert.Equal(["11-Jun-24"], TextOf(sql, CommentMarkKind.BannerDate));
        Assert.Equal(["AT"], TextOf(sql, CommentMarkKind.BannerAuthor));
        Assert.Equal(["NA"], TextOf(sql, CommentMarkKind.BannerTicket));
        Assert.Equal(["Changed the thing"], TextOf(sql, CommentMarkKind.BannerDescription));
    }

    [Fact]
    public void ChangeRow_NeedsADateInTheFirstColumn()
    {
        // Without it there is nothing to say a multi-column line is a change row rather than free text.
        const string sql = "/*********\n** Some words  and more words\n*********/";

        Assert.Empty(TextOf(sql, CommentMarkKind.BannerDate));
        Assert.Equal(["Some words  and more words"], TextOf(sql, CommentMarkKind.BannerProse));
    }

    [Theory]
    [InlineData("11-Jun-24")]
    [InlineData("2024-06-11")]
    [InlineData("11/06/24")]
    public void ChangeRow_AcceptsTheCommonDateFormats(string date)
    {
        string sql = $"/*********\n** {date}    AT    NA    Did a thing\n*********/";

        Assert.Equal([date], TextOf(sql, CommentMarkKind.BannerDate));
    }

    // --- shape ---

    [Fact]
    public void OrdinaryBlockComment_GetsNoBannerMarks()
    {
        // A banner is a comment whose first line is nothing but stars. Without that, `Description :` inside
        // any ordinary block comment would light up.
        Assert.Empty(CommentMarkScanner.Scan("/* Description : just a note\n   over two lines */"));
    }

    [Fact]
    public void SingleLineStarComment_IsNotTornIntoHeadings()
    {
        // One line, so not a banner — and the star run makes it decoration rather than a `*` highlight,
        // so it comes back with nothing at all and keeps the editor's comment colour.
        Assert.Empty(CommentMarkScanner.Scan("/**** Section ****/"));
    }

    [Fact]
    public void MultipleStars_AreDecorationNotAHighlightTag()
    {
        Assert.Empty(CommentMarkScanner.Scan("-- ** section **"));
        Assert.Equal(CommentMarkKind.Highlight, Assert.Single(CommentMarkScanner.Scan("-- * the interesting bit")).Kind);
    }

    [Fact]
    public void Marks_ComeBackInPositionOrder_AndDoNotOverlap()
    {
        // The tagger emits one classification per mark and the editor composes overlaps unpredictably.
        var marks = CommentMarkScanner.Scan(Banner);

        for (int i = 1; i < marks.Count; i++)
            Assert.True(marks[i].Start >= marks[i - 1].End, $"mark {i} ({marks[i].Kind}) starts at {marks[i].Start}, inside the {marks[i - 1].Kind} ending at {marks[i - 1].End}");
    }
}
