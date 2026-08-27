using System;
using System.Text.RegularExpressions;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended;

/// <summary>
/// Extracts the object name (table/view) at the cursor position in the SSMS query editor.
/// Handles selected text, bracket-quoted names, and schema-qualified names like [dbo].[MyTable].
/// </summary>
internal static class EditorHelper
{
    // Valid SQL identifier characters (letters, digits, underscore, #, @)
    // Plus brackets and dots for qualified names like [dbo].[TableName]
    private static readonly Regex IdentifierPattern = new Regex(
        @"[\w@#\.\[\]]+",
        RegexOptions.Compiled);

    /// <summary>
    /// Gets the SQL object name at the current cursor position.
    /// If text is selected, uses the selection. Otherwise expands outward
    /// from the cursor to capture the full identifier (including schema prefix).
    /// </summary>
    public static string GetObjectNameAtCursor(IServiceProvider serviceProvider)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var dte = (DTE2)serviceProvider.GetService(typeof(DTE));
        if (dte?.ActiveDocument == null)
            return null;

        var textSelection = dte.ActiveDocument.Selection as TextSelection;
        if (textSelection == null)
            return null;

        // If user has selected text, use that directly
        string selected = textSelection.Text?.Trim();
        if (!string.IsNullOrEmpty(selected))
            return CleanObjectName(selected);

        // Otherwise, get the current line and find the identifier at the cursor position
        string lineText = GetCurrentLineText(textSelection);
        int cursorColumn = textSelection.ActivePoint.DisplayColumn - 1; // 0-based

        if (string.IsNullOrEmpty(lineText) || cursorColumn < 0 || cursorColumn >= lineText.Length)
            return null;

        // Find the identifier span that contains the cursor position
        foreach (Match match in IdentifierPattern.Matches(lineText))
        {
            if (cursorColumn >= match.Index && cursorColumn < match.Index + match.Length)
            {
                return CleanObjectName(match.Value);
            }
        }

        return null;
    }

    private static string GetCurrentLineText(TextSelection textSelection)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var point = textSelection.ActivePoint;
        var startOfLine = point.CreateEditPoint();
        startOfLine.StartOfLine();

        var endOfLine = point.CreateEditPoint();
        endOfLine.EndOfLine();

        return startOfLine.GetText(endOfLine);
    }

    /// <summary>
    /// Cleans up a SQL object name:
    ///   - Strips surrounding whitespace
    ///   - Preserves schema.table format
    ///   - Strips outer brackets: [dbo].[MyTable] → dbo.MyTable
    /// Returns (schema, objectName) tuple embedded in a single string
    /// that SchemaQueryService can parse.
    /// </summary>
    private static string CleanObjectName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Remove bracket quoting: [dbo].[TableName] → dbo.TableName
        raw = raw.Replace("[", "").Replace("]", "");

        // Trim trailing dots, semicolons, commas, parens
        raw = raw.TrimEnd('.', ';', ',', '(', ')', ' ');
        raw = raw.TrimStart(' ');

        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    /// <summary>
    /// Splits a potentially qualified name into (database, schema, name).
    /// Handles all SQL Server multi-part name formats:
    ///   "Customers"                    → (null, null, "Customers")
    ///   "dbo.Customers"                → (null, "dbo", "Customers")
    ///   "MyDB..Customers"              → ("MyDB", null, "Customers")
    ///   "MyDB.dbo.Customers"           → ("MyDB", "dbo", "Customers")
    ///   "Server.MyDB.dbo.Customers"    → ("MyDB", "dbo", "Customers") — server ignored
    ///   "[MyDB]..[Customers]"          → ("MyDB", null, "Customers")
    /// Bracket quoting is stripped before returning.
    /// </summary>
    public static (string Database, string Schema, string Name) ParseObjectName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return (null, null, null);

        // Split on dots but preserve empty parts (for "database..table" syntax)
        // First strip brackets so we can split cleanly
        var cleaned = fullName.Replace("[", "").Replace("]", "");
        var parts = cleaned.Split('.');

        return parts.Length switch
        {
            1 => (null, null, parts[0].Trim()),
            2 => (null, parts[0].Trim(), parts[1].Trim()),
            3 => (NullIfEmpty(parts[0]), NullIfEmpty(parts[1]), parts[2].Trim()),
            // 4-part: server.database.schema.table — ignore server
            _ => (NullIfEmpty(parts[parts.Length - 3]), NullIfEmpty(parts[parts.Length - 2]), parts[parts.Length - 1].Trim())
        };
    }

    private static string NullIfEmpty(string value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
