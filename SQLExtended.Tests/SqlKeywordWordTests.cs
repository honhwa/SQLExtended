using SQLExtended.IntelliSense;
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
    public void Rejects_non_keyword_words(string word)
    {
        Assert.False(SqlKeywords.IsKeywordWord(word));
    }
}
