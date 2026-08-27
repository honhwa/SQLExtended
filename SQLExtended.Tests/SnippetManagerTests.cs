using System.Linq;
using SQLExtended.Snippets;
using Xunit;

namespace SQLExtended.Tests;

public class SnippetManagerTests
{
    [Fact]
    public void Instance_ReturnsNonNull()
    {
        var manager = SnippetManager.Instance;
        Assert.NotNull(manager);
    }

    [Fact]
    public void Snippets_ContainsDefaults()
    {
        var snippets = SnippetManager.Instance.Snippets;
        Assert.True(snippets.Count > 0);
    }

    [Fact]
    public void FindByCode_Sel_ReturnsSelectSnippet()
    {
        var snippet = SnippetManager.Instance.FindByCode("sel");
        Assert.NotNull(snippet);
        Assert.Equal("sel", snippet.Code);
        Assert.Contains("SELECT", snippet.Body);
    }

    [Fact]
    public void FindByCode_CaseInsensitive()
    {
        var snippet = SnippetManager.Instance.FindByCode("SEL");
        Assert.NotNull(snippet);
        Assert.Equal("sel", snippet.Code);
    }

    [Fact]
    public void FindByCode_Cte_ReturnsCteSnippet()
    {
        var snippet = SnippetManager.Instance.FindByCode("cte");
        Assert.NotNull(snippet);
        Assert.Contains("WITH", snippet.Body);
    }

    [Fact]
    public void FindByCode_Unknown_ReturnsNull()
    {
        var snippet = SnippetManager.Instance.FindByCode("zzz_nonexistent");
        Assert.Null(snippet);
    }

    [Fact]
    public void FindByCode_Null_ReturnsNull()
    {
        var snippet = SnippetManager.Instance.FindByCode(null);
        Assert.Null(snippet);
    }

    [Fact]
    public void DefaultSnippets_AllHaveRequiredFields()
    {
        var snippets = SnippetManager.Instance.Snippets;

        foreach (var snippet in snippets)
        {
            Assert.False(string.IsNullOrWhiteSpace(snippet.Code), "Code must not be empty");
            //Assert.False(string.IsNullOrWhiteSpace(snippet.Title), "Title must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(snippet.Body), "Body must not be empty");
        }
    }

    [Fact]
    public void DefaultSnippets_HaveUniqueCode()
    {
        var snippets = SnippetManager.Instance.Snippets;
        var codes = snippets.Select(s => s.Code.ToLowerInvariant()).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void DefaultSnippets_ContainsExpectedCodes()
    {
        var snippets = SnippetManager.Instance.Snippets;
        var codes = snippets.Select(s => s.Code).ToList();

        Assert.Contains("sel", codes);
        Assert.Contains("selt", codes);
        Assert.Contains("ins", codes);
        Assert.Contains("upd", codes);
        Assert.Contains("del", codes);
        Assert.Contains("cte", codes);
        Assert.Contains("iff", codes);
        Assert.Contains("beg", codes);
        Assert.Contains("tran", codes);
    }
}
