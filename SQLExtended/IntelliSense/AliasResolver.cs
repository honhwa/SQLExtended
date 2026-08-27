using Microsoft.SqlServer.TransactSql.ScriptDom;
using System;
using System.Collections.Generic;
using System.IO;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Parses the current SQL statement using ScriptDom and builds a map of
/// table aliases to their resolved (schema, table) names.
/// </summary>
internal sealed class AliasResolver
{
    /// <summary>
    /// Represents a resolved table reference with optional alias.
    /// </summary>
    internal sealed class TableReference
    {
        /// <summary>Database qualifier for cross-database references (null = current database).</summary>
        public string Database { get; set; }
        public string Schema { get; set; }
        public string Table { get; set; }
        public string Alias { get; set; }

        /// <summary>
        /// The name used to reference this table: alias if present, otherwise the table name.
        /// </summary>
        public string ReferenceName => Alias ?? Table;
    }

    /// <summary>
    /// Parses SQL text and extracts all table references with their aliases.
    /// Returns a list of TableReference objects for all FROM/JOIN clauses found.
    /// </summary>
    public static IReadOnlyList<TableReference> Resolve(string sqlText)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
            return Array.Empty<TableReference>();

        try
        {
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(sqlText);
            var fragment = parser.Parse(reader, out var errors);

            // Even with parse errors, ScriptDom often produces a partial AST
            var visitor = new TableReferenceVisitor();
            fragment.Accept(visitor);

            // If ScriptDom found tables, use them; otherwise fall back to regex
            if (visitor.Tables.Count > 0)
                return visitor.Tables;

            return ExtractTablesRegex(sqlText);
        }
        catch
        {
            // Fall back to regex-based extraction for severely malformed SQL
            return ExtractTablesRegex(sqlText);
        }
    }

    /// <summary>
    /// Finds the table reference that matches the given identifier (alias or table name).
    /// Case-insensitive matching.
    /// </summary>
    public static TableReference FindByIdentifier(IReadOnlyList<TableReference> tables, string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || tables == null)
            return null;

        // First try alias match
        foreach (var t in tables)
        {
            if (!string.IsNullOrEmpty(t.Alias) &&
                string.Equals(t.Alias, identifier, StringComparison.OrdinalIgnoreCase))
                return t;
        }

        // Then try table name match
        foreach (var t in tables)
        {
            if (string.Equals(t.Table, identifier, StringComparison.OrdinalIgnoreCase))
                return t;
        }

        return null;
    }

    /// <summary>
    /// ScriptDom AST visitor that collects NamedTableReference nodes.
    /// </summary>
    private sealed class TableReferenceVisitor : TSqlFragmentVisitor
    {
        public List<TableReference> Tables { get; } = new();

        public override void Visit(NamedTableReference node)
        {
            if (node.SchemaObject == null)
                return;

            var table = new TableReference
            {
                Database = node.SchemaObject.DatabaseIdentifier?.Value,
                Schema = node.SchemaObject.SchemaIdentifier?.Value,
                Table = node.SchemaObject.BaseIdentifier?.Value,
                Alias = node.Alias?.Value
            };

            if (!string.IsNullOrEmpty(table.Table))
                Tables.Add(table);
        }

        // Table variables used in a FROM/JOIN clause (e.g. "FROM @tv t") are
        // VariableTableReference nodes, not NamedTableReference — capture them too so
        // their columns resolve from the current window's local-table scan.
        public override void Visit(VariableTableReference node)
        {
            string name = node.Variable?.Name;
            if (string.IsNullOrEmpty(name))
                return;

            Tables.Add(new TableReference
            {
                Schema = null,
                Table = name,
                Alias = node.Alias?.Value
            });
        }
    }

    /// <summary>
    /// Regex fallback for when ScriptDom can't parse the SQL (incomplete statements).
    /// Extracts FROM/JOIN plus UPDATE/DELETE/MERGE/INSERT target table patterns so
    /// completion works while the user is still typing the WHERE clause.
    /// </summary>
    private static IReadOnlyList<TableReference> ExtractTablesRegex(string sqlText)
    {
        var results = new List<TableReference>();

        // Order matters: multi-word forms (DELETE FROM, INSERT INTO, MERGE INTO) must
        // come before single-word forms so the longer alternative wins. References may be
        // up to three-part (database.schema.table); bracketed segments may contain dots
        // (e.g. [databasename].dbo.Projects), so brackets consume everything to ']'.
        var pattern = new System.Text.RegularExpressions.Regex(
            @"\b(?:DELETE\s+FROM|INSERT\s+INTO|MERGE\s+INTO|FROM|JOIN|UPDATE|MERGE|DELETE)\s+" +
            @"(?:(?:\[(?<q1>[^\]]+)\]|(?<q1>\w+))\s*\.\s*(?:(?:\[(?<q2>[^\]]+)\]|(?<q2>\w+))\s*\.\s*)?)?" +
            @"(?:\[(?<tbl>[^\]]+)\]|(?<tbl>\w+))(?:\s+(?:AS\s+)?(?<alias>\w+))?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (System.Text.RegularExpressions.Match match in pattern.Matches(sqlText))
        {
            // Two qualifiers → database.schema.table; one → schema.table.
            string q1 = match.Groups["q1"].Success ? match.Groups["q1"].Value : null;
            string q2 = match.Groups["q2"].Success ? match.Groups["q2"].Value : null;

            var table = new TableReference
            {
                Database = q2 != null ? q1 : null,
                Schema = q2 ?? q1,
                Table = match.Groups["tbl"].Value,
                Alias = match.Groups["alias"].Success ? match.Groups["alias"].Value : null
            };

            // Filter out SQL keywords that regex might capture as aliases
            if (table.Alias != null && IsSqlKeyword(table.Alias))
                table.Alias = null;

            results.Add(table);
        }

        return results;
    }

    private static bool IsSqlKeyword(string word)
    {
        switch (word.ToUpperInvariant())
        {
            case "WHERE":
            case "ON":
            case "SET":
            case "AND":
            case "OR":
            case "ORDER":
            case "GROUP":
            case "HAVING":
            case "UNION":
            case "INNER":
            case "LEFT":
            case "RIGHT":
            case "CROSS":
            case "FULL":
            case "OUTER":
            case "JOIN":
            case "INTO":
            case "VALUES":
            case "SELECT":
            case "FROM":
            case "WITH":
            case "AS":
            case "WHEN":
            case "THEN":
            case "ELSE":
            case "END":
            case "CASE":
            case "BEGIN":
            case "RETURN":
            case "EXEC":
            case "EXECUTE":
            case "DECLARE":
            case "IF":
            case "WHILE":
            case "NOT":
            case "IN":
            case "EXISTS":
            case "BETWEEN":
            case "LIKE":
            case "IS":
            case "NULL":
            case "TOP":
            case "DISTINCT":
            case "GO":
            case "NOLOCK":
            case "PIVOT":
            case "UNPIVOT":
            case "TABLESAMPLE":
                return true;
            default:
                return false;
        }
    }
}
