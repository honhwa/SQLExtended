using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SQLExtended.ResultsGrid;

/// <summary>
/// One result set captured from an SSMS results grid. Cell values are the grid's display strings;
/// a null entry means SQL NULL.
/// </summary>
public sealed class ResultGridData
{
    public string[] ColumnNames { get; set; }

    /// <summary>
    /// Formatted SQL type per column (e.g. "nvarchar(50)"). Entries may be null/empty when SSMS
    /// internals were unavailable — the generator then infers a type from the data.
    /// </summary>
    public string[] SqlTypes { get; set; }

    public List<string[]> Rows { get; } = new();
}

/// <summary>
/// Generates a "CREATE TABLE #Results + INSERT" script from grid result sets, so users can
/// re-materialize query output as a temp table (like SSMS Tools Pack's "Script Grid Results").
/// </summary>
public static class InsertScriptGenerator
{
    private const int MaxRowsPerInsert = 1000; // T-SQL row-constructor limit

    private static readonly HashSet<string> NumericTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "tinyint", "smallint", "int", "bigint", "bit", "decimal", "numeric", "money", "smallmoney", "float", "real"
    };

    // Types the grid displays as 0x… hex, which insert correctly as raw binary literals.
    private static readonly HashSet<string> BinaryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "binary", "varbinary", "timestamp", "rowversion", "image", "hierarchyid", "geometry", "geography"
    };

    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-dd"
    };

    public static string Generate(IReadOnlyList<ResultGridData> resultSets)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < resultSets.Count; i++)
        {
            if (i > 0) sb.AppendLine();
            string table = resultSets.Count > 1 ? $"#Results{i + 1}" : "#Results";
            AppendResultSet(sb, resultSets[i], table);
        }
        return sb.ToString();
    }

    private static void AppendResultSet(StringBuilder sb, ResultGridData data, string tableName)
    {
        string[] names = SanitizeColumnNames(data);
        int cols = names.Length;
        var types = new string[cols];
        for (int c = 0; c < cols; c++)
        {
            string provided = data.SqlTypes != null && c < data.SqlTypes.Length ? data.SqlTypes[c] : null;
            types[c] = string.IsNullOrWhiteSpace(provided) ? InferType(data.Rows, c) : NormalizeType(provided.Trim());
        }

        sb.AppendLine($"IF OBJECT_ID('tempdb..{tableName}') IS NOT NULL DROP TABLE {tableName};");
        sb.AppendLine();
        sb.AppendLine($"CREATE TABLE {tableName} (");
        for (int c = 0; c < cols; c++)
            sb.AppendLine($"    [{Escape(names[c])}] {types[c]} NULL{(c < cols - 1 ? "," : "")}");
        sb.AppendLine(");");

        if (data.Rows.Count == 0 || cols == 0)
            return;

        string columnList = string.Join(", ", names.Select(n => $"[{Escape(n)}]"));
        for (int start = 0; start < data.Rows.Count; start += MaxRowsPerInsert)
        {
            int count = Math.Min(MaxRowsPerInsert, data.Rows.Count - start);
            sb.AppendLine();
            sb.AppendLine($"INSERT INTO {tableName} ({columnList})");
            for (int r = 0; r < count; r++)
            {
                sb.Append(r == 0 ? "VALUES (" : "       (");
                string[] row = data.Rows[start + r];
                for (int c = 0; c < cols; c++)
                {
                    if (c > 0) sb.Append(", ");
                    sb.Append(FormatValue(c < row.Length ? row[c] : null, types[c]));
                }
                sb.AppendLine(r < count - 1 ? ")," : ");");
            }
        }
    }

    /// <summary>Replaces empty/placeholder names with ColumnN and dedupes repeats so the CREATE TABLE is valid.</summary>
    private static string[] SanitizeColumnNames(ResultGridData data)
    {
        int cols = data.ColumnNames?.Length ?? data.Rows.FirstOrDefault()?.Length ?? 0;
        var result = new string[cols];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int c = 0; c < cols; c++)
        {
            string name = data.ColumnNames != null && c < data.ColumnNames.Length ? data.ColumnNames[c] : null;
            if (string.IsNullOrWhiteSpace(name) || name == "(No column name)")
                name = $"Column{c + 1}";
            string unique = name;
            for (int suffix = 2; !seen.Add(unique); suffix++)
                unique = $"{name}_{suffix}";
            result[c] = unique;
        }
        return result;
    }

    private static string Escape(string name) => name.Replace("]", "]]");

    /// <summary>Explicit values can't be inserted into rowversion columns, so degrade to binary(8).</summary>
    private static string NormalizeType(string sqlType)
    {
        string baseType = BaseTypeName(sqlType);
        if (baseType.Equals("timestamp", StringComparison.OrdinalIgnoreCase) || baseType.Equals("rowversion", StringComparison.OrdinalIgnoreCase))
            return "binary(8)";
        return sqlType;
    }

    private static string BaseTypeName(string sqlType)
    {
        int paren = sqlType.IndexOf('(');
        return (paren < 0 ? sqlType : sqlType.Substring(0, paren)).Trim();
    }

    private static string FormatValue(string value, string sqlType)
    {
        if (value == null)
            return "NULL";
        string baseType = BaseTypeName(sqlType);
        if (NumericTypes.Contains(baseType) && IsNumericLiteral(value))
            return value;
        if (BinaryTypes.Contains(baseType) && IsHexLiteral(value))
            return value;
        return "N'" + value.Replace("'", "''") + "'";
    }

    // Conservative: anything that doesn't look like a plain numeric literal gets quoted and left to implicit conversion.
    private static bool IsNumericLiteral(string value) =>
        value.Length > 0 && value.All(ch => char.IsDigit(ch) || ch == '.' || ch == '-' || ch == '+' || ch == 'e' || ch == 'E');

    private static bool IsHexLiteral(string value) =>
        value.Length >= 2 && value[0] == '0' && (value[1] == 'x' || value[1] == 'X') && value.Skip(2).All(Uri.IsHexDigit);

    private static string InferType(List<string[]> rows, int col)
    {
        bool any = false;
        bool allInt = true, allDec = true, allFloat = true, allGuid = true, allHex = true, allDate = true;
        bool needsBigInt = false, dateOnly = true;
        int maxLen = 1, maxScale = 0, maxIntDigits = 1;

        foreach (string[] row in rows)
        {
            string v = col < row.Length ? row[col] : null;
            if (v == null)
                continue;
            any = true;
            maxLen = Math.Max(maxLen, v.Length);

            if (allInt)
            {
                if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                    needsBigInt |= l > int.MaxValue || l < int.MinValue;
                else
                    allInt = false;
            }
            if (allDec)
            {
                if (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                {
                    int dot = v.IndexOf('.');
                    int scale = dot < 0 ? 0 : v.Length - dot - 1;
                    maxScale = Math.Max(maxScale, scale);
                    maxIntDigits = Math.Max(maxIntDigits, (dot < 0 ? v.Length : dot) - (v.StartsWith("-") ? 1 : 0));
                }
                else
                    allDec = false;
            }
            if (allFloat && !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                allFloat = false;
            if (allGuid && !Guid.TryParse(v, out _))
                allGuid = false;
            if (allHex && !IsHexLiteral(v))
                allHex = false;
            if (allDate)
            {
                if (DateTime.TryParseExact(v, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    dateOnly &= v.Length <= 10;
                else
                    allDate = false;
            }
        }

        if (!any)
            return "nvarchar(max)";
        if (allGuid)
            return "uniqueidentifier";
        if (allInt)
            return needsBigInt ? "bigint" : "int";
        if (allDec)
            return $"decimal({Math.Min(38, maxIntDigits + maxScale)},{maxScale})";
        if (allFloat)
            return "float";
        if (allDate)
            return dateOnly ? "date" : "datetime2";
        if (allHex)
            return "varbinary(max)";
        return maxLen > 4000 ? "nvarchar(max)" : $"nvarchar({maxLen})";
    }
}
