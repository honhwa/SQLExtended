using SQLExtended.IntelliSense;
using System.Linq;
using Xunit;

namespace SQLExtended.Tests;

/// <summary>
/// Covers <see cref="SqlKeywords.IsKeywordWord"/>, the vocabulary that drives type-time
/// keyword recasing (KeywordCaseController).
/// </summary>
public class SqlKeywordWordTests
{
    [Theory]
    [InlineData("SELECT")]
    [InlineData("select")]   // case-insensitive
    [InlineData("from")]
    [InlineData("inner")]    // from multi-word "INNER JOIN"
    [InlineData("join")]
    [InlineData("on")]       // 2-letter keyword
    [InlineData("order")]    // from "ORDER BY"
    [InlineData("by")]
    [InlineData("nolock")]   // from "WITH (NOLOCK)"
    [InlineData("int")]      // data type
    [InlineData("identity_insert")]   // underscores kept: the recaser looks up the whole typed word
    [InlineData("xact_abort")]
    [InlineData("nocount")]
    [InlineData("sql_variant")]
    [InlineData("datetime2")]         // digits kept too
    [InlineData("cursor")]            // its own entry now, not a fragment of @@CURSOR_ROWS
    public void Recognizes_keyword_words(string word)
    {
        Assert.True(SqlKeywords.IsKeywordWord(word));
    }

    [Theory]
    [InlineData("transaction_header")] // identifier with underscore, not the TRANSACTION keyword
    [InlineData("customerid")]
    [InlineData("h")]                  // single-char alias
    [InlineData("tl")]                 // two-char alias that isn't a keyword
    [InlineData("@myvar")]             // variable
    [InlineData("")]
    [InlineData(null)]
    [InlineData("status")]             // was a fragment of @@FETCH_STATUS - recased every Status column
    [InlineData("variant")]            // was a fragment of SQL_VARIANT
    [InlineData("level")]              // SET options that would recase ordinary column names stay out
    [InlineData("language")]
    [InlineData("profile")]
    public void Rejects_non_keyword_words(string word)
    {
        Assert.False(SqlKeywords.IsKeywordWord(word));
    }

    [Theory]
    [InlineData("SET ")]
    [InlineData("SELECT 1;\nSET ")]
    [InlineData("SET IDENT")]
    public void SetOptions_AreOfferedAfterSet(string textBeforeCursor)
    {
        var offered = SqlKeywords.GetKeywordsForContext(SqlKeywords.DetectContext(textBeforeCursor)).Select(k => k.Text).ToList();
        Assert.Contains("IDENTITY_INSERT", offered);
        Assert.Contains("NOCOUNT", offered);
        Assert.Contains("XACT_ABORT", offered);
    }

    [Fact]
    public void CompletionOnlyOptions_AreOfferedAfterSet()
    {
        var offered = SqlKeywords.GetKeywordsForContext(SqlKeywords.DetectContext("SET ")).Select(k => k.Text).ToList();
        Assert.Contains("STATISTICS IO", offered);
        Assert.Contains("STATISTICS TIME", offered);
        Assert.Contains("TRANSACTION ISOLATION LEVEL", offered);
    }

    [Theory]
    [InlineData("SET TRANSACTION ISOLATION LEVEL ")]
    [InlineData("set transaction isolation level ")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL REA")]
    [InlineData("BEGIN\n    SET TRANSACTION\n        ISOLATION LEVEL ")]
    public void IsolationLevels_AreOfferedAfterIsolationLevel(string textBeforeCursor)
    {
        var context = SqlKeywords.DetectContext(textBeforeCursor);
        Assert.Equal(KeywordContext.AfterIsolationLevel, context);
        Assert.Equal(new[] { "READ UNCOMMITTED", "READ COMMITTED", "REPEATABLE READ", "SNAPSHOT", "SERIALIZABLE" },
            SqlKeywords.GetKeywordsForContext(context).Select(k => k.Text));
    }

    [Fact]
    public void IsolationLevels_AreNotOfferedElsewhere()
    {
        Assert.DoesNotContain(SqlKeywords.GetKeywordsForContext(SqlKeywords.DetectContext("SET ")), k => k.Text == "READ COMMITTED");
        Assert.DoesNotContain(SqlKeywords.GetKeywordsForContext(KeywordContext.General), k => k.Text == "SNAPSHOT");
    }

    // Completion-only entries must stay out of the recase set - these are the column names they would hit.
    [Theory]
    [InlineData("io")]
    [InlineData("profile")]
    [InlineData("statistics")]
    [InlineData("isolation")]
    [InlineData("level")]
    [InlineData("read")]
    [InlineData("committed")]
    [InlineData("snapshot")]
    [InlineData("serializable")]
    public void CompletionOnlyWords_AreNotRecased(string word)
    {
        Assert.False(SqlKeywords.IsKeywordWord(word));
    }

    [Fact]
    public void SetOptions_AreNotOfferedAtStatementStart()
    {
        var offered = SqlKeywords.GetKeywordsForContext(SqlKeywords.DetectContext("")).Select(k => k.Text).ToList();
        Assert.DoesNotContain("IDENTITY_INSERT", offered);
    }
}
