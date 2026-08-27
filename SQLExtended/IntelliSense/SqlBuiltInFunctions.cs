using System;
using System.Collections.Generic;
using System.Linq;

namespace SQLExtended.IntelliSense;

/// <summary>
/// The kind of value a built-in function argument expects, when it draws from a
/// known fixed set worth offering as completion (rather than an arbitrary expression).
/// </summary>
internal enum SqlArgKind
{
    None,
    DataType,
    DatePart
}

/// <summary>
/// A suggestion offered for a function argument position (a data type or a datepart).
/// </summary>
internal sealed class SqlArgSuggestion
{
    public SqlArgSuggestion(string name, string detail, string sortKey)
    {
        Name = name;
        Detail = detail;
        SortKey = sortKey ?? name;
    }

    public string Name { get; }
    public string Detail { get; }
    public string SortKey { get; }
}

/// <summary>
/// A single parameter of a built-in T-SQL function, used for signature help and
/// the parenthesized signature shown in completion suffixes.
/// </summary>
internal sealed class BuiltInParam
{
    public BuiltInParam(string name, bool isOptional)
    {
        Name = name;
        IsOptional = isOptional;
    }

    public string Name { get; }
    public bool IsOptional { get; }

    /// <summary>Display form, wrapping optional params in square brackets.</summary>
    public string Display => IsOptional ? $"[{Name}]" : Name;
}

/// <summary>
/// Describes a built-in T-SQL function (e.g. GETDATE, DATEADD, STRING_SPLIT) for
/// IntelliSense completion and signature help. These are not stored in the schema
/// cache — they are intrinsic to the language and available without a connection.
/// </summary>
internal sealed class SqlBuiltInFunction
{
    public SqlBuiltInFunction(
        string name, string category, string returnType, string description,
        IReadOnlyList<BuiltInParam> parameters, bool requiresParentheses)
    {
        Name = name;
        Category = category;
        ReturnType = returnType;
        Description = description;
        Parameters = parameters;
        RequiresParentheses = requiresParentheses;
    }

    public string Name { get; }
    public string Category { get; }
    public string ReturnType { get; }
    public string Description { get; }
    public IReadOnlyList<BuiltInParam> Parameters { get; }

    /// <summary>
    /// True for ordinary functions called with parentheses (even no-argument ones
    /// like GETDATE()). False for niladic functions that take no parentheses at all
    /// (e.g. CURRENT_TIMESTAMP, SESSION_USER).
    /// </summary>
    public bool RequiresParentheses { get; }

    /// <summary>
    /// The full signature, e.g. "DATEADD(datepart, number, date)" or "GETDATE()"
    /// or "CURRENT_TIMESTAMP".
    /// </summary>
    public string Signature => RequiresParentheses
        ? $"{Name}({string.Join(", ", Parameters.Select(p => p.Display))})"
        : Name;
}

/// <summary>
/// Catalog of built-in T-SQL functions for IntelliSense. Covers the documented
/// function categories: aggregate, analytic, ranking, conversion, date/time,
/// logical, mathematical, string, JSON, security, system, and metadata.
/// </summary>
internal static class SqlBuiltInFunctions
{
    private static readonly List<SqlBuiltInFunction> _all = Build();

    private static readonly Dictionary<string, SqlBuiltInFunction> _byName =
        BuildIndex(_all);

    public static IReadOnlyList<SqlBuiltInFunction> All => _all;

    /// <summary>
    /// Looks up a built-in function by name, case-insensitively. Returns null if
    /// the name is not a known built-in. Niladic functions (no parentheses) are
    /// included but won't surface in signature help since they take no arguments.
    /// </summary>
    public static SqlBuiltInFunction Find(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return _byName.TryGetValue(name, out var fn) ? fn : null;
    }

    /// <summary>
    /// Returns the kind of value expected at the given (zero-based) argument position
    /// of a built-in function, when that position draws from a fixed set worth offering
    /// as completion. CAST/PARSE-style "expr AS type" data-type detection is handled by
    /// the caller, since it depends on the AS keyword rather than the comma position.
    /// </summary>
    public static SqlArgKind GetArgumentKind(string functionName, int argIndex)
    {
        if (string.IsNullOrEmpty(functionName))
            return SqlArgKind.None;

        switch (functionName.ToUpperInvariant())
        {
            // first argument is a data type
            case "CONVERT":
            case "TRY_CONVERT":
                return argIndex == 0 ? SqlArgKind.DataType : SqlArgKind.None;

            // first argument is a datepart (year, month, day, …)
            case "DATEADD":
            case "DATEDIFF":
            case "DATEDIFF_BIG":
            case "DATENAME":
            case "DATEPART":
            case "DATETRUNC":
            case "DATE_BUCKET":
                return argIndex == 0 ? SqlArgKind.DatePart : SqlArgKind.None;

            default:
                return SqlArgKind.None;
        }
    }

    /// <summary>
    /// Functions whose data type is supplied after an AS keyword: CAST(expr AS type),
    /// PARSE(string AS type [, culture]).
    /// </summary>
    public static bool UsesAsDataType(string functionName)
    {
        if (string.IsNullOrEmpty(functionName))
            return false;
        switch (functionName.ToUpperInvariant())
        {
            case "CAST":
            case "TRY_CAST":
            case "PARSE":
            case "TRY_PARSE":
                return true;
            default:
                return false;
        }
    }

    /// <summary>T-SQL data types, offered after CONVERT( / CAST(… AS / DECLARE.</summary>
    public static IReadOnlyList<SqlArgSuggestion> DataTypes { get; } = BuildDataTypes();

    /// <summary>Datepart names and common abbreviations, offered as the first arg of DATEADD etc.</summary>
    public static IReadOnlyList<SqlArgSuggestion> DateParts { get; } = BuildDateParts();

    private static List<SqlArgSuggestion> BuildDataTypes()
    {
        // detail shows the parameterized form where relevant.
        SqlArgSuggestion T(string name, string detail = "Data type") => new SqlArgSuggestion(name, detail, name);
        return new List<SqlArgSuggestion>
        {
            T("bigint"), T("int"), T("smallint"), T("tinyint"), T("bit"),
            T("decimal", "decimal(p, s)"), T("numeric", "numeric(p, s)"),
            T("money"), T("smallmoney"), T("float", "float(n)"), T("real"),
            T("date"), T("datetime"), T("datetime2", "datetime2(n)"), T("smalldatetime"),
            T("datetimeoffset", "datetimeoffset(n)"), T("time", "time(n)"),
            T("char", "char(n)"), T("varchar", "varchar(n | max)"), T("text"),
            T("nchar", "nchar(n)"), T("nvarchar", "nvarchar(n | max)"), T("ntext"),
            T("binary", "binary(n)"), T("varbinary", "varbinary(n | max)"), T("image"),
            T("uniqueidentifier"), T("xml"), T("sql_variant"), T("sysname"),
            T("rowversion"), T("hierarchyid"), T("geography"), T("geometry"),
        };
    }

    private static List<SqlArgSuggestion> BuildDateParts()
    {
        // SortKey "0…" keeps canonical names above abbreviations.
        SqlArgSuggestion Canon(string name, string abbrevs) => new SqlArgSuggestion(name, $"datepart · {abbrevs}", "0_" + name);
        SqlArgSuggestion Abbr(string name, string ofWhich) => new SqlArgSuggestion(name, $"datepart · {ofWhich}", "1_" + name);
        return new List<SqlArgSuggestion>
        {
            Canon("year", "yy, yyyy"), Canon("quarter", "qq, q"), Canon("month", "mm, m"),
            Canon("dayofyear", "dy, y"), Canon("day", "dd, d"),
            Canon("week", "wk, ww"), Canon("weekday", "dw"), Canon("iso_week", "isowk, isoww"),
            Canon("hour", "hh"), Canon("minute", "mi, n"), Canon("second", "ss, s"),
            Canon("millisecond", "ms"), Canon("microsecond", "mcs"), Canon("nanosecond", "ns"),
            Canon("tzoffset", "tz"),
            Abbr("yyyy", "year"), Abbr("qq", "quarter"), Abbr("mm", "month"),
            Abbr("dd", "day"), Abbr("wk", "week"), Abbr("dw", "weekday"),
            Abbr("hh", "hour"), Abbr("mi", "minute"), Abbr("ss", "second"), Abbr("ms", "millisecond"),
        };
    }

    private static Dictionary<string, SqlBuiltInFunction> BuildIndex(List<SqlBuiltInFunction> all)
    {
        var dict = new Dictionary<string, SqlBuiltInFunction>(StringComparer.OrdinalIgnoreCase);
        foreach (var fn in all)
            dict[fn.Name] = fn; // last wins on dupes
        return dict;
    }

    // --- Authoring helpers -------------------------------------------------

    /// <summary>
    /// Defines a parenthesized function. Each param spec is a parameter name;
    /// append "?" to mark it optional (e.g. "length?").
    /// </summary>
    private static SqlBuiltInFunction F(string name, string category, string ret, string desc, params string[] paramSpecs)
    {
        var ps = new List<BuiltInParam>(paramSpecs.Length);
        foreach (var spec in paramSpecs)
        {
            bool optional = spec.EndsWith("?", StringComparison.Ordinal);
            string pname = optional ? spec.Substring(0, spec.Length - 1) : spec;
            ps.Add(new BuiltInParam(pname, optional));
        }
        return new SqlBuiltInFunction(name, category, ret, desc, ps, requiresParentheses: true);
    }

    /// <summary>Defines a niladic function/constant that takes no parentheses.</summary>
    private static SqlBuiltInFunction N(string name, string category, string ret, string desc)
        => new SqlBuiltInFunction(name, category, ret, desc, Array.Empty<BuiltInParam>(), requiresParentheses: false);

    private static List<SqlBuiltInFunction> Build()
    {
        const string Agg = "Aggregate";
        const string Analytic = "Analytic";
        const string Ranking = "Ranking";
        const string Conv = "Conversion";
        const string DateTime = "Date/Time";
        const string Logical = "Logical";
        const string Math = "Mathematical";
        const string Str = "String";
        const string Json = "JSON";
        const string NullFn = "Null Handling";
        const string Security = "Security";
        const string System = "System";
        const string Meta = "Metadata";
        const string Cursor = "Cursor";

        return new List<SqlBuiltInFunction>
        {
            // --- Aggregate ---
            F("AVG", Agg, "numeric", "Returns the average of the values in a group.", "expression"),
            F("CHECKSUM_AGG", Agg, "int", "Returns the checksum of the values in a group.", "expression"),
            F("COUNT", Agg, "int", "Returns the number of items in a group.", "expression"),
            F("COUNT_BIG", Agg, "bigint", "Returns the number of items in a group as a bigint.", "expression"),
            F("GROUPING", Agg, "tinyint", "Indicates whether a column in a GROUP BY list is aggregated.", "column_expression"),
            F("GROUPING_ID", Agg, "int", "Computes the level of grouping for a set of grouped columns.", "column_expression", "...?"),
            F("MAX", Agg, "expr type", "Returns the maximum value in a group.", "expression"),
            F("MIN", Agg, "expr type", "Returns the minimum value in a group.", "expression"),
            F("STDEV", Agg, "float", "Returns the statistical standard deviation of all values in the expression.", "expression"),
            F("STDEVP", Agg, "float", "Returns the population standard deviation of all values in the expression.", "expression"),
            F("STRING_AGG", Agg, "nvarchar", "Concatenates values from rows into a single delimited string.", "expression", "separator"),
            F("SUM", Agg, "numeric", "Returns the sum of all values in the expression.", "expression"),
            F("VAR", Agg, "float", "Returns the statistical variance of all values in the expression.", "expression"),
            F("VARP", Agg, "float", "Returns the statistical population variance of all values in the expression.", "expression"),
            F("APPROX_COUNT_DISTINCT", Agg, "bigint", "Returns the approximate number of unique non-null values in a group.", "expression"),

            // --- Analytic / Window ---
            F("CUME_DIST", Analytic, "float", "Calculates the cumulative distribution of a value within a group."),
            F("FIRST_VALUE", Analytic, "expr type", "Returns the first value in an ordered set of values.", "scalar_expression"),
            F("LAST_VALUE", Analytic, "expr type", "Returns the last value in an ordered set of values.", "scalar_expression"),
            F("LAG", Analytic, "expr type", "Accesses a value from a previous row without a self-join.", "scalar_expression", "offset?", "default?"),
            F("LEAD", Analytic, "expr type", "Accesses a value from a following row without a self-join.", "scalar_expression", "offset?", "default?"),
            F("PERCENTILE_CONT", Analytic, "float", "Calculates a percentile based on a continuous distribution.", "numeric_literal"),
            F("PERCENTILE_DISC", Analytic, "expr type", "Calculates a percentile based on a discrete distribution.", "numeric_literal"),
            F("PERCENT_RANK", Analytic, "float", "Calculates the relative rank of a row within a group."),

            // --- Ranking ---
            F("RANK", Ranking, "bigint", "Returns the rank of each row within a partition, with gaps."),
            F("DENSE_RANK", Ranking, "bigint", "Returns the rank of each row within a partition, without gaps."),
            F("NTILE", Ranking, "bigint", "Distributes the rows into a specified number of groups.", "integer_expression"),
            F("ROW_NUMBER", Ranking, "bigint", "Returns the sequential number of a row within a partition."),

            // --- Conversion ---
            F("CAST", Conv, "target type", "Converts an expression from one data type to another.", "expression AS data_type"),
            F("CONVERT", Conv, "target type", "Converts an expression from one data type to another, with optional style.", "data_type", "expression", "style?"),
            F("PARSE", Conv, "target type", "Converts a string to a date/time or number type using culture rules.", "string_value AS data_type", "culture?"),
            F("TRY_CAST", Conv, "target type", "Converts an expression, returning NULL on failure.", "expression AS data_type"),
            F("TRY_CONVERT", Conv, "target type", "Converts an expression, returning NULL on failure.", "data_type", "expression", "style?"),
            F("TRY_PARSE", Conv, "target type", "Parses a string to a date/time or number, returning NULL on failure.", "string_value AS data_type", "culture?"),

            // --- Date / Time ---
            N("CURRENT_TIMESTAMP", DateTime, "datetime", "Returns the current database system timestamp (ANSI equivalent of GETDATE())."),
            F("SYSDATETIME", DateTime, "datetime2", "Returns the current database system date and time (datetime2)."),
            F("SYSDATETIMEOFFSET", DateTime, "datetimeoffset", "Returns the current system date and time with time zone offset."),
            F("SYSUTCDATETIME", DateTime, "datetime2", "Returns the current UTC database system date and time."),
            F("GETDATE", DateTime, "datetime", "Returns the current database system date and time."),
            F("GETUTCDATE", DateTime, "datetime", "Returns the current UTC database system date and time."),
            F("DATENAME", DateTime, "nvarchar", "Returns a string representing the specified datepart of a date.", "datepart", "date"),
            F("DATEPART", DateTime, "int", "Returns an integer representing the specified datepart of a date.", "datepart", "date"),
            F("DAY", DateTime, "int", "Returns the day-of-month part of the specified date.", "date"),
            F("MONTH", DateTime, "int", "Returns the month part of the specified date.", "date"),
            F("YEAR", DateTime, "int", "Returns the year part of the specified date.", "date"),
            F("DATEADD", DateTime, "date type", "Adds an interval to a date and returns the new date.", "datepart", "number", "date"),
            F("DATEDIFF", DateTime, "int", "Returns the count of datepart boundaries crossed between two dates.", "datepart", "startdate", "enddate"),
            F("DATEDIFF_BIG", DateTime, "bigint", "Returns the count of datepart boundaries crossed between two dates as bigint.", "datepart", "startdate", "enddate"),
            F("DATETRUNC", DateTime, "date type", "Truncates a date/time value to the specified datepart.", "datepart", "date"),
            F("DATE_BUCKET", DateTime, "date type", "Returns the date/time at the start of the bucket the value falls into.", "datepart", "number", "date", "origin?"),
            F("EOMONTH", DateTime, "date", "Returns the last day of the month containing the specified date.", "start_date", "month_to_add?"),
            F("SWITCHOFFSET", DateTime, "datetimeoffset", "Changes the time zone offset of a datetimeoffset value.", "datetimeoffset", "time_zone"),
            F("TODATETIMEOFFSET", DateTime, "datetimeoffset", "Combines a datetime2 value with a time zone offset.", "expression", "time_zone"),
            F("ISDATE", DateTime, "int", "Returns 1 if the expression is a valid date/time, otherwise 0.", "expression"),
            F("DATEFROMPARTS", DateTime, "date", "Builds a date from year, month and day parts.", "year", "month", "day"),
            F("DATETIMEFROMPARTS", DateTime, "datetime", "Builds a datetime from date and time parts.", "year", "month", "day", "hour", "minute", "seconds", "milliseconds"),
            F("DATETIME2FROMPARTS", DateTime, "datetime2", "Builds a datetime2 from date and time parts.", "year", "month", "day", "hour", "minute", "seconds", "fractions", "precision"),
            F("DATETIMEOFFSETFROMPARTS", DateTime, "datetimeoffset", "Builds a datetimeoffset from its parts.", "year", "month", "day", "hour", "minute", "seconds", "fractions", "hour_offset", "minute_offset", "precision"),
            F("SMALLDATETIMEFROMPARTS", DateTime, "smalldatetime", "Builds a smalldatetime from date and time parts.", "year", "month", "day", "hour", "minute"),
            F("TIMEFROMPARTS", DateTime, "time", "Builds a time value from its parts.", "hour", "minute", "seconds", "fractions", "precision"),
            F("CURRENT_TIMEZONE", DateTime, "varchar", "Returns the name of the time zone observed by the server/instance."),
            F("CURRENT_TIMEZONE_ID", DateTime, "varchar", "Returns the ID of the time zone observed by the server/instance."),

            // --- Logical ---
            F("CHOOSE", Logical, "expr type", "Returns the item at the specified index from a list of values.", "index", "val_1", "val_2", "...?"),
            F("IIF", Logical, "expr type", "Returns one of two values depending on whether a Boolean expression is true.", "boolean_expression", "true_value", "false_value"),
            F("GREATEST", Logical, "expr type", "Returns the maximum value from a list of one or more expressions.", "expression1", "...?"),
            F("LEAST", Logical, "expr type", "Returns the minimum value from a list of one or more expressions.", "expression1", "...?"),

            // --- Null handling ---
            F("ISNULL", NullFn, "expr type", "Replaces NULL with the specified replacement value.", "check_expression", "replacement_value"),
            F("COALESCE", NullFn, "expr type", "Returns the first non-null expression in the list.", "expression1", "expression2", "...?"),
            F("NULLIF", NullFn, "expr type", "Returns NULL if the two expressions are equal; otherwise the first.", "expression1", "expression2"),

            // --- Mathematical ---
            F("ABS", Math, "numeric", "Returns the absolute (positive) value of a number.", "numeric_expression"),
            F("ACOS", Math, "float", "Returns the angle whose cosine is the specified value (in radians).", "float_expression"),
            F("ASIN", Math, "float", "Returns the angle whose sine is the specified value (in radians).", "float_expression"),
            F("ATAN", Math, "float", "Returns the angle whose tangent is the specified value (in radians).", "float_expression"),
            F("ATN2", Math, "float", "Returns the angle between the positive x-axis and the point (y, x).", "float_y", "float_x"),
            F("CEILING", Math, "numeric", "Returns the smallest integer greater than or equal to the value.", "numeric_expression"),
            F("COS", Math, "float", "Returns the trigonometric cosine of the specified angle (radians).", "float_expression"),
            F("COT", Math, "float", "Returns the trigonometric cotangent of the specified angle (radians).", "float_expression"),
            F("DEGREES", Math, "numeric", "Converts radians to degrees.", "numeric_expression"),
            F("EXP", Math, "float", "Returns the exponential value (e^x) of the specified value.", "float_expression"),
            F("FLOOR", Math, "numeric", "Returns the largest integer less than or equal to the value.", "numeric_expression"),
            F("LOG", Math, "float", "Returns the natural logarithm, or logarithm in the specified base.", "float_expression", "base?"),
            F("LOG10", Math, "float", "Returns the base-10 logarithm of the specified value.", "float_expression"),
            F("PI", Math, "float", "Returns the constant value of pi."),
            F("POWER", Math, "numeric", "Returns the value raised to the specified power.", "float_expression", "y"),
            F("RADIANS", Math, "numeric", "Converts degrees to radians.", "numeric_expression"),
            F("RAND", Math, "float", "Returns a pseudo-random float between 0 and 1.", "seed?"),
            F("ROUND", Math, "numeric", "Rounds a number to the specified length or precision.", "numeric_expression", "length", "function?"),
            F("SIGN", Math, "numeric", "Returns the sign (-1, 0, +1) of the specified value.", "numeric_expression"),
            F("SIN", Math, "float", "Returns the trigonometric sine of the specified angle (radians).", "float_expression"),
            F("SQRT", Math, "float", "Returns the square root of the specified value.", "float_expression"),
            F("SQUARE", Math, "float", "Returns the square of the specified value.", "float_expression"),
            F("TAN", Math, "float", "Returns the trigonometric tangent of the specified angle (radians).", "float_expression"),

            // --- String ---
            F("ASCII", Str, "int", "Returns the ASCII code of the leftmost character of a string.", "character_expression"),
            F("CHAR", Str, "char", "Returns the character for the specified ASCII integer code.", "integer_expression"),
            F("CHARINDEX", Str, "int", "Returns the starting position of a substring within a string.", "expression_to_find", "expression_to_search", "start_location?"),
            F("CONCAT", Str, "string", "Concatenates two or more values into one string.", "string_value1", "string_value2", "...?"),
            F("CONCAT_WS", Str, "string", "Concatenates values with a separator, skipping NULLs.", "separator", "argument1", "argument2", "...?"),
            F("DIFFERENCE", Str, "int", "Returns the difference between the SOUNDEX values of two strings.", "character_expression1", "character_expression2"),
            F("FORMAT", Str, "nvarchar", "Returns a value formatted with the specified format and culture.", "value", "format", "culture?"),
            F("LEFT", Str, "string", "Returns the specified number of characters from the left of a string.", "character_expression", "integer_expression"),
            F("LEN", Str, "int", "Returns the number of characters of a string, excluding trailing spaces.", "string_expression"),
            F("LOWER", Str, "string", "Returns a string with all characters converted to lowercase.", "character_expression"),
            F("LTRIM", Str, "string", "Removes leading spaces (or specified characters) from a string.", "character_expression", "characters?"),
            F("NCHAR", Str, "nchar", "Returns the Unicode character for the specified integer code.", "integer_expression"),
            F("PATINDEX", Str, "int", "Returns the starting position of a pattern within a string.", "pattern", "expression"),
            F("QUOTENAME", Str, "nvarchar", "Returns a Unicode string with delimiters to make a valid identifier.", "character_string", "quote_character?"),
            F("REPLACE", Str, "string", "Replaces all occurrences of a substring with another substring.", "string_expression", "string_pattern", "string_replacement"),
            F("REPLICATE", Str, "string", "Repeats a string value the specified number of times.", "string_expression", "integer_expression"),
            F("REVERSE", Str, "string", "Returns the reverse order of a string value.", "string_expression"),
            F("RIGHT", Str, "string", "Returns the specified number of characters from the right of a string.", "character_expression", "integer_expression"),
            F("RTRIM", Str, "string", "Removes trailing spaces (or specified characters) from a string.", "character_expression", "characters?"),
            F("SOUNDEX", Str, "varchar", "Returns a four-character code of how a string sounds.", "character_expression"),
            F("SPACE", Str, "char", "Returns a string of repeated spaces.", "integer_expression"),
            F("STR", Str, "char", "Returns character data converted from a number.", "float_expression", "length?", "decimal?"),
            F("STRING_ESCAPE", Str, "nvarchar", "Escapes special characters in a string for the given format (JSON).", "text", "type"),
            F("STRING_SPLIT", Str, "table", "Splits a string into rows of substrings by a separator.", "string", "separator", "enable_ordinal?"),
            F("STUFF", Str, "string", "Deletes part of a string and inserts another string at that position.", "character_expression", "start", "length", "replace_with"),
            F("SUBSTRING", Str, "string", "Returns part of a string starting at a position for a length.", "expression", "start", "length"),
            F("TRANSLATE", Str, "string", "Replaces characters listed in one set with characters in another set.", "inputString", "characters", "translations"),
            F("TRIM", Str, "string", "Removes leading and trailing spaces (or specified characters).", "characters_from?", "string"),
            F("UNICODE", Str, "int", "Returns the Unicode code point of the first character of a string.", "ncharacter_expression"),
            F("UPPER", Str, "string", "Returns a string with all characters converted to uppercase.", "character_expression"),

            // --- JSON ---
            F("ISJSON", Json, "int", "Tests whether a string contains valid JSON.", "expression", "json_type_constraint?"),
            F("JSON_VALUE", Json, "nvarchar", "Extracts a scalar value from a JSON string.", "expression", "path"),
            F("JSON_QUERY", Json, "nvarchar", "Extracts an object or array from a JSON string.", "expression", "path?"),
            F("JSON_MODIFY", Json, "nvarchar", "Updates the value of a property in a JSON string and returns it.", "expression", "path", "newValue"),
            F("JSON_PATH_EXISTS", Json, "int", "Tests whether a specified SQL/JSON path exists in the input JSON.", "value_expression", "path"),
            F("JSON_OBJECT", Json, "nvarchar", "Constructs a JSON object from key/value pairs.", "key:value?", "...?"),
            F("JSON_ARRAY", Json, "nvarchar", "Constructs a JSON array from a list of values.", "value?", "...?"),
            F("OPENJSON", Json, "table", "Parses JSON text and returns objects and properties as rows and columns.", "jsonExpression", "path?"),

            // --- Security ---
            N("CURRENT_USER", Security, "sysname", "Returns the name of the current database user."),
            N("SESSION_USER", Security, "sysname", "Returns the user name of the current session."),
            N("SYSTEM_USER", Security, "sysname", "Returns the login name of the current security context."),
            F("USER_NAME", Security, "nvarchar", "Returns the database user name from a specified ID.", "id?"),
            F("USER_ID", Security, "int", "Returns the database user ID for a specified name.", "user?"),
            F("SUSER_NAME", Security, "nvarchar", "Returns the login name for a specified security identification number.", "server_user_id?"),
            F("SUSER_SNAME", Security, "nvarchar", "Returns the login name from a security identification number (SID).", "server_user_sid?"),
            F("SUSER_ID", Security, "int", "Returns the login identification number for a specified login name.", "login?"),
            F("SUSER_SID", Security, "varbinary", "Returns the SID for a specified login name.", "login?", "Param2?"),
            F("IS_MEMBER", Security, "int", "Indicates whether the current user is a member of the specified group/role.", "group_or_role"),
            F("IS_ROLEMEMBER", Security, "int", "Indicates whether a principal is a member of the specified database role.", "role", "database_principal?"),
            F("IS_SRVROLEMEMBER", Security, "int", "Indicates whether a login is a member of the specified server role.", "role", "login?"),
            F("HAS_PERMS_BY_NAME", Security, "int", "Returns the effective permission of the current user on a securable.", "securable", "securable_class", "permission", "sub_securable?", "sub_securable_class?"),
            N("ORIGINAL_LOGIN", Security, "sysname", "Returns the original login name that connected to the instance."),
            F("DATABASE_PRINCIPAL_ID", Security, "int", "Returns the ID of a principal in the current database.", "principal_name?"),

            // --- System ---
            F("ERROR_LINE", System, "int", "Returns the line number where an error occurred inside CATCH."),
            F("ERROR_MESSAGE", System, "nvarchar", "Returns the message text of the error that caused the CATCH block to run."),
            F("ERROR_NUMBER", System, "int", "Returns the number of the error that caused the CATCH block to run."),
            F("ERROR_PROCEDURE", System, "nvarchar", "Returns the name of the procedure where an error occurred."),
            F("ERROR_SEVERITY", System, "int", "Returns the severity of the error that caused the CATCH block to run."),
            F("ERROR_STATE", System, "int", "Returns the state number of the error that caused the CATCH block to run."),
            F("FORMATMESSAGE", System, "nvarchar", "Builds a message from an existing message or string and arguments.", "msg_number_or_string", "param_value?", "...?"),
            F("ISNUMERIC", System, "int", "Returns 1 if the expression is a valid numeric type, otherwise 0.", "expression"),
            F("NEWID", System, "uniqueidentifier", "Creates a new unique identifier (GUID)."),
            F("NEWSEQUENTIALID", System, "uniqueidentifier", "Creates a GUID greater than any previously generated on the computer."),
            F("ROWCOUNT_BIG", System, "bigint", "Returns the number of rows affected by the last statement, as bigint."),
            F("XACT_STATE", System, "smallint", "Reports the transaction state of the current request."),
            F("BINARY_CHECKSUM", System, "int", "Returns the binary checksum over a row or list of expressions.", "expression", "...?"),
            F("CHECKSUM", System, "int", "Returns the checksum over a row or list of expressions.", "expression", "...?"),
            F("COMPRESS", System, "varbinary", "Compresses the input using the GZIP algorithm.", "expression"),
            F("DECOMPRESS", System, "varbinary", "Decompresses GZIP-compressed input.", "expression"),
            F("CONTEXT_INFO", System, "varbinary", "Returns the context_info value set for the current session."),
            F("SESSION_CONTEXT", System, "sql_variant", "Returns the value of a key from the current session context.", "key"),
            F("CONNECTIONPROPERTY", System, "sql_variant", "Returns a property of the connection's transport.", "property"),
            F("CURRENT_REQUEST_ID", System, "smallint", "Returns the ID of the current request within the current session."),
            F("HOST_ID", System, "char", "Returns the workstation identification number of the client."),
            F("HOST_NAME", System, "nvarchar", "Returns the workstation name of the client."),
            F("APP_NAME", System, "nvarchar", "Returns the application name for the current session, if set."),

            // --- Metadata ---
            F("COL_LENGTH", Meta, "smallint", "Returns the defined length of a column.", "table", "column"),
            F("COL_NAME", Meta, "sysname", "Returns the name of a column from its table ID and column ID.", "table_id", "column_id"),
            F("COLUMNPROPERTY", Meta, "int", "Returns information about a column or procedure parameter.", "id", "column", "property"),
            F("DATABASEPROPERTYEX", Meta, "sql_variant", "Returns the value of a database option or property.", "database", "property"),
            F("DB_ID", Meta, "int", "Returns the database ID for a specified database name.", "database_name?"),
            F("DB_NAME", Meta, "nvarchar", "Returns the database name for a specified database ID.", "database_id?"),
            F("FILE_ID", Meta, "smallint", "Returns the file ID for a specified logical file name.", "file_name"),
            F("FILE_NAME", Meta, "nvarchar", "Returns the logical file name for a specified file ID.", "file_id"),
            F("OBJECT_DEFINITION", Meta, "nvarchar", "Returns the T-SQL source text of a module.", "object_id"),
            F("OBJECT_ID", Meta, "int", "Returns the database object ID of a schema-scoped object.", "object_name", "object_type?"),
            F("OBJECT_NAME", Meta, "sysname", "Returns the name of a schema-scoped object from its ID.", "object_id", "database_id?"),
            F("OBJECT_SCHEMA_NAME", Meta, "sysname", "Returns the schema name of a schema-scoped object.", "object_id", "database_id?"),
            F("OBJECTPROPERTY", Meta, "int", "Returns information about schema-scoped objects in the current database.", "id", "property"),
            F("OBJECTPROPERTYEX", Meta, "sql_variant", "Returns extended information about schema-scoped objects.", "id", "property"),
            F("SCHEMA_ID", Meta, "int", "Returns the schema ID for a specified schema name.", "schema_name?"),
            F("SCHEMA_NAME", Meta, "sysname", "Returns the schema name for a specified schema ID.", "schema_id?"),
            F("SCOPE_IDENTITY", Meta, "numeric", "Returns the last identity value inserted in the current scope."),
            F("IDENT_CURRENT", Meta, "numeric", "Returns the last identity value generated for a specified table.", "table_name"),
            F("IDENT_INCR", Meta, "numeric", "Returns the increment value of the identity column of a table.", "table_or_view"),
            F("IDENT_SEED", Meta, "numeric", "Returns the seed value of the identity column of a table.", "table_or_view"),
            F("TYPE_ID", Meta, "int", "Returns the ID for a specified data type name.", "type_name"),
            F("TYPE_NAME", Meta, "sysname", "Returns the data type name for a specified type ID.", "type_id"),
            F("TYPEPROPERTY", Meta, "int", "Returns information about a data type.", "type", "property"),

            // --- Cursor ---
            F("CURSOR_STATUS", Cursor, "smallint", "Returns the state of a cursor for a given variable or name.", "scope", "name"),
        };
    }
}
