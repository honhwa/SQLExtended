using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SQLExtended.Validation;

/// <summary>
/// Parses a module definition (the body of a view / procedure / function / trigger) and collects
/// every name that is defined <em>locally</em> within it — common table expressions, derived-table
/// and table aliases, table variables and temp tables.
///
/// These names legitimately appear in <c>sys.sql_expression_dependencies</c> as unbound, unqualified
/// references (referenced_id NULL, no schema), yet they are not real schema objects. A dependency
/// whose unqualified name matches one of them is therefore NOT a broken reference and must not be
/// reported — which is the only reliable way to tell a genuine missing table (e.g. <c>FROM badtable</c>)
/// apart from a table alias (e.g. <c>FROM Orders o … o.Id</c>), since the dependency rows are identical.
/// </summary>
internal static class ModuleLocalNameScanner
{
    /// <summary>Above this size we skip parsing — a multi-megabyte module isn't worth the stall.</summary>
    private const int MaxParseLength = 1_000_000;

    /// <summary>
    /// Returns the case-insensitive set of locally-defined names in <paramref name="definition"/>.
    /// Returns an empty set on missing input or any parse failure, so the caller falls back to
    /// reporting the reference (a missed suppression is safer than a missed broken reference).
    /// </summary>
    public static ISet<string> Scan(string definition)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(definition) || definition.Length > MaxParseLength)
            return names;

        try
        {
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(definition);
            var fragment = parser.Parse(reader, out _);
            fragment?.Accept(new LocalNameVisitor(names));
        }
        catch
        {
            // ScriptDom usually yields a partial AST even on error; keep whatever was collected.
        }
        return names;
    }

    private sealed class LocalNameVisitor : TSqlFragmentVisitor
    {
        private readonly ISet<string> _names;
        public LocalNameVisitor(ISet<string> names) => _names = names;

        // sys.sql_expression_dependencies stores the bare name, so also record the de-sigilled form
        // of @table-variables and #temp-tables alongside the literal token.
        private void Add(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            _names.Add(name);
            if (name[0] == '@' || name[0] == '#')
            {
                string bare = name.TrimStart('@', '#');
                if (bare.Length > 0) _names.Add(bare);
            }
        }

        public override void Visit(NamedTableReference node) => Add(node.Alias?.Value);
        public override void Visit(QueryDerivedTable node) => Add(node.Alias?.Value);
        public override void Visit(SchemaObjectFunctionTableReference node) => Add(node.Alias?.Value);

        public override void Visit(VariableTableReference node)
        {
            Add(node.Alias?.Value);
            Add(node.Variable?.Name);
        }

        public override void Visit(CommonTableExpression node) => Add(node.ExpressionName?.Value);
        public override void Visit(DeclareTableVariableStatement node) => Add(node.Body?.VariableName?.Value);
        public override void Visit(CreateTableStatement node) => Add(node.SchemaObjectName?.BaseIdentifier?.Value);
    }
}
