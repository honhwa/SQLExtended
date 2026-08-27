using System.Collections.Generic;
using System.Linq;
using SQLExtended.EnvTabs;
using Xunit;

namespace SQLExtended.Tests.EnvTabs;

/// <summary>
/// Matching is where a mistake here is both silent and dangerous: an over-broad rule paints a production
/// tab in the development colour, which is the exact failure this feature exists to prevent.
/// </summary>
public class EnvTabRuleTests
{
    private static EnvTabRule Rule(string server, string database = "", int color = 0, EnvTabMatchMode mode = EnvTabMatchMode.Wildcard) =>
        new() { ServerPattern = server, DatabasePattern = database, ColorIndex = color, MatchMode = mode, Label = "L" };

    [Theory]
    [InlineData("PROD-SQL01", "PROD-SQL01", true)]
    [InlineData("PROD-SQL01", "prod-sql01", true)]   // server names are case-insensitive
    [InlineData("PROD-SQL01", "PROD-SQL02", false)]
    [InlineData("PROD*", "PROD-SQL01", true)]
    [InlineData("PROD*", "PRODUCTION", true)]
    [InlineData("PROD*", "DEV-SQL01", false)]
    [InlineData("SQL0?", "SQL01", true)]
    [InlineData("SQL0?", "SQL011", false)]           // ? is exactly one character
    [InlineData("*", "anything", true)]
    public void WildcardMatchingIsAnchoredAndCaseInsensitive(string pattern, string server, bool expected) =>
        Assert.Equal(expected, Rule(pattern).Matches(server, "db"));

    [Fact]
    public void PatternIsAnchored_SoASubstringDoesNotMatch()
    {
        // "PROD" must not match "NOT-PROD-REALLY". An unanchored implementation is the classic way this
        // goes wrong, and it fails in the dangerous direction.
        Assert.False(Rule("PROD").Matches("NOT-PROD-REALLY", "db"));
        Assert.True(Rule("PROD").Matches("PROD", "db"));
    }

    [Fact]
    public void EmptyPatternMatchesAnything_ButANamedPatternNeverMatchesAnUnknownValue()
    {
        Assert.True(Rule("").Matches("any-server", "any-db"));
        Assert.True(Rule("SRV", "").Matches("SRV", null));

        // The connection could not be read. A rule naming a database must not claim it.
        Assert.False(Rule("SRV", "Sales").Matches("SRV", null));
        Assert.False(Rule("SRV").Matches(null, "db"));
    }

    [Fact]
    public void DisabledRuleNeverMatches()
    {
        var rule = Rule("*");
        rule.Enabled = false;
        Assert.False(rule.Matches("anything", "anything"));
    }

    [Fact]
    public void RegexModeIsSupported_AndAMalformedPatternFailsClosedRatherThanThrowing()
    {
        Assert.True(Rule(@"PROD-SQL\d+", mode: EnvTabMatchMode.Regex).Matches("PROD-SQL07", "db"));
        Assert.False(Rule(@"PROD-SQL\d+", mode: EnvTabMatchMode.Regex).Matches("PROD-SQLX", "db"));

        // Unbalanced bracket. Must not throw into the tab-update path, and must not match.
        Assert.False(Rule("PROD[", mode: EnvTabMatchMode.Regex).Matches("PROD[", "db"));
    }

    [Fact]
    public void WildcardMetacharactersInALiteralNameAreEscaped()
    {
        // A wildcard pattern of "A.B" must not match "AxB" — the dot is a literal, not a regex dot.
        Assert.False(Rule("A.B").Matches("AxB", "db"));
        Assert.True(Rule("A.B").Matches("A.B", "db"));
    }

    [Fact]
    public void FirstMatchWins_InRuleOrder()
    {
        var set = new EnvTabRuleSet
        {
            Rules = new List<EnvTabRule>
            {
                Rule("PROD-SQL01", color: 3),
                Rule("PROD*", color: 7),
            }
        };

        Assert.Equal(3, set.Match("PROD-SQL01", "db").ColorIndex);
        Assert.Equal(7, set.Match("PROD-SQL02", "db").ColorIndex);
        Assert.Null(set.Match("DEV-SQL01", "db"));
    }

    [Fact]
    public void PromptedRuleGoesToTheTop_SoItBeatsAnExistingCatchAll()
    {
        // Appending instead would make the prompt look like it did nothing whenever a "*" rule exists.
        var set = new EnvTabRuleSet { Rules = new List<EnvTabRule> { Rule("*", color: 1) } };
        set.AddFromPrompt(Rule("PROD-SQL01", color: 9));

        Assert.Equal(9, set.Match("PROD-SQL01", "db").ColorIndex);
        Assert.Equal(1, set.Match("OTHER", "db").ColorIndex);
    }

    [Fact]
    public void ProposedRuleUsesTheLiteralName_NotAGuessedWildcard()
    {
        var proposed = EnvTabRuleSet.ProposeRule("PRODUCTION-01", "Sales", EnvTabGrouping.Server, 4);

        Assert.Equal("PRODUCTION-01", proposed.ServerPattern);
        Assert.Equal("", proposed.DatabasePattern);           // grouping by server only
        Assert.False(proposed.Matches("PROD-SANDBOX", "Sales"));

        var byBoth = EnvTabRuleSet.ProposeRule("PRODUCTION-01", "Sales", EnvTabGrouping.ServerAndDatabase, 4);
        Assert.Equal("Sales", byBoth.DatabasePattern);
        Assert.False(byBoth.Matches("PRODUCTION-01", "Other"));
    }

    [Fact]
    public void NextFreeColorPrefersAnUnusedPaletteEntry()
    {
        var set = new EnvTabRuleSet { Rules = new List<EnvTabRule> { Rule("a", color: 0), Rule("b", color: 1) } };
        Assert.Equal(2, set.NextFreeColor());
    }

    [Fact]
    public void PaletteSanitizesOutOfRangeToNoColor()
    {
        // Wrapping would silently show a different environment's colour.
        Assert.Equal(EnvTabPalette.NoColor, EnvTabPalette.Sanitize(16));
        Assert.Equal(EnvTabPalette.NoColor, EnvTabPalette.Sanitize(-2));
        Assert.Equal(15, EnvTabPalette.Sanitize(15));
        Assert.Equal(EnvTabPalette.Count, EnvTabPalette.All().Length);
        Assert.All(EnvTabPalette.All(), c => Assert.Matches("^#[0-9A-Fa-f]{6}$", c.Hex));
    }
}
