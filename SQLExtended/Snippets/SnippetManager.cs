using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SQLExtended.Snippets;

/// <summary>
/// A T-SQL code snippet with a short prefix that expands into a template.
/// </summary>
internal sealed class SqlSnippet
{
    /// <summary>Short trigger code (e.g., "sel", "selt", "cte"). This is the only thing that triggers the snippet in completion.</summary>
    [JsonProperty("prefix")]
    public string Code { get; set; }

    /// <summary>Human-readable title shown in the completion list.</summary>
    [JsonProperty("title")]
    public string Title { get; set; }

    /// <summary>Description shown in the completion tooltip.</summary>
    [JsonProperty("description")]
    public string Description { get; set; }

    /// <summary>
    /// The template body to insert. Newlines are literal \n in JSON.
    /// Supports $placeholder$ variables that are resolved at insertion time:
    /// $date$, $time$, $datetime$, $year$, $month$, $day$,
    /// $user$, $machine$, $dbname$, $server$, $guid$.
    /// Custom placeholders (any name not in the built-in list) become interactive
    /// tab stops when the snippet is expanded via the VS expansion engine.
    /// </summary>
    [JsonProperty("body")]
    public string Body { get; set; }

    /// <summary>
    /// Default values for custom (non-system) placeholders.
    /// Keys are placeholder names without $ delimiters (e.g., "count", "table").
    /// Null or empty means no custom placeholder defaults.
    /// </summary>
    [JsonProperty("defaults", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, string> Defaults { get; set; }
}

/// <summary>
/// Manages loading, saving, and merging of T-SQL snippets.
/// Ships with built-in defaults; users can add/edit/delete custom snippets
/// persisted to %APPDATA%\SQLExtended\SSMS\snippets.json.
/// </summary>
internal sealed class SnippetManager
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SQLExtended", "SSMS");

    private static readonly string UserSnippetsPath =
        Path.Combine(SettingsDir, "snippets.json");

    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        Formatting = global::Newtonsoft.Json.Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    private static SnippetManager _instance;
    private List<SqlSnippet> _snippets;

    public static SnippetManager Instance => _instance ?? (_instance = new SnippetManager());

    private SnippetManager()
    {
        _snippets = LoadSnippets();
    }

    /// <summary>
    /// All available snippets (built-in + user-defined, user overrides built-in by prefix).
    /// </summary>
    public IReadOnlyList<SqlSnippet> Snippets => _snippets;

    /// <summary>
    /// Finds a snippet by exact code match (case-insensitive).
    /// </summary>
    public SqlSnippet FindByCode(string code)
    {
        if (string.IsNullOrEmpty(code))
            return null;

        return _snippets.FirstOrDefault(s =>
            string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Adds or updates a user snippet and saves to disk.
    /// </summary>
    public void SaveSnippet(SqlSnippet snippet)
    {
        var existing = _snippets.FindIndex(s =>
            string.Equals(s.Code, snippet.Code, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
            _snippets[existing] = snippet;
        else
            _snippets.Add(snippet);

        SaveUserSnippets();
    }

    /// <summary>
    /// Removes a snippet by code and saves to disk.
    /// </summary>
    public bool RemoveSnippet(string code)
    {
        int removed = _snippets.RemoveAll(s =>
            string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
            SaveUserSnippets();

        return removed > 0;
    }

    /// <summary>
    /// Reloads snippets from disk (defaults + user overrides).
    /// </summary>
    public void Reload()
    {
        _snippets = LoadSnippets();
    }

    private List<SqlSnippet> LoadSnippets()
    {
        // Start with built-in defaults
        var defaults = LoadDefaults();
        var result = new List<SqlSnippet>(defaults);

        // Merge user snippets (override by prefix)
        var userSnippets = LoadUserSnippets();
        if (userSnippets != null)
        {
            foreach (var snippet in userSnippets)
            {
                var existing = result.FindIndex(s =>
                    string.Equals(s.Code, snippet.Code, StringComparison.OrdinalIgnoreCase));

                if (existing >= 0)
                    result[existing] = snippet;
                else
                    result.Add(snippet);
            }
        }

        return result;
    }

    private static List<SqlSnippet> LoadDefaults()
    {
        return new List<SqlSnippet>
        {
            new SqlSnippet
            {
                Code = "sel",
                Title = "SELECT *",
                Description = "SELECT * FROM table",
                Body = "SELECT *\nFROM $table$",
                Defaults = new Dictionary<string, string> { { "table", "TableName" } }
            },
            new SqlSnippet
            {
                Code = "selt",
                Title = "SELECT TOP",
                Description = "SELECT TOP n rows FROM table",
                Body = "SELECT TOP $count$ *\nFROM $table$",
                Defaults = new Dictionary<string, string> { { "count", "100" }, { "table", "TableName" } }
            },
            new SqlSnippet
            {
                Code = "selc",
                Title = "SELECT COUNT",
                Description = "SELECT COUNT(*) FROM table",
                Body = "SELECT COUNT(*)\nFROM $table$",
                Defaults = new Dictionary<string, string> { { "table", "TableName" } }
            },
            new SqlSnippet
            {
                Code = "seld",
                Title = "SELECT DISTINCT",
                Description = "SELECT DISTINCT columns FROM table",
                Body = "SELECT DISTINCT $columns$\nFROM $table$",
                Defaults = new Dictionary<string, string> { { "columns", "Column1" }, { "table", "TableName" } }
            },
            new SqlSnippet
            {
                Code = "ins",
                Title = "INSERT INTO",
                Description = "INSERT INTO table (columns) VALUES (values)",
                Body = "INSERT INTO $table$ ($columns$)\nVALUES ($values$)",
                Defaults = new Dictionary<string, string> { { "table", "TableName" }, { "columns", "Col1" }, { "values", "Value1" } }
            },
            new SqlSnippet
            {
                Code = "upd",
                Title = "UPDATE SET",
                Description = "UPDATE table SET column = value WHERE condition",
                Body = "UPDATE $table$\nSET $column$ = $value$\nWHERE $condition$",
                Defaults = new Dictionary<string, string> { { "table", "TableName" }, { "column", "Col1" }, { "value", "NewValue" }, { "condition", "ID = 1" } }
            },
            new SqlSnippet
            {
                Code = "del",
                Title = "DELETE FROM",
                Description = "DELETE FROM table WHERE condition",
                Body = "DELETE FROM $table$\nWHERE $condition$",
                Defaults = new Dictionary<string, string> { { "table", "TableName" }, { "condition", "ID = 1" } }
            },
            new SqlSnippet
            {
                Code = "cte",
                Title = "CTE (WITH)",
                Description = "Common Table Expression template",
                Body = "WITH $cteName$ AS (\n\tSELECT $columns$\n\tFROM $table$\n)\nSELECT *\nFROM $cteName$",
                Defaults = new Dictionary<string, string> { { "cteName", "cte" }, { "columns", "*" }, { "table", "TableName" } }
            },
            new SqlSnippet
            {
                Code = "iff",
                Title = "IF EXISTS",
                Description = "IF EXISTS (SELECT 1 FROM ...) BEGIN ... END",
                Body = "IF EXISTS (SELECT 1 FROM $table$ WHERE $condition$)\nBEGIN\n\t\nEND",
                Defaults = new Dictionary<string, string> { { "table", "TableName" }, { "condition", "ID = 1" } }
            },
            new SqlSnippet
            {
                Code = "ifn",
                Title = "IF NOT EXISTS",
                Description = "IF NOT EXISTS (SELECT 1 FROM ...) BEGIN ... END",
                Body = "IF NOT EXISTS (SELECT 1 FROM $table$ WHERE $condition$)\nBEGIN\n\t\nEND",
                Defaults = new Dictionary<string, string> { { "table", "TableName" }, { "condition", "ID = 1" } }
            },
            new SqlSnippet
            {
                Code = "beg",
                Title = "BEGIN TRY...CATCH",
                Description = "BEGIN TRY / BEGIN CATCH error handling block",
                Body = "BEGIN TRY\n\t\nEND TRY\nBEGIN CATCH\n\tSELECT\n\t\tERROR_NUMBER() AS ErrorNumber,\n\t\tERROR_MESSAGE() AS ErrorMessage,\n\t\tERROR_SEVERITY() AS ErrorSeverity,\n\t\tERROR_STATE() AS ErrorState,\n\t\tERROR_LINE() AS ErrorLine,\n\t\tERROR_PROCEDURE() AS ErrorProcedure;\nEND CATCH"
            },
            new SqlSnippet
            {
                Code = "tran",
                Title = "BEGIN TRANSACTION",
                Description = "Transaction with TRY/CATCH and ROLLBACK",
                Body = "BEGIN TRANSACTION\nBEGIN TRY\n\t\n\tCOMMIT TRANSACTION\nEND TRY\nBEGIN CATCH\n\tROLLBACK TRANSACTION;\n\tTHROW;\nEND CATCH"
            },
            new SqlSnippet
            {
                Code = "temp",
                Title = "Temp Table",
                Description = "CREATE temp table with columns",
                Body = "CREATE TABLE #$tableName$ (\n\tID INT IDENTITY(1,1) NOT NULL,\n\t\n)",
                Defaults = new Dictionary<string, string> { { "tableName", "TempTable" } }
            },
            new SqlSnippet
            {
                Code = "tabv",
                Title = "Table Variable",
                Description = "DECLARE table variable",
                Body = "DECLARE @$varName$ TABLE (\n\tID INT IDENTITY(1,1) NOT NULL,\n\t\n)",
                Defaults = new Dictionary<string, string> { { "varName", "TableVar" } }
            },
            new SqlSnippet
            {
                Code = "cur",
                Title = "CURSOR",
                Description = "DECLARE and use a cursor",
                Body = "DECLARE @$fetchVar$ INT\n\nDECLARE $cursorName$ CURSOR LOCAL FAST_FORWARD FOR\n\tSELECT $fetchVar$\n\tFROM $table$\n\nOPEN $cursorName$\nFETCH NEXT FROM $cursorName$ INTO @$fetchVar$\n\nWHILE @@FETCH_STATUS = 0\nBEGIN\n\t\n\tFETCH NEXT FROM $cursorName$ INTO @$fetchVar$\nEND\n\nCLOSE $cursorName$\nDEALLOCATE $cursorName$",
                Defaults = new Dictionary<string, string> { { "cursorName", "cur_Name" }, { "fetchVar", "Value" }, { "table", "TableName" } }
            },
            new SqlSnippet
            {
                Code = "merge",
                Title = "MERGE",
                Description = "MERGE statement (upsert pattern)",
                Body = "MERGE INTO $targetTable$ AS target\nUSING $sourceTable$ AS source\n\tON target.$joinColumn$ = source.$joinColumn$\nWHEN MATCHED THEN\n\tUPDATE SET\n\t\ttarget.$column$ = source.$column$\nWHEN NOT MATCHED BY TARGET THEN\n\tINSERT ($column$)\n\tVALUES (source.$column$);",
                Defaults = new Dictionary<string, string> { { "targetTable", "TargetTable" }, { "sourceTable", "SourceTable" }, { "joinColumn", "ID" }, { "column", "Col1" } }
            },
            new SqlSnippet
            {
                Code = "piv",
                Title = "PIVOT",
                Description = "PIVOT query template",
                Body = "SELECT *\nFROM (\n\tSELECT $columns$\n\tFROM $table$\n) AS src\nPIVOT (\n\tCOUNT($aggColumn$)\n\tFOR $pivotColumn$ IN ([$pivotValue$])\n) AS pvt",
                Defaults = new Dictionary<string, string> { { "columns", "*" }, { "table", "TableName" }, { "aggColumn", "Value" }, { "pivotColumn", "Category" }, { "pivotValue", "Val1" } }
            },
            new SqlSnippet
            {
                Code = "page",
                Title = "Pagination",
                Description = "OFFSET/FETCH pagination query",
                Body = "SELECT $columns$\nFROM $table$\nORDER BY $orderBy$\nOFFSET $offset$ ROWS\nFETCH NEXT $pageSize$ ROWS ONLY",
                Defaults = new Dictionary<string, string> { { "columns", "*" }, { "table", "TableName" }, { "orderBy", "ID" }, { "offset", "0" }, { "pageSize", "50" } }
            },
            new SqlSnippet
            {
                Code = "idx",
                Title = "CREATE INDEX",
                Description = "CREATE NONCLUSTERED INDEX on table",
                Body = "CREATE NONCLUSTERED INDEX [IX_$table$_$column$]\nON $schema$.$table$($column$) \nINCLUDE ($includeColumn$)",
                Defaults = new Dictionary<string, string> { { "table", "TableName" }, { "column", "Col1" }, { "schema", "dbo" }, { "includeColumn", "Col2" } }
            },
            new SqlSnippet
            {
                Code = "whr",
                Title = "WHERE EXISTS subquery",
                Description = "WHERE EXISTS (SELECT 1 FROM ...)",
                Body = "WHERE EXISTS (\n\tSELECT 1\n\tFROM $table$\n\tWHERE $condition$\n)",
                Defaults = new Dictionary<string, string> { { "table", "TableName" }, { "condition", "ID = 1" } }
            },
            new SqlSnippet
            {
                Code = "hdr",
                Title = "Script Header",
                Description = "Script header comment with date, author, and database. Uses $date$, $user$, $dbname$ placeholders.",
                Body = "-- ============================================================\n-- Author:      $user$\n-- Create Date: $date$\n-- Database:    $dbname$\n-- Description: $description$\n-- ============================================================\n",
                Defaults = new Dictionary<string, string> { { "description", "Description" } }
            },
            new SqlSnippet
            {
                Code = "chg",
                Title = "Change Log Entry",
                Description = "Change log comment line. Uses $date$ and $user$ placeholders.",
                Body = "-- $date$ | $user$ | $change$",
                Defaults = new Dictionary<string, string> { { "change", "Description of change" } }
            },
            new SqlSnippet
            {
                Code = "seldb",
                Title = "USE Database",
                Description = "USE current database. Uses $dbname$ placeholder.",
                Body = "USE [$dbname$]\nGO\n"
            },
        };
    }

    private List<SqlSnippet> LoadUserSnippets()
    {
        try
        {
            if (File.Exists(UserSnippetsPath))
            {
                string json = File.ReadAllText(UserSnippetsPath);
                return JsonConvert.DeserializeObject<List<SqlSnippet>>(json, JsonSettings)
                    ?? new List<SqlSnippet>();
            }
        }
        catch
        {
            // Corrupted file — ignore user snippets
        }

        return null;
    }

    private void SaveUserSnippets()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonConvert.SerializeObject(_snippets, JsonSettings);
            File.WriteAllText(UserSnippetsPath, json);
        }
        catch
        {
            // Best effort
        }
    }
}
