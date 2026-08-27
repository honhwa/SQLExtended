using SQLExtended.IntelliSense;
using System.Linq;
using Xunit;

namespace SQLExtended.Tests;

public class SqlBuiltInFunctionsTests
{
    [Theory]
    [InlineData("GETDATE")]
    [InlineData("getdate")]   // case-insensitive
    [InlineData("STRING_SPLIT")]
    [InlineData("DATEADD")]
    [InlineData("ISNULL")]
    [InlineData("ROW_NUMBER")]
    [InlineData("JSON_VALUE")]
    public void Find_KnownFunction_ReturnsIt(string name)
    {
        var fn = SqlBuiltInFunctions.Find(name);
        Assert.NotNull(fn);
        Assert.Equal(name, fn.Name, ignoreCase: true);
    }

    [Theory]
    [InlineData("NOT_A_FUNCTION")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_Unknown_ReturnsNull(string name)
    {
        Assert.Null(SqlBuiltInFunctions.Find(name));
    }

    [Fact]
    public void Getdate_HasParensButNoParameters()
    {
        var fn = SqlBuiltInFunctions.Find("GETDATE");
        Assert.True(fn.RequiresParentheses);
        Assert.Empty(fn.Parameters);
        Assert.Equal("GETDATE()", fn.Signature);
    }

    [Fact]
    public void Dateadd_SignatureListsParameters()
    {
        var fn = SqlBuiltInFunctions.Find("DATEADD");
        Assert.Equal("DATEADD(datepart, number, date)", fn.Signature);
        Assert.Equal(3, fn.Parameters.Count);
    }

    [Fact]
    public void CurrentTimestamp_IsNiladic_NoParens()
    {
        var fn = SqlBuiltInFunctions.Find("CURRENT_TIMESTAMP");
        Assert.False(fn.RequiresParentheses);
        Assert.Equal("CURRENT_TIMESTAMP", fn.Signature);
    }

    [Fact]
    public void OptionalParameter_IsBracketedInSignature()
    {
        // CHARINDEX(expression_to_find, expression_to_search, [start_location])
        var fn = SqlBuiltInFunctions.Find("CHARINDEX");
        Assert.Contains("[start_location]", fn.Signature);
        Assert.True(fn.Parameters.Last().IsOptional);
    }

    [Fact]
    public void AllFunctions_HaveNameCategoryAndDescription()
    {
        foreach (var fn in SqlBuiltInFunctions.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(fn.Name), $"name missing for {fn.Signature}");
            Assert.False(string.IsNullOrWhiteSpace(fn.Category), $"category missing for {fn.Name}");
            Assert.False(string.IsNullOrWhiteSpace(fn.Description), $"description missing for {fn.Name}");
            Assert.False(string.IsNullOrWhiteSpace(fn.ReturnType), $"return type missing for {fn.Name}");
        }
    }

    [Fact]
    public void Catalog_IsReasonablyComprehensive()
    {
        // Guards against accidental truncation of the catalog.
        Assert.True(SqlBuiltInFunctions.All.Count > 120,
            $"expected a broad catalog, found {SqlBuiltInFunctions.All.Count}");
    }
}
