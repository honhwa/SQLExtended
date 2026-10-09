using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

/// <summary>
/// When a typed quote brings its closing quote. A wrong "yes" is the costly direction: the quote that closes a
/// literal gets a second one, which opens a new string and silently swallows the rest of the script.
/// </summary>
public class SqlQuotePairingTests
{
    [Theory]
    [InlineData("SELECT * FROM T WHERE Name = ", '\0')]
    [InlineData("SELECT * FROM T WHERE Name = ", '\r')]
    [InlineData("SELECT * FROM T WHERE Name = ", ')')]
    [InlineData("INSERT INTO T VALUES (", ')')]
    [InlineData("INSERT INTO T VALUES (1, ", ')')]
    [InlineData("SET @s = N", '\0')]          // N'…' literal
    [InlineData("WHERE x IN (n", ')')]          // lower-case prefix too
    [InlineData("WHERE a = 'x' AND b = ", '\0')] // after a closed literal
    [InlineData("SELECT 'It'", '\0')]           // typing '' to escape: reopens as 'It''|'
    [InlineData("", '\0')]
    [InlineData("-- comment\r\nSELECT ", '\0')] // line comment ended
    [InlineData("/* a /* b */ c */ SELECT ", '\0')] // nested block comment closed
    [InlineData("SELECT [a]]b] = ", '\0')]      // ]] escape inside brackets
    [InlineData("SELECT \"a\"\"b\" = ", '\0')]  // "" escape inside double quotes
    public void Pairs_in_code(string before, char next) => Assert.True(SqlQuotePairing.ShouldPair(before, next));

    [Theory]
    [InlineData("WHERE Name = 'abc", '\0')]                 // closing a literal
    [InlineData("WHERE Name = 'line one\r\nline two", '\0')] // closing a multi-line literal
    [InlineData("WHERE Name = 'It''s", '\0')]               // still inside after an escaped quote
    [InlineData("-- don", '\0')]                            // line comment
    [InlineData("/* don", '\0')]                            // block comment
    [InlineData("/* a /* b */ don", '\0')]                  // still inside a nested block comment
    [InlineData("SELECT [O", '\0')]                         // bracketed name
    [InlineData("SELECT \"O", '\0')]                        // double-quoted name
    [InlineData("WHERE Name = O", '\0')]                    // apostrophe after a word: O'Brien
    [InlineData("WHERE Name = AN", '\0')]                   // N ending a word is not a prefix
    [InlineData("SELECT 1", '\0')]                          // glued to a number
    [InlineData("WHERE Name = ", 'a')]                      // wrapping existing text
    [InlineData("WHERE Name = ", '\'')]                     // in front of an existing literal
    public void Does_not_pair(string before, char next) => Assert.False(SqlQuotePairing.ShouldPair(before, next));

    [Theory]
    [InlineData("SELECT 'a'", "Code")]
    [InlineData("SELECT 'a''", "String")]
    [InlineData("SELECT 'a''b'", "Code")]
    [InlineData("SELECT '--'", "Code")]   // comment marker inside a string
    [InlineData("SELECT '/*'", "Code")]
    [InlineData("-- it's\r\n", "Code")]   // quote inside a comment
    [InlineData("/* it's */", "Code")]
    [InlineData("[it's]", "Code")]
    [InlineData("SELECT 1 /", "Code")]
    public void RegionAt(string text, string expected) => Assert.Equal(expected, SqlQuotePairing.RegionAt(text).ToString());
}
