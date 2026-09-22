using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

/// <summary>
/// Covers <see cref="SqlCompletionContext.IsCamelCaseMatch"/>, which ranks the loosest tier of the
/// completion list (<c>SqlCompletionItemManager.Score</c> rank 6) when "CamelCase matching" is on.
///
/// <para>Worth pinning because it shipped unreferenced: the setting that turns it on was never read, so
/// nothing in the product had ever called this. Its own failure is silent in both directions — too strict
/// and the hump match the checkbox promises simply never happens, too loose and it admits unrelated objects
/// at the bottom of every list, and neither reports itself.</para>
/// </summary>
public class CamelCaseMatchTests
{
    [Theory]
    [InlineData("OD", "OrderDetails")]       // the documented example
    [InlineData("od", "order_details")]      // humps after underscores, typed lowercase
    [InlineData("ti", "TimesheetItem")]
    [InlineData("O", "OrderDetails")]        // a single initial
    [InlineData("Order", "OrderDetails")]    // plain prefix, via the substring branch
    [InlineData("Details", "OrderDetails")]  // plain substring, not at a hump
    public void Matches(string typed, string candidate)
    {
        Assert.True(SqlCompletionContext.IsCamelCaseMatch(typed, candidate));
    }

    [Theory]
    [InlineData("OD", "Orders")]             // 'd' mid-word is not a hump
    [InlineData("OX", "OrderDetails")]       // second initial absent
    [InlineData("DO", "OrderDetails")]       // right humps, wrong order
    [InlineData("ODX", "OrderDetails")]      // runs out of humps
    [InlineData("OrderDetails", "OD")]       // typed longer than the candidate
    public void Does_not_match(string typed, string candidate)
    {
        Assert.False(SqlCompletionContext.IsCamelCaseMatch(typed, candidate));
    }

    [Theory]
    [InlineData("", "OrderDetails")]
    [InlineData("OD", "")]
    [InlineData(null, "OrderDetails")]
    [InlineData("OD", null)]
    public void Empty_input_never_matches(string typed, string candidate)
    {
        Assert.False(SqlCompletionContext.IsCamelCaseMatch(typed, candidate));
    }
}
