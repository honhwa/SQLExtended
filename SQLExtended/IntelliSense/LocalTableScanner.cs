using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SQLExtended.Cache.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Scans a single editor window's text for session-local tables — #local and ##global
/// temp tables (via CREATE TABLE / SELECT … INTO) and DECLARE @t TABLE variables — and
/// extracts their columns. These objects live only in the script being written (a temp
/// table is scoped to the connection's session; a table variable to its batch), so they
/// belong to the current window, never the shared <see cref="Cache.SchemaCache"/>.
/// </summary>
internal static class LocalTableScanner
{
    /// <summary>
    /// Upper bound on script size we'll hand to ScriptDom. A full parse of a multi-megabyte
    /// paste takes long enough to be felt as a freeze, and a script that large is past the
    /// point where local-table completion is useful — so above this we simply scan nothing.
    /// </summary>
    private const int MaxParseLength = 500_000;

    /// <summary>A table defined inline in the current script.</summary>
    internal sealed class LocalTable
    {
        /// <summary>The reference name including its sigil, e.g. "#temp", "##global", "@tv".</summary>
        public string Name { get; set; }
        public bool IsTableVariable { get; set; }
        public bool IsGlobal { get; set; }
        public List<CachedColumn> Columns { get; } = new();
    }

    /// <summary>
    /// Parses <paramref name="sql"/> and returns every local table it can find. Resilient to
    /// partially-typed scripts: ScriptDom usually yields a partial AST even with parse errors,
    /// and any failure falls back to an empty result so completion simply offers nothing extra.
    /// </summary>
    public static IReadOnlyList<LocalTable> Scan(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > MaxParseLength)
            return Array.Empty<LocalTable>();

        // Cheap pre-filter before the (relatively expensive) ScriptDom parse: the only things
        // this scanner can find are '#'/'##' temp tables and DECLARE @v TABLE variables. A '#'
        // anywhere covers CREATE TABLE #t and SELECT … INTO #t (the INTO form is only captured
        // when the target starts with '#'); a table variable needs both DECLARE and TABLE.
        // Absent both, no parse can yield a local table, so skip it — this keeps a large pasted
        // script with no temp tables from stalling completion.
        bool hasHash = sql.IndexOf('#') >= 0;
        bool maybeTableVar = sql.IndexOf("DECLARE", StringComparison.OrdinalIgnoreCase) >= 0 &&
                             sql.IndexOf("TABLE", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!hasHash && !maybeTableVar)
            return Array.Empty<LocalTable>();

        try
        {
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(sql);
            var fragment = parser.Parse(reader, out _);

            var visitor = new LocalTableVisitor();
            fragment.Accept(visitor);
            return visitor.Tables;
        }
        catch
        {
            return Array.Empty<LocalTable>();
        }
    }

    /// <summary>Finds a local table by reference name (case-insensitive, sigil included).</summary>
    public static LocalTable Find(IReadOnlyList<LocalTable> tables, string name)
    {
        if (tables == null || string.IsNullOrEmpty(name))
            return null;
        foreach (var t in tables)
            if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                return t;
        return null;
    }

    /// <summary>True if the identifier names a session-local object (# temp table or @ variable).</summary>
    public static bool IsLocalName(string name) =>
        !string.IsNullOrEmpty(name) && (name[0] == '#' || name[0] == '@');

    private sealed class LocalTableVisitor : TSqlFragmentVisitor
    {
        public List<LocalTable> Tables { get; } = new();

        // CREATE TABLE #temp ( … ) / CREATE TABLE ##global ( … )
        public override void Visit(CreateTableStatement node)
        {
            string name = node.SchemaObjectName?.BaseIdentifier?.Value;
            if (!IsTempName(name))
                return;

            var table = new LocalTable { Name = name, IsGlobal = name.StartsWith("##", StringComparison.Ordinal) };
            AddColumns(table, node.Definition);
            Tables.Add(table);
        }

        // DECLARE @tv TABLE ( … )
        public override void Visit(DeclareTableVariableStatement node)
        {
            string name = node.Body?.VariableName?.Value;
            if (string.IsNullOrEmpty(name))
                return;
            if (!name.StartsWith("@", StringComparison.Ordinal))
                name = "@" + name;

            var table = new LocalTable { Name = name, IsTableVariable = true };
            AddColumns(table, node.Body?.Definition);
            Tables.Add(table);
        }

        // SELECT … INTO #temp FROM …  — capture the name and any determinable column names.
        public override void Visit(SelectStatement node)
        {
            if (node.Into == null || node.QueryExpression is not QuerySpecification spec)
                return;

            string name = node.Into.BaseIdentifier?.Value;
            if (!IsTempName(name))
                return;

            var table = new LocalTable { Name = name, IsGlobal = name.StartsWith("##", StringComparison.Ordinal) };

            int ordinal = 1;
            foreach (var element in spec.SelectElements)
            {
                if (element is not SelectScalarExpression scalar)
                    continue; // SELECT * and similar can't be resolved without the source schema

                string colName = scalar.ColumnName?.Value
                    ?? (scalar.Expression as ColumnReferenceExpression)?.MultiPartIdentifier?.Identifiers?.LastOrDefault()?.Value;
                if (string.IsNullOrEmpty(colName))
                    continue;

                table.Columns.Add(new CachedColumn
                {
                    ColumnName = colName,
                    Ordinal = ordinal++,
                    DataType = "(derived)",
                    IsNullable = true
                });
            }

            Tables.Add(table);
        }

        private static bool IsTempName(string name) =>
            !string.IsNullOrEmpty(name) && name[0] == '#';

        private static void AddColumns(LocalTable table, TableDefinition definition)
        {
            if (definition?.ColumnDefinitions == null)
                return;

            int ordinal = 1;
            foreach (var col in definition.ColumnDefinitions)
            {
                string colName = col.ColumnIdentifier?.Value;
                if (string.IsNullOrEmpty(colName))
                    continue;

                bool isComputed = col.ComputedColumnExpression != null;
                bool isNullable = col.Constraints
                    .OfType<NullableConstraintDefinition>()
                    .Select(c => (bool?)c.Nullable)
                    .FirstOrDefault() ?? true;

                table.Columns.Add(new CachedColumn
                {
                    ColumnName = colName,
                    Ordinal = ordinal++,
                    DataType = isComputed ? "computed" : (TokenText(col.DataType) ?? "unknown"),
                    IsNullable = isNullable,
                    IsIdentity = col.IdentityOptions != null,
                    IsComputed = isComputed
                });
            }
        }

        /// <summary>Reconstructs a fragment's source text from its token range, dropping whitespace/comments.</summary>
        private static string TokenText(TSqlFragment fragment)
        {
            if (fragment?.ScriptTokenStream == null || fragment.FirstTokenIndex < 0)
                return null;

            var sb = new StringBuilder();
            for (int i = fragment.FirstTokenIndex; i <= fragment.LastTokenIndex && i < fragment.ScriptTokenStream.Count; i++)
            {
                var token = fragment.ScriptTokenStream[i];
                if (token.TokenType == TSqlTokenType.WhiteSpace ||
                    token.TokenType == TSqlTokenType.SingleLineComment ||
                    token.TokenType == TSqlTokenType.MultilineComment)
                    continue;
                sb.Append(token.Text);
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
    }
}
