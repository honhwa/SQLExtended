using System;
using System.Collections.Generic;
using System.Text;
using SQLExtended.Cache.Models;
using SQLExtended.Formatting;
using SQLExtended.Snippets;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Expands an accepted stored-procedure completion into an EXEC parameter list — one
/// "@param = value -- TYPE" line per parameter, mirroring Red Gate SQL Prompt. Each value
/// becomes an interactive snippet tab-stop (via <see cref="SnippetSession"/>) so the user can
/// Tab through and fill them in. Comma position and indentation follow the formatter options.
/// </summary>
internal static class ProcParameterExpansion
{
    /// <summary>
    /// Property key carried on a stored-procedure <c>CompletionItem</c> holding the data needed to
    /// build the parameter expansion at commit time. Its presence marks an expandable proc item.
    /// </summary>
    internal const string InfoKey = "ProcExpansionInfo";

    /// <summary>Data attached to a proc completion item so the commit manager can build the expansion.</summary>
    internal sealed class Info
    {
        /// <summary>Text inserted for the proc name — must equal the item's insert text so the
        /// replaced span produces valid SQL (e.g. "dbo.spFoo" or just "spFoo" when the schema is typed).</summary>
        public string InsertName { get; }
        public IReadOnlyList<CachedParameter> Parameters { get; }

        public Info(string insertName, IReadOnlyList<CachedParameter> parameters)
        {
            InsertName = insertName;
            Parameters = parameters;
        }
    }

    /// <summary>
    /// Builds the tab-stop snippet for a proc's parameters, or null when the proc takes no
    /// parameters (in which case the caller should fall back to inserting just the name).
    /// </summary>
    public static SqlSnippet Build(Info info)
    {
        if (info?.Parameters == null || info.Parameters.Count == 0)
            return null;

        FormatterOptions opts;
        try { opts = FormatterOptions.Load(); }
        catch { opts = new FormatterOptions(); }

        string indent = opts.IndentStyle == IndentStyleOption.Tabs
            ? "\t"
            : new string(' ', Math.Max(1, opts.IndentSize));
        bool leadingComma = opts.CommaPosition == CommaPositionOption.LeadingComma;

        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.Append(info.InsertName);

        var pars = info.Parameters;
        for (int i = 0; i < pars.Count; i++)
        {
            var p = pars[i];

            // Each value is a tab-stop placeholder. The 'p_' prefix keeps the field name clear of the
            // built-in placeholder names ($user$, $server$, …) so it is never resolved as a system value.
            string field = FieldName(p.ParameterName, i, used);
            defaults[field] = SqlCompletionSource.DefaultValueForType(p.DataType, isNullable: true);

            string outputKw = p.IsOutput ? " OUTPUT" : "";
            string comment = FormatTypeComment(p);

            sb.AppendLine();
            sb.Append(indent);

            if (leadingComma)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"{p.ParameterName} = ${field}${outputKw} -- {comment}");
            }
            else
            {
                bool last = i == pars.Count - 1;
                sb.Append($"{p.ParameterName} = ${field}${outputKw}{(last ? "" : ",")} -- {comment}");
            }
        }

        return new SqlSnippet
        {
            Code = info.InsertName,
            Body = sb.ToString(),
            Defaults = defaults
        };
    }

    /// <summary>
    /// Derives a unique, syntactically valid tab-stop field name from a parameter name. Prefixed
    /// with "p_" to avoid colliding with system placeholder names, sanitized to identifier chars,
    /// and de-duplicated within the proc (defensive — SQL parameter names are already unique).
    /// </summary>
    private static string FieldName(string parameterName, int index, HashSet<string> used)
    {
        var sb = new StringBuilder("p_");
        foreach (char c in (parameterName ?? string.Empty).TrimStart('@'))
            sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

        string name = sb.ToString();
        if (name == "p_")
            name = "p_" + index;

        string candidate = name;
        int suffix = 1;
        while (!used.Add(candidate))
            candidate = $"{name}_{suffix++}";
        return candidate;
    }

    /// <summary>
    /// Formats the trailing type comment (e.g. "NVARCHAR(255)", "INT"). Length is shown for the
    /// character/binary types; precision/scale are not cached for parameters, so other types show
    /// their base name only.
    /// </summary>
    private static string FormatTypeComment(CachedParameter p)
    {
        string type = (p.DataType ?? string.Empty).ToLowerInvariant();
        switch (type)
        {
            case "varchar":
            case "nvarchar":
            case "char":
            case "nchar":
            case "binary":
            case "varbinary":
                string len = p.MaxLength == -1
                    ? "MAX"
                    : (type[0] == 'n' ? (p.MaxLength / 2) : p.MaxLength).ToString();
                return $"{p.DataType.ToUpperInvariant()}({len})";
            default:
                return (p.DataType ?? string.Empty).ToUpperInvariant();
        }
    }
}
