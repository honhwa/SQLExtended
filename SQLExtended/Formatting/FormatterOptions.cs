using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.IO;

namespace SQLExtended.Formatting;

public class FormatterOptions
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SQLExtended", "SSMS");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "formatter-options.json");

    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        Formatting = Newtonsoft.Json.Formatting.Indented,
        Converters = { new StringEnumConverter() },
        DefaultValueHandling = DefaultValueHandling.Include
    };

    // Keyword Casing
    [JsonConverter(typeof(StringEnumConverter))]
    public CasingOption KeywordCase { get; set; } = CasingOption.Upper;

    // Identifier Casing
    [JsonConverter(typeof(StringEnumConverter))]
    public CasingOption IdentifierCase { get; set; } = CasingOption.Unchanged;

    // Built-in function casing (ROW_NUMBER, SUM, GETDATE, ...). ScriptDom's KeywordCasing reaches
    // reserved keywords only — a function call is an identifier in the AST and comes back spelled
    // exactly as it was typed — so this is a separate switch. Upper by default, to match the
    // KeywordCase default and the surrounding SELECT / PARTITION BY / OVER.
    [JsonConverter(typeof(StringEnumConverter))]
    public CasingOption BuiltInFunctionCase { get; set; } = CasingOption.Upper;

    // Indentation
    [JsonConverter(typeof(StringEnumConverter))]
    public IndentStyleOption IndentStyle { get; set; } = IndentStyleOption.Tabs;

    public int IndentSize { get; set; } = 4;

    // When true (default), AND/OR continuation lines under WHERE keep ScriptDom's
    // indentation (aligned under the first predicate). When false, they are left-aligned
    // with the WHERE keyword (T-SQL "river" style). Only applies when
    // WhereConditionLayout == NewLinePerCondition.
    public bool IndentBetweenConditions { get; set; } = true;

    // SELECT clause
    [JsonConverter(typeof(StringEnumConverter))]
    public SelectColumnLayoutOption SelectColumnLayout { get; set; } = SelectColumnLayoutOption.StackedIndented;

    // Comma placement
    [JsonConverter(typeof(StringEnumConverter))]
    public CommaPositionOption CommaPosition { get; set; } = CommaPositionOption.TrailingComma;

    // Leading-comma indentation. When false (default), the leading comma is pulled back so
    // the identifiers stay aligned with the first item. When true, the comma sits at the
    // item's own indent level (e.g. "\t, [Name]"), matching the common T-SQL leading-comma
    // convention. Only applies when CommaPosition == LeadingComma.
    public bool LeadingCommaKeepIndent { get; set; } = false;

    // FROM / JOIN layout
    [JsonConverter(typeof(StringEnumConverter))]
    public JoinLayoutOption JoinLayout { get; set; } = JoinLayoutOption.NewLine;

    public bool JoinOnSameLine { get; set; } = false;

    // Normalize JOIN keywords to the shortest explicit form: "LEFT OUTER JOIN" -> "LEFT JOIN",
    // "RIGHT OUTER JOIN" -> "RIGHT JOIN". "FULL OUTER JOIN" and "INNER JOIN" are left as-is.
    // (Bare "JOIN" is already emitted as "INNER JOIN" by the underlying generator.)
    public bool NormalizeJoinKeywords { get; set; } = false;

    // WHERE clause
    [JsonConverter(typeof(StringEnumConverter))]
    public WhereConditionLayoutOption WhereConditionLayout { get; set; } = WhereConditionLayoutOption.NewLinePerCondition;

    // INSERT statements
    public int InsertColumnsPerLine { get; set; } = 4;
    public int InsertValuesPerLine { get; set; } = 4;
    public bool InsertParenthesesOnSameLine { get; set; } = false;

    // "INSERT INTO #tt (" — the opening bracket ends the table line, the columns stack beneath it and
    // the closing bracket keeps its own line. Distinct from InsertParenthesesOnSameLine, which also
    // pulls the *first column* up onto the table line and the closing bracket onto the last one
    // ("INSERT INTO #tt (OrderId,\n    CustomerId)"). Wins over it when both are set, since it is the
    // more specific request.
    public bool InsertOpenParenthesisOnSameLine { get; set; } = false;

    // Default style for the IntelliSense "INSERT all columns" template.
    // Both styles are always offered; this picks which one is preselected.
    [JsonConverter(typeof(StringEnumConverter))]
    public InsertTemplateStyleOption InsertTemplateDefaultStyle { get; set; } = InsertTemplateStyleOption.Values;

    // Semicolons
    [JsonConverter(typeof(StringEnumConverter))]
    public SemicolonOption TrailingSemicolon { get; set; } = SemicolonOption.Unchanged;

    // Blank lines
    public int BlankLinesBetweenStatements { get; set; } = 1;
    public int BlankLineAfterGO { get; set; } = 1;

    // Aliases
    [JsonConverter(typeof(StringEnumConverter))]
    public AliasStyleOption AliasStyle { get; set; } = AliasStyleOption.AS;

    // CASE expressions. ScriptDom emits a CASE as one run-on line and only ever breaks it where a
    // WHEN's own condition is a multi-part boolean — which lands the continuation under whatever
    // column the condition started in, so a CASE with several WHENs walks off the right of the
    // screen. This reflows it so every WHEN/ELSE starts a line. Layout only.
    [JsonConverter(typeof(StringEnumConverter))]
    public CaseWhenLayoutOption CaseWhenLayout { get; set; } = CaseWhenLayoutOption.Unchanged;

    // Bracket quoting
    [JsonConverter(typeof(StringEnumConverter))]
    public BracketQuotingOption BracketQuoting { get; set; } = BracketQuotingOption.Unchanged;

    // Max line width
    public int MaxLineWidth { get; set; } = 120;

    // UPDATE / SET clause
    public bool AlignSetClauseItem { get; set; } = false;
    public bool MultilineSetClauseItems { get; set; } = true;

    // Pulls the whole SET clause back one level so SET is left-aligned with its UPDATE, the first
    // assignment stays on the SET line, and the rest sit one indent in:
    //     UPDATE s              UPDATE s
    //         SET a = 1    ->   SET a = 1
    //             , b = 2           , b = 2
    public bool AlignSetWithUpdate { get; set; } = false;

    // CREATE TABLE / DDL formatting
    public bool AlignColumnDefinitionFields { get; set; } = true;
    public bool NewlineFormattedCheckConstraint { get; set; } = true;
    public bool NewLineFormattedIndexDefinition { get; set; } = true;

    // AS keyword (CTEs, derived tables)
    public bool AsKeywordOnOwnLine { get; set; } = false;

    // CTE stacked layout. When true, common-table expressions are reflowed to:
    //     WITH cteName AS (
    //         <body at one indent level>
    //     )
    //
    //     , cteNext AS (
    //         ...
    //     )
    // i.e. the opening "(" ends the WITH/, line, the body sits one level in, the closing
    // ")" drops to the left margin, and a blank line separates each CTE. Layout only —
    // it does not rename the CTEs.
    public bool CteStackedLayout { get; set; } = false;

    // Derived tables (subqueries in FROM / JOIN / APPLY) reflowed to the same stacked shape as
    // CteStackedLayout gives a CTE:
    //     LEFT JOIN (
    //         SELECT x
    //         , y
    //         FROM B
    //     ) AS bb ON bb.x = a.Id
    // ScriptDom instead aligns the body under whatever column the "(" landed in, which differs for
    // every join in the statement. Layout only — nothing is rewritten.
    public bool DerivedTableStackedLayout { get; set; } = false;

    // Parenthesis formatting in multiline lists
    public bool NewLineBeforeOpenParenthesis { get; set; } = false;
    public bool NewLineBeforeCloseParenthesis { get; set; } = false;

    // OFFSET / FETCH, WINDOW clauses
    public bool NewLineBeforeOffsetClause { get; set; } = true;
    public bool NewLineBeforeWindowClause { get; set; } = true;

    // Data type spacing: VARCHAR (50) vs VARCHAR(50)
    public bool SpaceBetweenDataTypeAndParameters { get; set; } = false;
    // DECIMAL(10, 2) vs DECIMAL(10,2)
    public bool SpaceBetweenParametersInDataType { get; set; } = true;

    // View columns on separate lines
    public bool MultilineViewColumnsList { get; set; } = true;

    // Indent view body
    public bool IndentViewBody { get; set; } = true;

    // Blank line before SELECT/INSERT/UPDATE/DELETE statements
    public bool BlankLineBeforeStatement { get; set; } = false;

    // Align FROM and JOIN keywords at the same indentation level
    public bool AlignFromAndJoins { get; set; } = true;

    // Procedure/function parameters inline (wrapped at max line width)
    public bool ProcedureParametersOnSameLine { get; set; } = false;

    public FormatterOptions Clone()
    {
        var json = JsonConvert.SerializeObject(this, JsonSettings);
        return JsonConvert.DeserializeObject<FormatterOptions>(json, JsonSettings);
    }

    public static FormatterOptions Load()
    {
        return FormatterProfileManager.Instance.GetActiveOptions();
    }

    public void Save()
    {
        var manager = FormatterProfileManager.Instance;
        manager.SaveProfile(manager.ActiveProfileName, this);
        manager.SetActiveProfile(manager.ActiveProfileName);
    }

    /// <summary>
    /// Loads from a specific file path (used for legacy direct-file access).
    /// </summary>
    internal static FormatterOptions LoadFromFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<FormatterOptions>(json, JsonSettings) ?? new FormatterOptions();
            }
        }
        catch
        {
            // Corrupted file — return defaults
        }

        return new FormatterOptions();
    }

    /// <summary>
    /// Saves to a specific file path (used for legacy direct-file access).
    /// </summary>
    internal void SaveToFile(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = JsonConvert.SerializeObject(this, JsonSettings);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Best effort
        }
    }

    public void ExportTo(string filePath)
    {
        string json = JsonConvert.SerializeObject(this, JsonSettings);
        File.WriteAllText(filePath, json);
    }

    public static FormatterOptions ImportFrom(string filePath)
    {
        string json = File.ReadAllText(filePath);
        return JsonConvert.DeserializeObject<FormatterOptions>(json, JsonSettings) ?? new FormatterOptions();
    }

    public static FormatterOptions Defaults => new FormatterOptions();
}

// Enums

public enum CasingOption
{
    Upper,
    Lower,
    Unchanged
}

public enum IndentStyleOption
{
    Tabs,
    Spaces
}

public enum SelectColumnLayoutOption
{
    SameLine,
    StackedIndented,
    StackedAligned,
    /// <summary>
    /// SELECT keyword alone on its line; every column on its own line indented one level
    /// under SELECT (FROM/WHERE/etc. stay at the SELECT keyword's level). Combined with
    /// leading commas this yields the classic stacked T-SQL layout.
    /// </summary>
    StackedFirstOnNewLine
}

public enum CommaPositionOption
{
    TrailingComma,
    LeadingComma
}

public enum JoinLayoutOption
{
    NewLine,
    SameLine
}

public enum WhereConditionLayoutOption
{
    NewLinePerCondition,
    Inline
}

public enum SemicolonOption
{
    Always,
    Never,
    Unchanged
}

public enum AliasStyleOption
{
    AS,
    NoAS,
    Unchanged,
    /// <summary>
    /// SELECT column aliases as "Alias = expression" instead of "expression AS Alias".
    /// Table aliases in FROM/JOIN are left alone (they cannot use this syntax).
    /// </summary>
    ColumnEquals
}

public enum CaseWhenLayoutOption
{
    /// <summary>Leave ScriptDom's layout alone.</summary>
    Unchanged,
    /// <summary>
    /// CASE alone on its line, every WHEN/ELSE one indent level under it, END back at the CASE's column:
    /// <code>
    /// CASE
    ///     WHEN a THEN 1
    ///     ELSE 0
    /// END
    /// </code>
    /// </summary>
    Stacked,
    /// <summary>
    /// The first WHEN stays on the CASE line and the rest align under it, END at the CASE's column:
    /// <code>
    /// CASE WHEN a THEN 1
    ///      WHEN b THEN 2
    ///      ELSE 0
    /// END
    /// </code>
    /// </summary>
    WhenAligned
}

public enum BracketQuotingOption
{
    Unchanged,
    AddBrackets,
    RemoveBrackets
}

public enum InsertTemplateStyleOption
{
    /// <summary>(cols) VALUES (vals) form.</summary>
    Values,
    /// <summary>(cols) SELECT col = val, ... form.</summary>
    SelectAssign
}
