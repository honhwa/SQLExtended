using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SQLExtended.Search;

internal static class TempTableScriptBuilder
{
    /// <summary>
    /// Returns true when the given schema script describes a table (starts with CREATE TABLE).
    /// </summary>
    internal static bool IsTableScript(string schemaScript) =>
        !string.IsNullOrEmpty(schemaScript) &&
        schemaScript.TrimStart().StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts the bare object name from a qualified name (e.g. "dbo.Orders" → "Orders").
    /// </summary>
    internal static string ExtractObjectName(string qualifiedName)
    {
        if (string.IsNullOrEmpty(qualifiedName))
            return qualifiedName;
        string last = qualifiedName.Split('.').Last();
        return last.Trim('[', ']');
    }

    /// <summary>
    /// Builds a CREATE TABLE script for a temp table from the given schema script.
    /// Strips the indexes and FK sections, removes named PK constraints (which must be
    /// anonymous on temp tables to avoid cross-session name collisions), and optionally
    /// prepends DROP TABLE IF EXISTS.
    /// </summary>
    internal static string Build(string schemaScript, string tableName, bool includeDropIfExists)
    {
        if (string.IsNullOrEmpty(schemaScript))
            return "";

        var lines = schemaScript.Split('\n');
        var createLines = new List<string>();
        bool started = false;
        bool done = false;

        foreach (var rawLine in lines)
        {
            if (done) break;
            string trimmed = rawLine.TrimEnd('\r').TrimStart();

            if (!started)
            {
                if (trimmed.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
                    started = true;
                else
                    continue;
            }

            if (trimmed.StartsWith("-- ==="))
                break;

            string processed = rawLine.TrimEnd('\r');

            // Remove named CONSTRAINT on PRIMARY KEY — temp table PK constraints must be
            // anonymous because named constraints must be unique across concurrent sessions.
            if (trimmed.StartsWith("CONSTRAINT ", StringComparison.OrdinalIgnoreCase) &&
                trimmed.IndexOf("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                processed = Regex.Replace(
                    processed,
                    @"CONSTRAINT\s+\[?[^\]\s]+\]?\s+",
                    "",
                    RegexOptions.IgnoreCase);
            }

            createLines.Add(processed);

            if (trimmed == ");")
                done = true;
        }

        if (createLines.Count == 0)
            return "";

        var sb = new StringBuilder();

        if (includeDropIfExists)
        {
            sb.AppendLine($"DROP TABLE IF EXISTS [#{tableName}];");
            sb.AppendLine();
        }

        foreach (var line in createLines)
        {
            string transformed = Regex.Replace(
                line,
                @"CREATE\s+TABLE\s+\[?[^\]\.\s]+\]?\.\[?[^\]\s]+\]?",
                $"CREATE TABLE [#{tableName}]",
                RegexOptions.IgnoreCase);

            sb.AppendLine(transformed);
        }

        return sb.ToString().TrimEnd();
    }
}
