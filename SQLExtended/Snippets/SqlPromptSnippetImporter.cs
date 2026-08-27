using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace SQLExtended.Snippets;

/// <summary>
/// Converts Redgate SQL Prompt snippets into SQLExtended <see cref="SqlSnippet"/>s.
/// SQL Prompt stores each snippet as a JSON object (one per file) with the shape:
/// <code>{ "id": "...", "prefix": "sth", "description": "", "body": "SELECT ..." }</code>
/// Files may contain a single object or an array of objects.
/// </summary>
internal static class SqlPromptSnippetImporter
{
    /// <summary>Raw SQL Prompt snippet as stored on disk.</summary>
    private sealed class SqlPromptSnippet
    {
        [JsonProperty("prefix")]
        public string Prefix { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("body")]
        public string Body { get; set; }

        [JsonProperty("placeholders")]
        public List<SqlPromptPlaceholder> Placeholders { get; set; }
    }

    /// <summary>A custom placeholder ($name$) with its default value.</summary>
    private sealed class SqlPromptPlaceholder
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("defaultValue")]
        public string DefaultValue { get; set; }
    }

    /// <summary>
    /// Parses SQL Prompt snippet JSON (a single object or an array) and converts it to
    /// SQLExtended snippets. Entries without a prefix or body are skipped. Returns an empty
    /// list if the JSON is empty; throws on malformed JSON so callers can surface the error.
    /// </summary>
    public static List<SqlSnippet> Convert(string json)
    {
        var result = new List<SqlSnippet>();
        if (string.IsNullOrWhiteSpace(json))
            return result;

        var token = JToken.Parse(json);
        var items = token.Type == JTokenType.Array
            ? token.ToObject<List<SqlPromptSnippet>>()
            : new List<SqlPromptSnippet> { token.ToObject<SqlPromptSnippet>() };

        if (items == null)
            return result;

        foreach (var item in items)
        {
            var converted = Convert(item);
            if (converted != null)
                result.Add(converted);
        }

        return result;
    }

    private static SqlSnippet Convert(SqlPromptSnippet source)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.Prefix) || string.IsNullOrEmpty(source.Body))
            return null;

        string description = source.Description?.Trim() ?? "";

        return new SqlSnippet
        {
            Code = source.Prefix.Trim(),
            // SQL Prompt has no title field; use its description, falling back to the prefix.
            Title = description.Length > 0 ? description : source.Prefix.Trim(),
            Description = description,
            Body = ConvertBody(source.Body),
            Defaults = ConvertPlaceholders(source.Placeholders)
        };
    }

    /// <summary>
    /// Maps SQL Prompt's placeholder list to SQLExtended's Defaults dictionary
    /// (placeholder name → default value). Returns null when there are none so the
    /// field is omitted from serialized JSON.
    /// </summary>
    private static Dictionary<string, string> ConvertPlaceholders(List<SqlPromptPlaceholder> placeholders)
    {
        if (placeholders == null || placeholders.Count == 0)
            return null;

        var defaults = new Dictionary<string, string>();
        foreach (var p in placeholders)
        {
            if (!string.IsNullOrWhiteSpace(p?.Name))
                defaults[p.Name.Trim()] = p.DefaultValue ?? "";
        }

        return defaults.Count > 0 ? defaults : null;
    }

    /// <summary>
    /// Normalizes SQL Prompt placeholder syntax to SQLExtended's. Most SQL Prompt tokens
    /// ($CURSOR$, $DATE$, $USER$, ...) already match our case-insensitive built-ins, so
    /// only the selection/clipboard tokens that have no equivalent are stripped.
    /// </summary>
    private static string ConvertBody(string body)
    {
        if (string.IsNullOrEmpty(body))
            return body;

        // $SELECTEDTEXT$ and $PASTE$ have no meaning on plain insertion — drop them
        // rather than let them become stray interactive tab stops.
        return body
            .Replace("$SELECTEDTEXT$", "")
            .Replace("$PASTE$", "");
    }
}
