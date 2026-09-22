using SQLExtended.Cache.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Builds whole JOIN clauses — "dbo.Customer c ON c.Id = o.CustomerId" — from the foreign keys between the
/// tables already in scope and the tables next to them.
///
/// <para>This is the table-position half of the foreign-key join support. <c>BuildJoinConditionCompletion</c>
/// answers "JOIN dbo.Customer c ON &lt;here&gt;", once the table has been named and the predicate is all that
/// is left; this answers "JOIN &lt;here&gt;", where the table has not been chosen yet — so a suggestion has to
/// carry the table, an alias for it and the predicate together, as one committed string.</para>
///
/// <para>Free of the VS editor assemblies so the test project links it, the same split
/// <c>SqlIdentifierQuoting</c> exists for — and for the same reason, that every way this can be wrong is
/// silent. An inverted predicate, a duplicated alias and a mispaired composite key all read as plausible SQL
/// in the completion list, and two of the three still run.</para>
/// </summary>
internal static class JoinClauseBuilder
{
    /// <summary>Which side of the relationship the foreign key is declared on.</summary>
    internal enum FkDirection
    {
        /// <summary>Declared on the table in scope, pointing out at the candidate (Order.CustomerId → Customer).</summary>
        FromScope,

        /// <summary>Declared on the candidate, pointing back at the table in scope (OrderItem.OrderId → Order).</summary>
        ToScope
    }

    /// <summary>One foreign key tying a table already in scope to a table that could be joined to it.</summary>
    internal sealed class Relation
    {
        public AliasResolver.TableReference ScopeTable { get; set; }
        public CachedForeignKey Fk { get; set; }
        public FkDirection Direction { get; set; }
    }

    internal sealed class JoinSuggestion
    {
        public string Schema { get; set; }
        public string Table { get; set; }
        public string Alias { get; set; }

        /// <summary>The ON predicate alone, e.g. "c.Id = o.CustomerId".</summary>
        public string Predicate { get; set; }

        /// <summary>The whole clause as it is typed into the editor.</summary>
        public string InsertText { get; set; }

        /// <summary>Name of the foreign key the suggestion came from, for the item's suffix.</summary>
        public string ForeignKeyName { get; set; }
    }

    /// <summary>
    /// Turns the gathered relations into join clauses, keeping the order they arrive in.
    ///
    /// <para>The new table's alias always comes first in the predicate, in both directions —
    /// "c.Id = o.CustomerId" and "oi.OrderId = o.Id" — so the half being introduced reads on the left in
    /// every suggestion rather than swapping sides with the direction of the key.</para>
    /// </summary>
    public static IReadOnlyList<JoinSuggestion> Build(
        IReadOnlyList<Relation> relations,
        IReadOnlyList<AliasResolver.TableReference> tablesInScope,
        bool qualifyWithSchema)
    {
        var results = new List<JoinSuggestion>();
        if (relations == null || relations.Count == 0)
            return results;

        // Aliases already spoken for — both the alias and the table name of everything in scope. A generated
        // alias that collides with either produces SQL that parses and runs, with the predicate bound to the
        // wrong side of what is now an accidental self-join.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tablesInScope != null)
        {
            foreach (var t in tablesInScope)
            {
                if (!string.IsNullOrEmpty(t.Alias)) taken.Add(t.Alias);
                if (!string.IsNullOrEmpty(t.Table)) taken.Add(t.Table);
            }
        }

        // One alias per candidate *table*, not per suggestion: two foreign keys to the same table
        // (BillToAddressId and ShipToAddressId, both → Address) are alternatives, only one of which is ever
        // committed, and giving them separate aliases would imply both could be taken at once.
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rel in relations)
        {
            if (rel?.Fk == null || rel.ScopeTable == null)
                continue;

            string schema, table, newCols, scopeCols;
            if (rel.Direction == FkDirection.FromScope)
            {
                // The key is on the table in scope; the candidate is the table it references.
                schema = rel.Fk.ReferencedSchema;
                table = rel.Fk.ReferencedTable;
                newCols = rel.Fk.ReferencedColumns;
                scopeCols = rel.Fk.Columns;
            }
            else
            {
                // The key is on the candidate and points back at the table in scope.
                schema = rel.Fk.SchemaName;
                table = rel.Fk.TableName;
                newCols = rel.Fk.Columns;
                scopeCols = rel.Fk.ReferencedColumns;
            }

            if (string.IsNullOrEmpty(table))
                continue;

            string key = $"{schema}.{table}";
            if (!aliases.TryGetValue(key, out string alias))
            {
                alias = MakeAlias(table, taken);
                taken.Add(alias);
                aliases[key] = alias;
            }

            string predicate = FormatPredicate(alias, newCols, rel.ScopeTable.ReferenceName, scopeCols);
            if (string.IsNullOrEmpty(predicate))
                continue;

            string qualified = qualifyWithSchema && !string.IsNullOrEmpty(schema)
                ? $"{SqlIdentifierQuoting.QuoteIfNeeded(schema)}.{SqlIdentifierQuoting.QuoteObjectIfNeeded(table)}"
                : SqlIdentifierQuoting.QuoteObjectIfNeeded(table);

            results.Add(new JoinSuggestion
            {
                Schema = schema,
                Table = table,
                Alias = alias,
                Predicate = predicate,
                InsertText = $"{qualified} {alias} ON {predicate}",
                ForeignKeyName = rel.Fk.ForeignKeyName
            });
        }

        return results;
    }

    /// <summary>
    /// Derives a short alias from a table name — the initial of each word, so Customer → c, OrderItem → oi,
    /// ORDER_ITEM → oi — and makes it unique against <paramref name="taken"/> by appending a counter.
    ///
    /// <para>An alias that lands on a reserved word goes through the same counter rather than being
    /// bracketed: InventoryStock initials to "is", and "JOIN dbo.InventoryStock is ON ..." does not parse at
    /// all. "is2" is ugly exactly once, here; "[is]" would be ugly in every query it is pasted into.</para>
    /// </summary>
    internal static string MakeAlias(string tableName, ISet<string> taken)
    {
        string baseAlias = Initials(tableName);
        if (baseAlias.Length == 0)
            baseAlias = "t";

        string candidate = baseAlias;
        int n = 2;
        while ((taken != null && taken.Contains(candidate)) || SqlIdentifierQuoting.IsReservedKeyword(candidate))
            candidate = baseAlias + n++;

        return candidate;
    }

    /// <summary>
    /// First letter of each word in a name, lower-cased. Words break on underscores, spaces, hyphens and
    /// camel-case humps. Capped at four letters, past which an alias has stopped being an abbreviation.
    /// </summary>
    private static string Initials(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        // Brackets are part of how the name is written, never part of the alias.
        name = name.Trim().Trim('[', ']');

        var sb = new StringBuilder(4);
        bool atWordStart = true;

        for (int i = 0; i < name.Length && sb.Length < 4; i++)
        {
            char c = name[i];

            if (c == '_' || c == ' ' || c == '-' || c == '.')
            {
                atWordStart = true;
                continue;
            }

            if (!char.IsLetterOrDigit(c))
                continue;

            // A capital after a lower-case letter or a digit starts a new word (OrderItem, Address2Line).
            bool hump = i > 0 && char.IsUpper(c) && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));

            if (atWordStart || hump)
            {
                if (char.IsLetter(c))
                    sb.Append(char.ToLowerInvariant(c));
                atWordStart = false;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Pairs two comma-separated column lists positionally and combines composite keys with AND — the same
    /// shape <c>SqlCompletionSource.FormatPredicate</c> produces for the ON-clause list. Returns empty when
    /// either side names no columns, the one case that would otherwise be emitted as "a. = b.".
    /// </summary>
    internal static string FormatPredicate(string newRef, string newCols, string scopeRef, string scopeCols)
    {
        var nc = (newCols ?? string.Empty).Split(',');
        var sc = (scopeCols ?? string.Empty).Split(',');
        int n = Math.Min(nc.Length, sc.Length);

        var parts = new List<string>(n);
        for (int i = 0; i < n; i++)
        {
            string left = nc[i].Trim();
            string right = sc[i].Trim();
            if (left.Length == 0 || right.Length == 0)
                continue;

            parts.Add($"{newRef}.{SqlIdentifierQuoting.QuoteIfNeeded(left)} = {scopeRef}.{SqlIdentifierQuoting.QuoteIfNeeded(right)}");
        }

        return parts.Count > 0 ? string.Join(" AND ", parts) : string.Empty;
    }
}
