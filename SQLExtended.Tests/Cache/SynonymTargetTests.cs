using SQLExtended.Cache;
using Xunit;

namespace SQLExtended.Tests.Cache;

/// <summary>
/// Pins the PARSENAME-style split of <c>sys.synonyms.base_object_name</c>. A wrong split fails silently: the synonym's
/// columns are looked up under the wrong database or schema, and column completion after it just comes back empty.
/// </summary>
public class SynonymTargetTests
{
    [Theory]
    [InlineData("[dbo].[Orders]", null, null, "dbo", "Orders")]
    [InlineData("[Sales].[dbo].[Orders]", null, "Sales", "dbo", "Orders")]
    [InlineData("[LINKED].[Sales].[dbo].[Orders]", "LINKED", "Sales", "dbo", "Orders")]
    [InlineData("[Sales]..[Orders]", null, "Sales", null, "Orders")]
    [InlineData("[Orders]", null, null, null, "Orders")]
    [InlineData("dbo.Orders", null, null, "dbo", "Orders")]
    [InlineData("[my.db].[dbo].[Order Lines]", null, "my.db", "dbo", "Order Lines")]
    [InlineData("[dbo].[Odd]]Name]", null, null, "dbo", "Odd]Name")]
    [InlineData("\"Sales\".\"dbo\".\"Orders\"", null, "Sales", "dbo", "Orders")]
    public void SplitsTheParts(string text, string server, string database, string schema, string name)
    {
        var target = SynonymTarget.Parse(text);

        Assert.NotNull(target);
        Assert.Equal(server, target.Server);
        Assert.Equal(database, target.Database);
        Assert.Equal(schema, target.Schema);
        Assert.Equal(name, target.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a.b.c.d.e")]
    [InlineData("[dbo].")]
    public void RejectsWhatIsNotAName(string text) => Assert.Null(SynonymTarget.Parse(text));
}
