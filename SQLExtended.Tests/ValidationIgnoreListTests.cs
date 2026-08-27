using SQLExtended.Validation;
using Xunit;

namespace SQLExtended.Tests;

public class ValidationIgnoreListTests
{
    [Fact]
    public void IgnoredDatabase_MatchesCaseInsensitively()
    {
        var list = new ValidationIgnoreList();
        Assert.True(list.AddDatabase("msdbCentral"));

        Assert.True(list.IsIgnored("MSDBCENTRAL", "dbo", "backupset"));
        Assert.False(list.IsIgnored("OtherDb", "dbo", "backupset"));
    }

    [Fact]
    public void IgnoredObject_MatchesSchemaAndEntity()
    {
        var list = new ValidationIgnoreList();
        Assert.True(list.AddObject("dbo", "SqlServerVersions"));

        Assert.True(list.IsIgnored(null, "dbo", "sqlserverversions"));
        Assert.False(list.IsIgnored(null, "dbo", "SomethingElse"));
    }

    [Fact]
    public void AddObject_DefaultsSchemaToDbo()
    {
        var list = new ValidationIgnoreList();
        list.AddObject(null, "Widget");

        Assert.Contains("dbo.Widget", list.Objects);
        Assert.True(list.IsIgnored(null, null, "Widget"));
    }

    [Fact]
    public void Add_IsIdempotent()
    {
        var list = new ValidationIgnoreList();

        Assert.True(list.AddDatabase("Foo"));
        Assert.False(list.AddDatabase("foo"));
        Assert.True(list.AddObject("dbo", "Bar"));
        Assert.False(list.AddObject("dbo", "bar"));

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Remove_DropsEntry()
    {
        var list = new ValidationIgnoreList();
        list.AddDatabase("Foo");
        list.AddObject("dbo", "Bar");

        list.Remove("Foo");
        list.Remove("dbo.Bar");

        Assert.Equal(0, list.Count);
        Assert.False(list.IsIgnored("Foo", "dbo", "Bar"));
    }

    [Fact]
    public void EmptyList_IgnoresNothing()
    {
        var list = new ValidationIgnoreList();
        Assert.False(list.IsIgnored("AnyDb", "dbo", "AnyObject"));
    }
}
