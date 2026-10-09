using SQLExtended.Cache.Models;
using SQLExtended.Formatting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Builds the "(all columns) SET col = val" template offered after "UPDATE dbo.T ": every column that can be
/// assigned, each set to a placeholder for its type, and a WHERE on the primary key.
///
/// <para>The WHERE is the point of it. A template that stopped after the SET list would hand the user an UPDATE
/// that rewrites every row the moment it runs, and the placeholders make that a plausible-looking statement.
/// The key columns are left out of the SET (a key is what identifies the row, not what is changed on it) and
/// go into the WHERE instead; a table with no primary key gets <c>WHERE 1 = 0</c>, which updates nothing until
/// the user writes a real predicate.</para>
///
/// <para>Free of the VS editor assemblies so the test project links it, the same split <c>JoinClauseBuilder</c>
/// exists for. The placeholder values come from the caller (<c>SqlCompletionSource.DefaultValueFor</c>), the
/// same ones the INSERT template uses.</para>
/// </summary>
internal static class UpdateTemplateBuilder
{
    /// <summary>
    /// Whether a column may appear on the left of SET. Identity, computed and rowversion columns may not —
    /// each fails the whole statement, not just its own line.
    /// </summary>
    internal static bool IsSettable(CachedColumn column) =>
        !column.IsIdentity && !column.IsComputed && !IsRowVersion(column.DataType);

    /// <summary>
    /// The template text, starting at "SET", or null when nothing outside the key can be set. Key columns are
    /// matched by name, in key order, so a composite key reads in the order its index declares.
    /// </summary>
    internal static string Build(
        IReadOnlyList<CachedColumn> columns, IReadOnlyList<string> keyColumns, Func<CachedColumn, string> valueFor,
        string indent, bool leadingComma, CasingOption keywordCase)
    {
        if (columns == null || columns.Count == 0)
            return null;

        var keys = (keyColumns ?? Array.Empty<string>())
            .Select(k => columns.FirstOrDefault(c => string.Equals(c.ColumnName, k, StringComparison.OrdinalIgnoreCase)))
            .Where(c => c != null)
            .ToList();
        var keyNames = new HashSet<string>(keys.Select(k => k.ColumnName), StringComparer.OrdinalIgnoreCase);

        var settable = columns.Where(c => IsSettable(c) && !keyNames.Contains(c.ColumnName)).ToList();
        if (settable.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine(Case("SET", keywordCase));

        for (int i = 0; i < settable.Count; i++)
        {
            sb.Append(indent);
            if (leadingComma && i > 0)
                sb.Append(", ");
            sb.Append(SqlIdentifierQuoting.QuoteIfNeeded(settable[i].ColumnName)).Append(" = ").Append(valueFor(settable[i]));
            if (!leadingComma && i < settable.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }

        sb.Append(Case("WHERE", keywordCase)).Append(' ');
        sb.Append(keys.Count == 0
            ? "1 = 0"
            : string.Join($" {Case("AND", keywordCase)} ", keys.Select(k => $"{SqlIdentifierQuoting.QuoteIfNeeded(k.ColumnName)} = {valueFor(k)}")));

        return sb.ToString();
    }

    private static bool IsRowVersion(string dataType)
    {
        string type = (dataType ?? string.Empty).Trim();
        return string.Equals(type, "timestamp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "rowversion", StringComparison.OrdinalIgnoreCase);
    }

    private static string Case(string keyword, CasingOption casing) => casing switch
    {
        CasingOption.Upper => keyword.ToUpperInvariant(),
        CasingOption.Lower => keyword.ToLowerInvariant(),
        _ => keyword
    };
}
