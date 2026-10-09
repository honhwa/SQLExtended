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
///   <item>a FROM clause with an INNER JOIN — the join restricts the rows (LEFT, CROSS and comma joins do not);</item>
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
            if (from != null && from.TableReferences.Any(HasInnerJoin)) return;

            findings.Add(new UnsafeDmlFinding { Kind = kind, Target = TargetName(target), Line = node.StartLine, Snippet = Snippet(node) });
        }

        private static bool IsScratchTable(TableReference target) => target switch
        {
            VariableTableReference => true,
            NamedTableReference n => n.SchemaObject?.BaseIdentifier?.Value?.StartsWith("#") == true,
            _ => false
        };

        private static bool HasInnerJoin(TableReference reference) => reference switch
        {
            QualifiedJoin q => q.QualifiedJoinType == QualifiedJoinType.Inner || HasInnerJoin(q.FirstTableReference) || HasInnerJoin(q.SecondTableReference),
            JoinParenthesisTableReference p => HasInnerJoin(p.Join),
            UnqualifiedJoin u => HasInnerJoin(u.FirstTableReference) || HasInnerJoin(u.SecondTableReference),
            _ => false
        };

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
