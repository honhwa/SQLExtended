using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SQLExtended.Safety;

/// <summary>One UPDATE or DELETE that would touch every row of its target.</summary>
internal sealed class UnsafeDmlFinding
{
    public string Kind { get; set; }    // "UPDATE" or "DELETE"
    public string Target { get; set; }
    public int Line { get; set; }
    public string Snippet { get; set; }
}

internal sealed class UnsafeDmlResult
{
    public List<UnsafeDmlFinding> Findings { get; } = new();

    /// <summary>
    /// The script did not parse, so nothing was checked. The guard lets it through — the server reports the
    /// syntax error itself — but this is also what SQLCMD-mode scripts (<c>:setvar</c>, <c>$(var)</c>) look like.
    /// </summary>
    public bool ParseFailed { get; set; }
}

/// <summary>
/// Finds UPDATE and DELETE statements with no WHERE clause in the text about to be executed.
///
/// <para>Free of VS, WPF and SqlClient so the test project links it. Deliberately not flagged, because each one
/// either cannot touch every row or is not executed by running the script:</para>
/// <list type="bullet">
///   <item>targets that are table variables or <c>#temp</c> tables — emptying scratch data is routine;</item>
///   <item>a target that is one side of an INNER JOIN whose ON names it — the join restricts its rows (LEFT, CROSS and
///   comma joins do not, nor does an inner join between other tables, nor <c>ON 1 = 1</c>);</item>
///   <item><c>WHERE CURRENT OF cursor</c> — that is a WHERE clause;</item>
///   <item>statements inside CREATE/ALTER PROCEDURE, FUNCTION or TRIGGER — running the script defines them, it does not run them.</item>
/// </list>
/// TOP (n) is still flagged: <c>DELETE TOP (1000) FROM t</c> in a loop is a purge of the whole table.
/// </summary>
internal static class UnsafeDmlAnalyzer
{
    public static UnsafeDmlResult Analyze(string sql)
    {
        var result = new UnsafeDmlResult();
        if (string.IsNullOrWhiteSpace(sql)) return result;

        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        TSqlFragment fragment;
        IList<ParseError> errors;
        using (var reader = new StringReader(sql))
            fragment = parser.Parse(reader, out errors);

        if (errors.Count > 0 || fragment is not TSqlScript script)
        {
            result.ParseFailed = true;
            return result;
        }

        var visitor = new Visitor(sql, result.Findings);
        foreach (var statement in script.Batches.SelectMany(b => b.Statements))
        {
            // A module body is defined by the script, not executed by it. Procedures and functions share a base;
            // triggers do not.
            if (statement is ProcedureStatementBodyBase or TriggerStatementBody) continue;
            statement.Accept(visitor);
        }
        return result;
    }

    private sealed class Visitor(string sql, List<UnsafeDmlFinding> findings) : TSqlFragmentVisitor
    {
        public override void Visit(UpdateSpecification node) => Check("UPDATE", node, node.Target, node.WhereClause, node.FromClause);

        public override void Visit(DeleteSpecification node) => Check("DELETE", node, node.Target, node.WhereClause, node.FromClause);

        private void Check(string kind, TSqlFragment node, TableReference target, WhereClause where, FromClause from)
        {
            if (where != null) return;
            if (IsScratchTable(target)) return;
            if (IsRestrictedByInnerJoin(target, from)) return;

            findings.Add(new UnsafeDmlFinding { Kind = kind, Target = TargetName(target), Line = node.StartLine, Snippet = Snippet(node) });
        }

        private static bool IsScratchTable(TableReference target) => target switch
        {
            VariableTableReference => true,
            NamedTableReference n => n.SchemaObject?.BaseIdentifier?.Value?.StartsWith("#") == true,
            _ => false
        };

        /// <summary>
        /// Whether the target's own rows are cut down by an INNER JOIN: the target appears in the FROM clause, sits on
        /// one side of an inner join, and that join's ON names it. An inner join anywhere else does not restrict it —
        /// "UPDATE dbo.A SET … FROM dbo.B b JOIN dbo.C c ON …" pairs every row of A with the join, and so does
        /// "ON 1 = 1". An unqualified column in the ON could be the target's, so it is given the benefit of the doubt.
        /// </summary>
        private static bool IsRestrictedByInnerJoin(TableReference target, FromClause from)
        {
            if (from == null || target is not NamedTableReference t) return false;

            var refs = new List<NamedTableReference>();
            from.Accept(new Collector<NamedTableReference>(refs));
            var match = refs.FirstOrDefault(r => Names(t, r));
            if (match == null) return false;

            string exposed = (match.Alias ?? match.SchemaObject.BaseIdentifier).Value;
            return from.TableReferences.Any(r => RestrictsByInnerJoin(r, match, exposed));
        }

        /// <summary>
        /// Whether the DML target names this FROM entry: "UPDATE c … FROM dbo.T c" by its alias, or "UPDATE dbo.T … FROM
        /// dbo.T" by an unaliased name. "UPDATE dbo.T … FROM dbo.T x" is a second copy of T, not the same reference.
        /// </summary>
        private static bool Names(NamedTableReference target, NamedTableReference candidate)
        {
            var tn = target.SchemaObject;
            if (candidate.Alias != null)
                return tn.Identifiers.Count == 1 && Same(tn.BaseIdentifier, candidate.Alias);

            var cn = candidate.SchemaObject;
            return Same(tn.BaseIdentifier, cn.BaseIdentifier)
                && (tn.SchemaIdentifier == null || cn.SchemaIdentifier == null || Same(tn.SchemaIdentifier, cn.SchemaIdentifier));
        }

        private static bool RestrictsByInnerJoin(TableReference reference, NamedTableReference target, string exposed) => reference switch
        {
            QualifiedJoin q => (q.QualifiedJoinType == QualifiedJoinType.Inner
                                && (Contains(q.FirstTableReference, target) || Contains(q.SecondTableReference, target))
                                && OnNames(q.SearchCondition, exposed))
                               || RestrictsByInnerJoin(q.FirstTableReference, target, exposed) || RestrictsByInnerJoin(q.SecondTableReference, target, exposed),
            JoinParenthesisTableReference p => RestrictsByInnerJoin(p.Join, target, exposed),
            UnqualifiedJoin u => RestrictsByInnerJoin(u.FirstTableReference, target, exposed) || RestrictsByInnerJoin(u.SecondTableReference, target, exposed),
            _ => false
        };

        private static bool Contains(TableReference subtree, NamedTableReference target)
        {
            var refs = new List<NamedTableReference>();
            subtree.Accept(new Collector<NamedTableReference>(refs));
            return refs.Contains(target);
        }

        private static bool OnNames(BooleanExpression on, string exposed)
        {
            if (on == null) return false;
            var columns = new List<ColumnReferenceExpression>();
            on.Accept(new Collector<ColumnReferenceExpression>(columns));
            return columns.Any(c => c.MultiPartIdentifier is { } id
                && (id.Identifiers.Count == 1 || string.Equals(id.Identifiers[id.Identifiers.Count - 2].Value, exposed, System.StringComparison.OrdinalIgnoreCase)));
        }

        private static bool Same(Identifier a, Identifier b) => string.Equals(a?.Value, b?.Value, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Every node of type <typeparamref name="T"/> under the fragment it is accepted by.</summary>
        private sealed class Collector<T>(List<T> found) : TSqlFragmentVisitor where T : TSqlFragment
        {
            public override void Visit(TSqlFragment node)
            {
                if (node is T match) found.Add(match);
            }
        }

        private static string TargetName(TableReference target) => target switch
        {
            NamedTableReference n => string.Join(".", n.SchemaObject.Identifiers.Select(i => i.Value)),
            VariableTableReference v => v.Variable.Name,
            _ => "(target)"
        };

        private string Snippet(TSqlFragment node)
        {
            if (node.StartOffset < 0 || node.FragmentLength <= 0 || node.StartOffset + node.FragmentLength > sql.Length) return "";
            string text = Regex.Replace(sql.Substring(node.StartOffset, node.FragmentLength), @"\s+", " ").Trim();
            return text.Length > 100 ? text.Substring(0, 100) + "…" : text;
        }
    }
}
