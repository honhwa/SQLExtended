using System.Collections.Generic;
using System.Text;

namespace SQLExtended.Snippets;

/// <summary>
/// Builds VS Code Snippet XML from a <see cref="SqlSnippet"/> for use with
/// <c>IVsExpansion.InsertSpecificExpansion()</c>. System placeholders are resolved
/// to literal values; custom placeholders become interactive tab-stop fields.
/// </summary>
internal static class SnippetXmlBuilder
{
    /// <summary>
    /// Builds VS snippet XML for the given snippet. Returns null if the snippet
    /// has no custom (non-system) placeholders, meaning plain-text insertion suffices.
    /// </summary>
    public static string Build(SqlSnippet snippet)
    {
        if (snippet == null || string.IsNullOrEmpty(snippet.Body))
            return null;

        // Resolve system placeholders first, leaving custom ones as $name$
        string body = SnippetPlaceholderResolver.ResolveSystemOnly(snippet.Body);

        // Find custom placeholders remaining in the body
        var customNames = SnippetPlaceholderResolver.GetCustomPlaceholderNames(body);
        if (customNames.Count == 0)
            return null;

        var defaults = snippet.Defaults ?? new Dictionary<string, string>();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<CodeSnippets xmlns=\"http://schemas.microsoft.com/VisualStudio/2005/CodeSnippet\">");
        sb.AppendLine("  <CodeSnippet Format=\"1.0.0\">");
        sb.AppendLine("    <Header>");
        sb.Append("      <Title>").Append(EscapeXml(snippet.Title ?? snippet.Code)).AppendLine("</Title>");
        sb.Append("      <Description>").Append(EscapeXml(snippet.Description ?? "")).AppendLine("</Description>");
        sb.AppendLine("    </Header>");
        sb.AppendLine("    <Snippet>");

        // Declare each custom placeholder as a Literal with a default value
        sb.AppendLine("      <Declarations>");
        foreach (var name in customNames)
        {
            string defaultValue = defaults.TryGetValue(name, out string val) ? val : name;
            sb.AppendLine("        <Literal>");
            sb.Append("          <ID>").Append(EscapeXml(name)).AppendLine("</ID>");
            sb.Append("          <Default>").Append(EscapeXml(defaultValue)).AppendLine("</Default>");
            sb.AppendLine("        </Literal>");
        }
        sb.AppendLine("      </Declarations>");

        // The body — $placeholder$ syntax already matches VS snippet field syntax.
        // Append $end$ to mark final caret position after tab-through.
        sb.AppendLine("      <Code Language=\"SQL\">");
        sb.Append("        <![CDATA[").Append(body).AppendLine("$end$]]>");
        sb.AppendLine("      </Code>");

        sb.AppendLine("    </Snippet>");
        sb.AppendLine("  </CodeSnippet>");
        sb.AppendLine("</CodeSnippets>");

        return sb.ToString();
    }

    private static string EscapeXml(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
