using System.Collections.Generic;
using SQLExtended.Snippets;
using Xunit;

namespace SQLExtended.Tests;

public class SnippetXmlBuilderTests
{
    [Fact]
    public void Build_NullSnippet_ReturnsNull()
    {
        Assert.Null(SnippetXmlBuilder.Build(null));
    }

    [Fact]
    public void Build_EmptyBody_ReturnsNull()
    {
        var snippet = new SqlSnippet { Code = "test", Body = "" };
        Assert.Null(SnippetXmlBuilder.Build(snippet));
    }

    [Fact]
    public void Build_NoCustomPlaceholders_ReturnsNull()
    {
        var snippet = new SqlSnippet
        {
            Code = "hdr",
            Title = "Header",
            Body = "-- $date$ $user$"
        };
        Assert.Null(SnippetXmlBuilder.Build(snippet));
    }

    [Fact]
    public void Build_OnlySystemPlaceholders_ReturnsNull()
    {
        var snippet = new SqlSnippet
        {
            Code = "seldb",
            Body = "USE [$dbname$]\nGO"
        };
        Assert.Null(SnippetXmlBuilder.Build(snippet));
    }

    [Fact]
    public void Build_WithCustomPlaceholders_ReturnsXml()
    {
        var snippet = new SqlSnippet
        {
            Code = "selt",
            Title = "SELECT TOP",
            Description = "Select top n",
            Body = "SELECT TOP $count$ *\nFROM $table$",
            Defaults = new Dictionary<string, string> { { "count", "100" }, { "table", "MyTable" } }
        };

        string xml = SnippetXmlBuilder.Build(snippet);
        Assert.NotNull(xml);
        Assert.Contains("<CodeSnippet", xml);
        Assert.Contains("<Title>SELECT TOP</Title>", xml);
        Assert.Contains("<ID>count</ID>", xml);
        Assert.Contains("<Default>100</Default>", xml);
        Assert.Contains("<ID>table</ID>", xml);
        Assert.Contains("<Default>MyTable</Default>", xml);
        Assert.Contains("$count$", xml);
        Assert.Contains("$table$", xml);
        Assert.Contains("$end$", xml);
    }

    [Fact]
    public void Build_CustomPlaceholderWithoutDefault_UsesNameAsDefault()
    {
        var snippet = new SqlSnippet
        {
            Code = "test",
            Title = "Test",
            Body = "SELECT $foo$"
        };

        string xml = SnippetXmlBuilder.Build(snippet);
        Assert.NotNull(xml);
        Assert.Contains("<ID>foo</ID>", xml);
        Assert.Contains("<Default>foo</Default>", xml);
    }

    [Fact]
    public void Build_MixedSystemAndCustom_ResolvesSystemInBody()
    {
        var snippet = new SqlSnippet
        {
            Code = "test",
            Title = "Test",
            Body = "-- $user$ SELECT TOP $count$",
            Defaults = new Dictionary<string, string> { { "count", "50" } }
        };

        string xml = SnippetXmlBuilder.Build(snippet);
        Assert.NotNull(xml);
        // System placeholder $user$ should be resolved to the actual username
        Assert.DoesNotContain("$user$", xml);
        Assert.Contains(System.Environment.UserName, xml);
        // Custom placeholder $count$ remains as a field
        Assert.Contains("$count$", xml);
        Assert.Contains("<ID>count</ID>", xml);
    }

    [Fact]
    public void Build_EscapesXmlCharactersInTitle()
    {
        var snippet = new SqlSnippet
        {
            Code = "test",
            Title = "Test <script> & 'quotes'",
            Body = "SELECT $col$",
            Defaults = new Dictionary<string, string> { { "col", "*" } }
        };

        string xml = SnippetXmlBuilder.Build(snippet);
        Assert.NotNull(xml);
        Assert.Contains("&lt;script&gt;", xml);
        Assert.Contains("&amp;", xml);
    }

    [Fact]
    public void Build_LinkedFields_DuplicatePlaceholderAppearsOnce()
    {
        var snippet = new SqlSnippet
        {
            Code = "cte",
            Title = "CTE",
            Body = "WITH $cteName$ AS (...)\nSELECT * FROM $cteName$",
            Defaults = new Dictionary<string, string> { { "cteName", "cte" } }
        };

        string xml = SnippetXmlBuilder.Build(snippet);
        Assert.NotNull(xml);

        // Only one Literal declaration for cteName
        int idCount = CountOccurrences(xml, "<ID>cteName</ID>");
        Assert.Equal(1, idCount);

        // But the body has $cteName$ twice
        int bodyCount = CountOccurrences(xml, "$cteName$");
        Assert.Equal(2, bodyCount);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
