using Microsoft.SqlServer.TransactSql.ScriptDom;
using SQLExtended.IntelliSense;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SQLExtended.Qualify;

/// <summary>
/// The object a FROM item names, as the column lookup is asked for it. Server, Database and Schema are null where the
/// reference left them out; Name keeps its sigil for a temp table or table variable ("#t", "@t").
/// </summary>
internal sealed class QualifierTableName(string server, string database, string schema, string name)
{
    public string Server { get; } = server;
    public string Database { get; } = database;
    public string Schema { get; } = schema;
    public string Name { get; } = name;

    public override string ToString() => string.Join(".", new[] { Server, Database, Schema, Name }.Where(p => p != null));
}

internal sealed class ColumnQualifierResult
{
    public bool Success { get; set; }
    public string Sql { get; set; }
    public string ErrorMessage { get; set; }
    public int TablesAliased { get; set; }
    public int ColumnsQualified { get; set; }

    /// <summary>Unqualified column references left as written: not found, ambiguous, or in a scope with a source whose
    /// columns are unknown.</summary>
    public int ColumnsLeft { get; set; }
}

/// <summary>
/// Gives every table in a SELECT's FROM an alias and prefixes every column reference with the alias of the table it
/// belongs to (GitHub issue #3). The SQL is edited in place at ScriptDom's token offsets, never regenerated, so the
/// layout and comments are the user's own. See <c>Qualify/CLAUDE.md</c> for why each guard exists.
/// </summary>
internal static class ColumnQualifier
{
    /// <param name="getColumns">The column names of a named object, or null when they are not known. Null makes every
    /// unqualified column in that scope ineligible — it might belong to the unknown source.</param>
    public static ColumnQualifierResult Qualify(string sql, Func<QualifierTableName, IReadOnlyCollection<string>> getColumns)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new ColumnQualifierResult { Success = true, Sql = sql };

        var fragment = Parse(sql, out var errors);
        if (errors.Count > 0)
            return Fail($"the SQL does not parse (line {errors[0].Line}: {errors[0].Message})");

        var walker = new Walker(getColumns ?? (_ => null));
        fragment.Accept(walker);

        string output = Apply(sql, walker.Edits);
        if (output == null)
            return Fail("two edits overlapped, so nothing was changed");

        Parse(output, out var outputErrors);
        if (outputErrors.Count > 0)
            return Fail($"the result would not parse (line {outputErrors[0].Line}: {outputErrors[0].Message}), so nothing was changed");

        return new ColumnQualifierResult
        {
            Success = true,
            Sql = output,
            TablesAliased = walker.TablesAliased,
            ColumnsQualified = walker.ColumnsQualified,
            ColumnsLeft = walker.ColumnsLeft
        };
    }

    private static ColumnQualifierResult Fail(string message) => new() { Success = false, ErrorMessage = message };

    private static TSqlFragment Parse(string sql, out IList<ParseError> errors)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        using var reader = new StringReader(sql);
        return parser.Parse(reader, out errors);
    }

    private sealed class Edit(int start, int length, string text)
    {
        public int Start { get; } = start;
        public int Length { get; } = length;
        public string Text { get; } = text;
    }

    /// <summary>Applies the edits back to front. Null when two of them overlap — that would be a bug here, and guessing
    /// which one wins could corrupt the statement.</summary>
    private static string Apply(string sql, List<Edit> edits)
    {
        var sb = new StringBuilder(sql);
        int limit = int.MaxValue;
        foreach (var e in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
        {
            if (e.Start + e.Length > limit)
                return null;
            sb.Remove(e.Start, e.Length).Insert(e.Start, e.Text);
            limit = e.Start;
        }
        return sb.ToString();
    }

    // A FROM item as seen by the column references of its query.
    private sealed class ScopeTable
    {
        public SchemaObjectName Object;       // named table, view, CTE or TVF; null for derived tables, variables, rowsets
        public string ExistingAlias;
        public string ExposedName;            // what the source exposes: the alias, else the object's own name
        public string NewAlias;
        public HashSet<string> Columns;       // null = unknown

        public string ReferenceText => NewAlias ?? (ExposedName == null ? null : SqlIdentifierQuoting.QuoteObjectIfNeeded(ExposedName));
        public string VisibleName => NewAlias ?? ExposedName;
    }

    private sealed class Scope
    {
        public Scope Parent;
        public QuerySpecification Spec;
        public List<ScopeTable> Tables = new();
        public HashSet<string> SelectAliases = new(StringComparer.OrdinalIgnoreCase);
        public bool InOrderBy;
    }

    private sealed class Walker(Func<QualifierTableName, IReadOnlyCollection<string>> getColumns) : TSqlFragmentVisitor
    {
        // The datepart argument of these is parsed as a column reference (DATEADD(day, …) reads "day" as a column), and
        // a table with a column called Day or Year would otherwise get DATEADD(o.Day, …).
        private static readonly HashSet<string> DatePartFunctions = new(StringComparer.OrdinalIgnoreCase)
        {
            "DATEADD", "DATEDIFF", "DATEDIFF_BIG", "DATEPART", "DATENAME", "DATETRUNC", "DATE_BUCKET"
        };

        public List<Edit> Edits { get; } = new();
        public int TablesAliased { get; private set; }
        public int ColumnsQualified { get; private set; }
        public int ColumnsLeft { get; private set; }

        private readonly HashSet<TSqlFragment> _skip = new();
        private readonly HashSet<OrderByClause> _setOrderBys = new();
        private readonly HashSet<QueryDerivedTable> _lateral = new();
        private Dictionary<string, CommonTableExpression> _ctes = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _statementNames;
        private HashSet<string> _taken;
        private Scope _current;
        private int _depth;

        // --- Statements: CTE visibility and the names an alias must avoid ---

        public override void ExplicitVisit(SelectStatement node) => InStatement(node, () => base.ExplicitVisit(node));
        public override void ExplicitVisit(InsertStatement node) => InStatement(node, () => base.ExplicitVisit(node));
        public override void ExplicitVisit(UpdateStatement node) => InStatement(node, () => base.ExplicitVisit(node));
        public override void ExplicitVisit(DeleteStatement node) => InStatement(node, () => base.ExplicitVisit(node));
        public override void ExplicitVisit(MergeStatement node) => InStatement(node, () => base.ExplicitVisit(node));

        private void InStatement(StatementWithCtesAndXmlNamespaces node, Action visit)
        {
            var savedCtes = _ctes;
            var savedNames = _statementNames;
            _ctes = new Dictionary<string, CommonTableExpression>(StringComparer.OrdinalIgnoreCase);
            foreach (var cte in node.WithCtesAndXmlNamespaces?.CommonTableExpressions ?? Enumerable.Empty<CommonTableExpression>())
                if (cte.ExpressionName?.Value is { } name)
                    _ctes[name] = cte;
            _statementNames = NameCollector.Collect(node);
            visit();
            _ctes = savedCtes;
            _statementNames = savedNames;
        }

        // A CTE body sees none of the query that uses it, and its aliases are its own.
        public override void ExplicitVisit(CommonTableExpression node)
        {
            var (current, depth, taken) = (_current, _depth, _taken);
            (_current, _depth) = (null, 0);
            base.ExplicitVisit(node);
            (_current, _depth, _taken) = (current, depth, taken);
        }

        // --- Query scopes ---

        public override void ExplicitVisit(QuerySpecification node)
        {
            bool isRoot = _depth == 0;
            var savedTaken = _taken;
            if (isRoot)
            {
                // Everything named anywhere in this query tree or its statement: a new alias that matched one would
                // capture references meant for the other.
                _taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_statementNames != null)
                    _taken.UnionWith(_statementNames);
                _taken.UnionWith(NameCollector.Collect(node));
            }

            _depth++;
            var saved = _current;
            _current = BuildScope(node, saved);
            base.ExplicitVisit(node);
            _current = saved;
            _depth--;
            if (isRoot)
                _taken = savedTaken;
        }

        // A derived table cannot see the other tables of the FROM it sits in — only an APPLY's right side can.
        public override void ExplicitVisit(QueryDerivedTable node)
        {
            var saved = _current;
            if (!_lateral.Contains(node))
                _current = _current?.Parent;
            base.ExplicitVisit(node);
            _current = saved;
        }

        // ORDER BY of a UNION names the union's output columns, not any branch's tables.
        public override void ExplicitVisit(BinaryQueryExpression node)
        {
            if (node.OrderByClause != null)
                _setOrderBys.Add(node.OrderByClause);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(QueryParenthesisExpression node)
        {
            if (node.OrderByClause != null)
                _setOrderBys.Add(node.OrderByClause);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(OrderByClause node)
        {
            if (_current != null && ReferenceEquals(node, _current.Spec.OrderByClause))
            {
                _current.InOrderBy = true;
                base.ExplicitVisit(node);
                _current.InOrderBy = false;
            }
            else if (_setOrderBys.Contains(node))
            {
                var saved = _current;
                _current = null;
                base.ExplicitVisit(node);
                _current = saved;
            }
            else
            {
                base.ExplicitVisit(node);   // OVER (ORDER BY …), WITHIN GROUP (ORDER BY …): ordinary column positions
            }
        }

        // --- Positions where a column reference may not carry a qualifier ---

        public override void ExplicitVisit(FunctionCall node)
        {
            if (DatePartFunctions.Contains(node.FunctionName?.Value ?? "") && node.Parameters.Count > 0 &&
                node.Parameters[0] is ColumnReferenceExpression { MultiPartIdentifier.Count: 1 } datePart)
                _skip.Add(datePart);
            base.ExplicitVisit(node);
        }

        // CONTAINSTABLE(T, (Col1, Col2), …) takes bare column names of T; so, to be safe, does CONTAINS(Col, …).
        public override void ExplicitVisit(FullTextTableReference node)
        {
            _skip.UnionWith(node.Columns);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(SemanticTableReference node)
        {
            _skip.UnionWith(node.Columns);
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(FullTextPredicate node)
        {
            _skip.UnionWith(node.Columns);
            base.ExplicitVisit(node);
        }

        // --- The rewrites ---

        public override void ExplicitVisit(ColumnReferenceExpression node)
        {
            if (_current != null && node.ColumnType == ColumnType.Regular && node.MultiPartIdentifier?.Count > 0 && !_skip.Contains(node))
            {
                var ids = node.MultiPartIdentifier.Identifiers;
                if (ids.Count == 1)
                    QualifyBare(ids[0]);
                else
                    RewriteQualifier(ids, ids.Count - 1);
            }
            base.ExplicitVisit(node);
        }

        // Person.* → p.*
        public override void ExplicitVisit(SelectStarExpression node)
        {
            if (_current != null && node.Qualifier?.Count > 0)
                RewriteQualifier(node.Qualifier.Identifiers, node.Qualifier.Count);
            base.ExplicitVisit(node);
        }

        /// <summary>
        /// Resolves an unqualified column the way SQL Server does — innermost scope first, outward only when no table
        /// there has it — and prefixes it only when exactly one table can be proved to own it.
        /// </summary>
        private void QualifyBare(Identifier column)
        {
            string name = column.Value;

            // ORDER BY may name a select-list alias, and the alias wins over a column of the same name.
            if (_current.InOrderBy && _current.SelectAliases.Contains(name))
            {
                ColumnsLeft++;
                return;
            }

            for (var scope = _current; scope != null; scope = scope.Parent)
            {
                if (scope.Tables.Any(t => t.Columns == null))
                    break;

                var owners = scope.Tables.Where(t => t.Columns.Contains(name)).ToList();
                if (owners.Count > 1)
                    break;
                if (owners.Count == 0)
                    continue;

                var owner = owners[0];
                if (owner.ReferenceText == null || IsShadowed(owner.VisibleName, scope))
                    break;

                Edits.Add(new Edit(column.StartOffset, 0, owner.ReferenceText + "."));
                ColumnsQualified++;
                return;
            }

            ColumnsLeft++;
        }

        /// <summary>True when a scope inside <paramref name="owningScope"/> exposes the same name, so the prefix would
        /// bind to that table instead.</summary>
        private bool IsShadowed(string name, Scope owningScope)
        {
            for (var scope = _current; scope != null && scope != owningScope; scope = scope.Parent)
                if (scope.Tables.Any(t => string.Equals(t.VisibleName, name, StringComparison.OrdinalIgnoreCase)))
                    return true;
            return false;
        }

        /// <summary>
        /// A qualifier that names a table by its own name (Person.AddressId, dbo.Person.AddressId) is replaced with the
        /// alias that table has just been given; once aliased, the table's name no longer resolves.
        /// </summary>
        private void RewriteQualifier(IList<Identifier> ids, int qualifierCount)
        {
            var qualifier = ids.Take(qualifierCount).Select(i => i.Value).ToList();
            for (var scope = _current; scope != null; scope = scope.Parent)
            {
                var matches = scope.Tables.Where(t => QualifierMatches(t, qualifier)).ToList();
                if (matches.Count > 1)
                    return;
                if (matches.Count == 0)
                    continue;

                if (matches[0].NewAlias is { } alias)
                {
                    var last = ids[qualifierCount - 1];
                    int start = ids[0].StartOffset;
                    Edits.Add(new Edit(start, last.StartOffset + last.FragmentLength - start, alias));
                }
                return;
            }
        }

        private static bool QualifierMatches(ScopeTable table, List<string> qualifier)
        {
            if (table.ExistingAlias != null || table.Object == null)
                return qualifier.Count == 1 && string.Equals(qualifier[0], table.ExposedName, StringComparison.OrdinalIgnoreCase);

            // Compare from the right: [db.][schema.]table against the reference's own parts. A part the FROM left out
            // matches anything (FROM Person … dbo.Person.Id), a part the qualifier left out is not compared.
            var parts = new[] { table.Object.DatabaseIdentifier?.Value, table.Object.SchemaIdentifier?.Value, table.Object.BaseIdentifier?.Value };
            if (qualifier.Count > parts.Length)
                return false;
            for (int i = 0; i < qualifier.Count; i++)
            {
                string part = parts[parts.Length - 1 - i];
                string written = qualifier[qualifier.Count - 1 - i];
                if (i == 0 ? !string.Equals(part, written, StringComparison.OrdinalIgnoreCase)
                           : part != null && !string.Equals(part, written, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        // --- Building a scope ---

        private Scope BuildScope(QuerySpecification spec, Scope parent)
        {
            var scope = new Scope { Parent = parent, Spec = spec };

            foreach (var element in spec.SelectElements.OfType<SelectScalarExpression>())
                if (element.ColumnName?.Value is { } alias)
                    scope.SelectAliases.Add(alias);

            foreach (var reference in spec.FromClause?.TableReferences ?? Enumerable.Empty<TableReference>())
                Collect(reference, scope.Tables);

            return scope;
        }

        private void Collect(TableReference reference, List<ScopeTable> tables)
        {
            switch (reference)
            {
                case UnqualifiedJoin apply when apply.UnqualifiedJoinType is UnqualifiedJoinType.CrossApply or UnqualifiedJoinType.OuterApply:
                    Collect(apply.FirstTableReference, tables);
                    if (apply.SecondTableReference is QueryDerivedTable lateral)
                        _lateral.Add(lateral);
                    Collect(apply.SecondTableReference, tables);
                    break;

                case JoinTableReference join:
                    Collect(join.FirstTableReference, tables);
                    Collect(join.SecondTableReference, tables);
                    break;

                case JoinParenthesisTableReference parens:
                    Collect(parens.Join, tables);
                    break;

                case NamedTableReference named when named.SchemaObject?.BaseIdentifier != null:
                {
                    var table = new ScopeTable
                    {
                        Object = named.SchemaObject,
                        ExistingAlias = named.Alias?.Value,
                        ExposedName = named.Alias?.Value ?? named.SchemaObject.BaseIdentifier.Value,
                        Columns = ColumnsOf(named.SchemaObject)
                    };
                    // The alias goes after FOR SYSTEM_TIME … but before TABLESAMPLE and WITH (hints).
                    if (table.ExistingAlias == null)
                        AddAlias(table, (TSqlFragment)named.TemporalClause ?? named.SchemaObject);
                    tables.Add(table);
                    break;
                }

                case SchemaObjectFunctionTableReference tvf when tvf.SchemaObject?.BaseIdentifier != null:
                {
                    var table = new ScopeTable
                    {
                        Object = tvf.SchemaObject,
                        ExistingAlias = tvf.Alias?.Value,
                        ExposedName = tvf.Alias?.Value ?? tvf.SchemaObject.BaseIdentifier.Value,
                        Columns = tvf.Columns.Count > 0 ? NameSet(tvf.Columns.Select(c => c.Value)) : Lookup(tvf.SchemaObject)
                    };
                    if (table.ExistingAlias == null)
                        AddAlias(table, tvf);
                    tables.Add(table);
                    break;
                }

                case VariableTableReference variable when variable.Variable?.Name != null:
                {
                    // @tv.Col does not parse, so a table variable's columns can only be qualified through an alias.
                    var table = new ScopeTable
                    {
                        ExistingAlias = variable.Alias?.Value,
                        ExposedName = variable.Alias?.Value ?? variable.Variable.Name,
                        Columns = NameSet(getColumns(new QualifierTableName(null, null, null, variable.Variable.Name)))
                    };
                    if (table.ExistingAlias == null)
                        AddAlias(table, variable.Variable);
                    tables.Add(table);
                    break;
                }

                case QueryDerivedTable derived:
                    tables.Add(new ScopeTable
                    {
                        ExistingAlias = derived.Alias?.Value,
                        ExposedName = derived.Alias?.Value,
                        Columns = derived.Columns.Count > 0 ? NameSet(derived.Columns.Select(c => c.Value)) : OutputColumns(derived.QueryExpression)
                    });
                    break;

                case InlineDerivedTable values:
                    tables.Add(new ScopeTable
                    {
                        ExistingAlias = values.Alias?.Value,
                        ExposedName = values.Alias?.Value,
                        Columns = values.Columns.Count > 0 ? NameSet(values.Columns.Select(c => c.Value)) : null
                    });
                    break;

                // OPENJSON, OPENQUERY, PIVOT, CONTAINSTABLE …: a name, but no columns we can vouch for.
                case TableReferenceWithAlias other:
                    tables.Add(new ScopeTable { ExistingAlias = other.Alias?.Value, ExposedName = other.Alias?.Value });
                    break;

                default:
                    tables.Add(new ScopeTable());
                    break;
            }
        }

        private void AddAlias(ScopeTable table, TSqlFragment after)
        {
            table.NewAlias = JoinClauseBuilder.MakeAlias(table.ExposedName, _taken);
            _taken.Add(table.NewAlias);
            Edits.Add(new Edit(after.StartOffset + after.FragmentLength, 0, " " + table.NewAlias));
            TablesAliased++;
        }

        private HashSet<string> ColumnsOf(SchemaObjectName obj)
        {
            // A one-part name may be a CTE of this statement, which hides any table of the same name.
            if (obj.SchemaIdentifier == null && obj.DatabaseIdentifier == null && obj.ServerIdentifier == null &&
                _ctes.TryGetValue(obj.BaseIdentifier.Value, out var cte))
                return cte.Columns.Count > 0 ? NameSet(cte.Columns.Select(c => c.Value)) : OutputColumns(cte.QueryExpression);

            return Lookup(obj);
        }

        private HashSet<string> Lookup(SchemaObjectName obj) =>
            NameSet(getColumns(new QualifierTableName(obj.ServerIdentifier?.Value, obj.DatabaseIdentifier?.Value, obj.SchemaIdentifier?.Value, obj.BaseIdentifier.Value)));

        private static HashSet<string> NameSet(IEnumerable<string> names) =>
            names == null ? null : new HashSet<string>(names.Where(n => !string.IsNullOrEmpty(n)), StringComparer.OrdinalIgnoreCase) is { Count: > 0 } set ? set : null;

        /// <summary>The column names a query produces — what a derived table or CTE exposes. Null when a <c>*</c> makes
        /// them unknowable from the text.</summary>
        private static HashSet<string> OutputColumns(QueryExpression query)
        {
            while (query is not QuerySpecification)
            {
                query = query switch
                {
                    BinaryQueryExpression binary => binary.FirstQueryExpression,
                    QueryParenthesisExpression parens => parens.QueryExpression,
                    _ => null
                };
                if (query == null)
                    return null;
            }

            var names = new List<string>();
            foreach (var element in ((QuerySpecification)query).SelectElements)
            {
                switch (element)
                {
                    case SelectStarExpression:
                        return null;
                    case SelectScalarExpression scalar:
                        names.Add(scalar.ColumnName?.Value ?? (scalar.Expression as ColumnReferenceExpression)?.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value);
                        break;
                }
            }
            return NameSet(names);
        }
    }

    /// <summary>Every table name, alias and CTE name under a fragment.</summary>
    private sealed class NameCollector : TSqlFragmentVisitor
    {
        private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

        public static HashSet<string> Collect(TSqlFragment fragment)
        {
            var collector = new NameCollector();
            fragment.Accept(collector);
            return collector._names;
        }

        public override void Visit(NamedTableReference node) => Add(node.SchemaObject?.BaseIdentifier?.Value);
        public override void Visit(SchemaObjectFunctionTableReference node) => Add(node.SchemaObject?.BaseIdentifier?.Value);
        public override void Visit(TableReferenceWithAlias node) => Add(node.Alias?.Value);
        public override void Visit(CommonTableExpression node) => Add(node.ExpressionName?.Value);

        private void Add(string name)
        {
            if (!string.IsNullOrEmpty(name))
                _names.Add(name);
        }
    }
}
