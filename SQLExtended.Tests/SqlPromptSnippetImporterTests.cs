using SQLExtended.Snippets;
using Xunit;

namespace SQLExtended.Tests;

public class SqlPromptSnippetImporterTests
{
    [Fact]
    public void Convert_SingleObject_MapsFields()
    {
        const string json = @"{
  ""id"": ""4c8d0f9b-0c54-4631-ad3f-44e2b5d2ca00"",
  ""prefix"": ""sth"",
  ""description"": ""Select top 100"",
  ""body"": ""SELECT TOP(100) * \nFROM ""
}";

        var result = SqlPromptSnippetImporter.Convert(json);

        var snippet = Assert.Single(result);
        Assert.Equal("sth", snippet.Code);
        Assert.Equal("Select top 100", snippet.Title);
        Assert.Equal("Select top 100", snippet.Description);
        Assert.Equal("SELECT TOP(100) * \nFROM ", snippet.Body);
    }

    [Fact]
    public void Convert_EmptyDescription_UsesPrefixAsTitle()
    {
        const string json = @"{ ""prefix"": ""sth"", ""description"": """", ""body"": ""SELECT 1"" }";

        var snippet = Assert.Single(SqlPromptSnippetImporter.Convert(json));

        Assert.Equal("sth", snippet.Title);
        Assert.Equal("", snippet.Description);
    }

    [Fact]
    public void Convert_Array_ReturnsAll()
    {
        const string json = @"[
  { ""prefix"": ""a"", ""body"": ""SELECT 1"" },
  { ""prefix"": ""b"", ""body"": ""SELECT 2"" }
]";

        var result = SqlPromptSnippetImporter.Convert(json);

        Assert.Equal(2, result.Count);
        Assert.Equal("a", result[0].Code);
        Assert.Equal("b", result[1].Code);
    }

    [Theory]
    [InlineData("{ \"prefix\": \"\", \"body\": \"SELECT 1\" }")]
    [InlineData("{ \"prefix\": \"x\", \"body\": \"\" }")]
    [InlineData("{ \"body\": \"SELECT 1\" }")]
    public void Convert_MissingPrefixOrBody_Skips(string json)
    {
        Assert.Empty(SqlPromptSnippetImporter.Convert(json));
    }

    [Fact]
    public void Convert_StripsSelectionAndPastePlaceholders()
    {
        const string json = @"{ ""prefix"": ""s"", ""body"": ""BEGIN\n$SELECTEDTEXT$$PASTE$\nEND"" }";

        var snippet = Assert.Single(SqlPromptSnippetImporter.Convert(json));

        Assert.Equal("BEGIN\n\nEND", snippet.Body);
    }

    [Fact]
    public void Convert_PreservesCursorAndSystemPlaceholders()
    {
        const string json = @"{ ""prefix"": ""s"", ""body"": ""-- $USER$ $DATE$\nSELECT $CURSOR$"" }";

        var snippet = Assert.Single(SqlPromptSnippetImporter.Convert(json));

        // These map to SQLExtended's case-insensitive built-ins, so they pass through unchanged.
        Assert.Equal("-- $USER$ $DATE$\nSELECT $CURSOR$", snippet.Body);
    }

    [Fact]
    public void Convert_Placeholders_MapToDefaults()
    {
        const string json = @"{
  ""prefix"": ""whl"",
  ""body"": ""SET ROWCOUNT $count$\n$stmt$"",
  ""placeholders"": [
    { ""name"": ""count"", ""defaultValue"": ""1000"" },
    { ""name"": ""stmt"", ""defaultValue"": """" }
  ]
}";

        var snippet = Assert.Single(SqlPromptSnippetImporter.Convert(json));

        Assert.NotNull(snippet.Defaults);
        Assert.Equal("1000", snippet.Defaults["count"]);
        Assert.Equal("", snippet.Defaults["stmt"]);
    }

    [Fact]
    public void Convert_NoPlaceholders_DefaultsIsNull()
    {
        const string json = @"{ ""prefix"": ""s"", ""body"": ""SELECT 1"" }";

        var snippet = Assert.Single(SqlPromptSnippetImporter.Convert(json));

        Assert.Null(snippet.Defaults);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Convert_EmptyInput_ReturnsEmpty(string json)
    {
        Assert.Empty(SqlPromptSnippetImporter.Convert(json));
    }
}
