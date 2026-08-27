using System;
using System.Collections.Generic;
using System.Linq;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Context flags indicating where a keyword is syntactically valid.
/// Multiple flags can be combined for keywords valid in several contexts.
/// </summary>
[Flags]
internal enum KeywordContext
{
    None = 0,

    /// <summary>Start of a new statement (beginning of batch or after semicolon/GO).</summary>
    StatementStart = 1 << 0,

    /// <summary>After SELECT column list (before FROM).</summary>
    AfterSelect = 1 << 1,

    /// <summary>After FROM or JOIN table reference.</summary>
    AfterFrom = 1 << 2,

    /// <summary>After WHERE condition or inside WHERE clause.</summary>
    AfterWhere = 1 << 3,

    /// <summary>After GROUP BY / HAVING.</summary>
    AfterGroupBy = 1 << 4,

    /// <summary>After ORDER BY.</summary>
    AfterOrderBy = 1 << 5,

    /// <summary>Inside an expression (comparison operators, functions, etc.).</summary>
    Expression = 1 << 6,

    /// <summary>Inside a BEGIN...END block or after control flow keywords.</summary>
    Block = 1 << 7,

    /// <summary>After JOIN (expecting ON).</summary>
    AfterJoin = 1 << 8,

    /// <summary>After INSERT INTO table (expecting column list or VALUES).</summary>
    AfterInsert = 1 << 9,

    /// <summary>After UPDATE table (expecting SET).</summary>
    AfterUpdate = 1 << 10,

    /// <summary>After SET clause in UPDATE.</summary>
    AfterSet = 1 << 11,

    /// <summary>Valid almost everywhere as a general keyword.</summary>
    General = StatementStart | AfterSelect | AfterFrom | AfterWhere | AfterGroupBy |
              AfterOrderBy | Expression | Block | AfterJoin | AfterInsert | AfterUpdate | AfterSet,
}

/// <summary>
/// Defines a T-SQL keyword for completion, including where it is syntactically valid.
/// </summary>
internal sealed class SqlKeyword
{
    public string Text { get; }
    public KeywordContext ValidContexts { get; }

    public SqlKeyword(string text, KeywordContext validContexts)
    {
        Text = text;
        ValidContexts = validContexts;
    }
}

/// <summary>
/// Master list of T-SQL keywords with context-awareness rules.
/// </summary>
internal static class SqlKeywords
{
    private static readonly List<SqlKeyword> AllKeywords = new List<SqlKeyword>
    {
        // DML statement starters
        new SqlKeyword("SELECT", KeywordContext.StatementStart | KeywordContext.Block | KeywordContext.Expression),
        new SqlKeyword("INSERT INTO", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("UPDATE", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("DELETE", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("DELETE FROM", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("MERGE", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("TRUNCATE TABLE", KeywordContext.StatementStart | KeywordContext.Block),

        // SELECT modifiers
        new SqlKeyword("DISTINCT", KeywordContext.AfterSelect),
        new SqlKeyword("TOP", KeywordContext.AfterSelect),
        new SqlKeyword("INTO", KeywordContext.AfterSelect),
        new SqlKeyword("AS", KeywordContext.AfterSelect | KeywordContext.AfterFrom | KeywordContext.Expression),

        // FROM / JOIN
        new SqlKeyword("FROM", KeywordContext.AfterSelect),
        new SqlKeyword("JOIN", KeywordContext.AfterFrom),
        new SqlKeyword("INNER JOIN", KeywordContext.AfterFrom),
        new SqlKeyword("LEFT JOIN", KeywordContext.AfterFrom),
        new SqlKeyword("LEFT OUTER JOIN", KeywordContext.AfterFrom),
        new SqlKeyword("RIGHT JOIN", KeywordContext.AfterFrom),
        new SqlKeyword("RIGHT OUTER JOIN", KeywordContext.AfterFrom),
        new SqlKeyword("FULL JOIN", KeywordContext.AfterFrom),
        new SqlKeyword("FULL OUTER JOIN", KeywordContext.AfterFrom),
        new SqlKeyword("CROSS JOIN", KeywordContext.AfterFrom),
        new SqlKeyword("CROSS APPLY", KeywordContext.AfterFrom),
        new SqlKeyword("OUTER APPLY", KeywordContext.AfterFrom),
        new SqlKeyword("ON", KeywordContext.AfterJoin),
        new SqlKeyword("WITH (NOLOCK)", KeywordContext.AfterFrom),

        // WHERE / conditions
        new SqlKeyword("WHERE", KeywordContext.AfterFrom),
        new SqlKeyword("AND", KeywordContext.AfterWhere | KeywordContext.AfterJoin),
        new SqlKeyword("OR", KeywordContext.AfterWhere),
        new SqlKeyword("NOT", KeywordContext.AfterWhere | KeywordContext.Expression),
        new SqlKeyword("IN", KeywordContext.AfterWhere | KeywordContext.Expression),
        new SqlKeyword("EXISTS", KeywordContext.AfterWhere | KeywordContext.Expression),
        new SqlKeyword("NOT EXISTS", KeywordContext.AfterWhere | KeywordContext.Expression),
        new SqlKeyword("BETWEEN", KeywordContext.AfterWhere | KeywordContext.Expression),
        new SqlKeyword("LIKE", KeywordContext.AfterWhere | KeywordContext.Expression),
        new SqlKeyword("IS NULL", KeywordContext.AfterWhere | KeywordContext.Expression),
        new SqlKeyword("IS NOT NULL", KeywordContext.AfterWhere | KeywordContext.Expression),

        // GROUP BY / HAVING / ORDER BY
        new SqlKeyword("GROUP BY", KeywordContext.AfterFrom | KeywordContext.AfterWhere),
        new SqlKeyword("HAVING", KeywordContext.AfterGroupBy),
        new SqlKeyword("ORDER BY", KeywordContext.AfterFrom | KeywordContext.AfterWhere | KeywordContext.AfterGroupBy),
        new SqlKeyword("ASC", KeywordContext.AfterOrderBy),
        new SqlKeyword("DESC", KeywordContext.AfterOrderBy),
        new SqlKeyword("OFFSET", KeywordContext.AfterOrderBy),
        new SqlKeyword("FETCH NEXT", KeywordContext.AfterOrderBy),
        new SqlKeyword("ROWS ONLY", KeywordContext.AfterOrderBy),

        // Set operations
        new SqlKeyword("UNION", KeywordContext.AfterFrom | KeywordContext.AfterWhere | KeywordContext.AfterOrderBy),
        new SqlKeyword("UNION ALL", KeywordContext.AfterFrom | KeywordContext.AfterWhere | KeywordContext.AfterOrderBy),
        new SqlKeyword("INTERSECT", KeywordContext.AfterFrom | KeywordContext.AfterWhere | KeywordContext.AfterOrderBy),
        new SqlKeyword("EXCEPT", KeywordContext.AfterFrom | KeywordContext.AfterWhere | KeywordContext.AfterOrderBy),

        // INSERT specific
        new SqlKeyword("VALUES", KeywordContext.AfterInsert),
        new SqlKeyword("OUTPUT", KeywordContext.AfterInsert | KeywordContext.AfterUpdate),
        new SqlKeyword("DEFAULT VALUES", KeywordContext.AfterInsert),

        // UPDATE specific
        new SqlKeyword("SET", KeywordContext.AfterUpdate),

        // DDL statement starters
        new SqlKeyword("CREATE TABLE", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("CREATE VIEW", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("CREATE PROCEDURE", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("CREATE FUNCTION", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("CREATE INDEX", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("CREATE NONCLUSTERED INDEX", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("CREATE UNIQUE INDEX", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("ALTER", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("ALTER TABLE", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("ALTER VIEW", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("ALTER PROCEDURE", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("ALTER FUNCTION", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("DROP TABLE", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("DROP VIEW", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("DROP PROCEDURE", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("DROP FUNCTION", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("DROP INDEX", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("ADD", KeywordContext.StatementStart),
        new SqlKeyword("CONSTRAINT", KeywordContext.StatementStart),

        // Control flow
        new SqlKeyword("IF", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("ELSE", KeywordContext.Block),
        new SqlKeyword("WHILE", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("BEGIN", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("END", KeywordContext.Block),
        new SqlKeyword("RETURN", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("BREAK", KeywordContext.Block),
        new SqlKeyword("CONTINUE", KeywordContext.Block),
        new SqlKeyword("GOTO", KeywordContext.Block),
        new SqlKeyword("WAITFOR", KeywordContext.StatementStart | KeywordContext.Block),

        // Transaction
        new SqlKeyword("BEGIN TRANSACTION", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("BEGIN TRAN", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("COMMIT", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("COMMIT TRANSACTION", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("ROLLBACK", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("ROLLBACK TRANSACTION", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("SAVE TRANSACTION", KeywordContext.StatementStart | KeywordContext.Block),

        // Error handling
        new SqlKeyword("BEGIN TRY", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("END TRY", KeywordContext.Block),
        new SqlKeyword("BEGIN CATCH", KeywordContext.Block),
        new SqlKeyword("END CATCH", KeywordContext.Block),
        new SqlKeyword("THROW", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("RAISERROR", KeywordContext.StatementStart | KeywordContext.Block),

        // Variables and output
        new SqlKeyword("DECLARE", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("SET", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("PRINT", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("EXEC", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("EXECUTE", KeywordContext.StatementStart | KeywordContext.Block),

        // CTE / subquery
        new SqlKeyword("WITH", KeywordContext.StatementStart),

        // CASE expression
        new SqlKeyword("CASE", KeywordContext.Expression | KeywordContext.AfterSelect),
        new SqlKeyword("WHEN", KeywordContext.Expression),
        new SqlKeyword("THEN", KeywordContext.Expression),
        new SqlKeyword("ELSE", KeywordContext.Expression | KeywordContext.Block),
        new SqlKeyword("END", KeywordContext.Expression | KeywordContext.Block),

        // OVER / window
        new SqlKeyword("OVER", KeywordContext.Expression),
        new SqlKeyword("PARTITION BY", KeywordContext.Expression),
        new SqlKeyword("ROWS BETWEEN", KeywordContext.Expression),

        // Data types (commonly used in DECLARE, CAST, CREATE)
        new SqlKeyword("INT", KeywordContext.General),
        new SqlKeyword("BIGINT", KeywordContext.General),
        new SqlKeyword("SMALLINT", KeywordContext.General),
        new SqlKeyword("TINYINT", KeywordContext.General),
        new SqlKeyword("BIT", KeywordContext.General),
        new SqlKeyword("DECIMAL", KeywordContext.General),
        new SqlKeyword("NUMERIC", KeywordContext.General),
        new SqlKeyword("FLOAT", KeywordContext.General),
        new SqlKeyword("MONEY", KeywordContext.General),
        new SqlKeyword("VARCHAR", KeywordContext.General),
        new SqlKeyword("NVARCHAR", KeywordContext.General),
        new SqlKeyword("CHAR", KeywordContext.General),
        new SqlKeyword("NCHAR", KeywordContext.General),
        new SqlKeyword("TEXT", KeywordContext.General),
        new SqlKeyword("NTEXT", KeywordContext.General),
        new SqlKeyword("DATETIME", KeywordContext.General),
        new SqlKeyword("DATETIME2", KeywordContext.General),
        new SqlKeyword("DATE", KeywordContext.General),
        new SqlKeyword("TIME", KeywordContext.General),
        new SqlKeyword("DATETIMEOFFSET", KeywordContext.General),
        new SqlKeyword("UNIQUEIDENTIFIER", KeywordContext.General),
        new SqlKeyword("VARBINARY", KeywordContext.General),
        new SqlKeyword("XML", KeywordContext.General),
        new SqlKeyword("SQL_VARIANT", KeywordContext.General),
        new SqlKeyword("TABLE", KeywordContext.General),

        // NULL / identity / constraint keywords
        new SqlKeyword("NULL", KeywordContext.General),
        new SqlKeyword("NOT NULL", KeywordContext.General),
        new SqlKeyword("DEFAULT", KeywordContext.General),
        new SqlKeyword("IDENTITY", KeywordContext.General),
        new SqlKeyword("PRIMARY KEY", KeywordContext.General),
        new SqlKeyword("FOREIGN KEY", KeywordContext.General),
        new SqlKeyword("REFERENCES", KeywordContext.General),
        new SqlKeyword("UNIQUE", KeywordContext.General),
        new SqlKeyword("CHECK", KeywordContext.General),
        new SqlKeyword("CLUSTERED", KeywordContext.General),
        new SqlKeyword("NONCLUSTERED", KeywordContext.General),

        // Built-in functions (GETDATE, DATEADD, STRING_SPLIT, …) are provided by the
        // dedicated SqlBuiltInFunctions catalog so they carry signatures and tooltips —
        // not duplicated here. Only the non-function language tokens remain below.

        // Global / configuration variables (no parentheses — not function calls)
        new SqlKeyword("@@IDENTITY", KeywordContext.Expression),
        new SqlKeyword("@@ROWCOUNT", KeywordContext.Expression),
        new SqlKeyword("@@ERROR", KeywordContext.Expression),
        new SqlKeyword("@@TRANCOUNT", KeywordContext.Expression),
        new SqlKeyword("@@VERSION", KeywordContext.Expression),
        new SqlKeyword("@@SPID", KeywordContext.Expression),
        new SqlKeyword("@@SERVERNAME", KeywordContext.Expression),
        new SqlKeyword("@@FETCH_STATUS", KeywordContext.Expression),
        new SqlKeyword("@@CURSOR_ROWS", KeywordContext.Expression),

        // Result-shaping clauses (not function calls)
        new SqlKeyword("FOR JSON", KeywordContext.Expression),
        new SqlKeyword("FOR XML", KeywordContext.Expression),

        // Misc
        new SqlKeyword("GO", KeywordContext.StatementStart),
        new SqlKeyword("USE", KeywordContext.StatementStart),
        new SqlKeyword("DBCC", KeywordContext.StatementStart | KeywordContext.Block),
        new SqlKeyword("OPTION", KeywordContext.AfterFrom | KeywordContext.AfterWhere | KeywordContext.AfterOrderBy),
    };

    /// <summary>
    /// Returns keywords valid for the given context.
    /// </summary>
    public static IReadOnlyList<SqlKeyword> GetKeywordsForContext(KeywordContext context)
    {
        if (context == KeywordContext.None)
            return AllKeywords; // Fallback: show all

        return AllKeywords.Where(k => (k.ValidContexts & context) != 0).ToList();
    }

    /// <summary>
    /// Individual keyword <em>words</em> (multi-word entries split apart) for fast type-time
    /// recasing — e.g. "INNER JOIN" contributes INNER and JOIN, "WITH (NOLOCK)" contributes
    /// WITH and NOLOCK. Only purely-alphabetic tokens of length ≥ 2 are included, so identifiers
    /// (which carry digits/underscores) and single letters never collide with the set.
    /// </summary>
    private static readonly HashSet<string> KeywordWordSet = BuildKeywordWordSet();

    /// <summary>True if <paramref name="word"/> is a standalone T-SQL keyword word (case-insensitive).</summary>
    public static bool IsKeywordWord(string word) =>
        !string.IsNullOrEmpty(word) && KeywordWordSet.Contains(word);

    private static HashSet<string> BuildKeywordWordSet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kw in AllKeywords)
        {
            string text = kw.Text;
            int i = 0;
            while (i < text.Length)
            {
                if (char.IsLetter(text[i]))
                {
                    int start = i;
                    while (i < text.Length && char.IsLetter(text[i]))
                        i++;
                    if (i - start >= 2)
                        set.Add(text.Substring(start, i - start));
                }
                else
                {
                    i++;
                }
            }
        }
        return set;
    }

    /// <summary>
    /// Determines the keyword context from the text before the cursor.
    /// </summary>
    public static KeywordContext DetectContext(string textBeforeCursor)
    {
        if (string.IsNullOrWhiteSpace(textBeforeCursor))
            return KeywordContext.StatementStart;

        // Trim and get the relevant tail for analysis
        string text = textBeforeCursor;
        if (text.Length > 500)
            text = text.Substring(text.Length - 500);

        // Strip the trailing partial identifier the user is typing so we classify
        // based on what's *before* the word. Without this, "SELECT * F" (user typing
        // "FROM") doesn't end with any clause keyword and falls through to
        // StatementStart — which offers "DELETE FROM", then the editor's
        // subsequence filter matches "F" against "DELETE FROM".
        int end = text.Length;
        while (end > 0)
        {
            char c = text[end - 1];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#')
                end--;
            else
                break;
        }
        text = text.Substring(0, end).TrimEnd();

        string upper = text.ToUpperInvariant();

        // After a comma — depends on what clause we're in
        if (text.EndsWith(","))
        {
            if (ContainsKeywordBefore(upper, "ORDER BY", "GROUP BY"))
                return KeywordContext.AfterOrderBy | KeywordContext.AfterGroupBy;
            if (ContainsKeywordBefore(upper, "SELECT"))
                return KeywordContext.AfterSelect | KeywordContext.Expression;
            if (ContainsKeywordBefore(upper, "SET"))
                return KeywordContext.AfterSet;
            return KeywordContext.Expression;
        }

        // After SET (in UPDATE context)
        if (EndsWithKeyword(upper, "SET"))
            return KeywordContext.AfterUpdate | KeywordContext.AfterSet;

        // After INSERT INTO ... (column list or VALUES expected)
        if (EndsWithKeyword(upper, "INSERT INTO") || EndsAfterInsertTable(upper))
            return KeywordContext.AfterInsert;

        // After UPDATE ... (SET expected)
        if (EndsAfterUpdateTable(upper))
            return KeywordContext.AfterUpdate;

        // After ORDER BY
        if (EndsWithKeyword(upper, "ORDER BY"))
            return KeywordContext.AfterOrderBy;

        // After GROUP BY
        if (EndsWithKeyword(upper, "GROUP BY"))
            return KeywordContext.AfterGroupBy;

        // After HAVING
        if (EndsWithKeyword(upper, "HAVING"))
            return KeywordContext.AfterGroupBy;

        // After WHERE, AND, OR
        if (EndsWithKeyword(upper, "WHERE") || EndsWithKeyword(upper, "AND") || EndsWithKeyword(upper, "OR"))
            return KeywordContext.AfterWhere;

        // After ON (join condition)
        if (EndsWithKeyword(upper, "ON"))
            return KeywordContext.AfterJoin;

        // After FROM, JOIN keywords
        if (EndsWithKeyword(upper, "FROM") || EndsWithKeyword(upper, "JOIN") ||
            EndsWithKeyword(upper, "INNER JOIN") || EndsWithKeyword(upper, "LEFT JOIN") ||
            EndsWithKeyword(upper, "RIGHT JOIN") || EndsWithKeyword(upper, "CROSS JOIN") ||
            EndsWithKeyword(upper, "FULL JOIN") || EndsWithKeyword(upper, "CROSS APPLY") ||
            EndsWithKeyword(upper, "OUTER APPLY"))
            return KeywordContext.AfterFrom;

        // After FROM + table reference (post-table in FROM clause)
        if (IsAfterTableInFromClause(upper))
        {
            // When the controlling clause is a JOIN (not a plain FROM), the next token
            // is the ON condition — surface AfterJoin so "ON" is offered and exact-matched.
            // Without this, typing "ON" finds no matching keyword and the item manager
            // hard-selects an unrelated first item.
            return ControllingClauseIsJoin(upper)
                ? KeywordContext.AfterFrom | KeywordContext.AfterJoin
                : KeywordContext.AfterFrom;
        }

        // After SELECT
        if (EndsWithKeyword(upper, "SELECT") || EndsWithKeyword(upper, "SELECT DISTINCT") ||
            EndsWithSelectTop(upper))
            return KeywordContext.AfterSelect;

        // After comparison operators
        if (text.TrimEnd().Length > 0)
        {
            string trimmed = text.TrimEnd();
            char last = trimmed[trimmed.Length - 1];
            if (last == '=' || last == '>' || last == '<')
                return KeywordContext.Expression;
        }

        // After open paren
        if (text.TrimEnd().EndsWith("("))
            return KeywordContext.Expression | KeywordContext.StatementStart;

        // After semicolon or start of text
        if (text.TrimEnd().EndsWith(";") || string.IsNullOrWhiteSpace(text))
            return KeywordContext.StatementStart;

        // Fallback: locate the most recent top-level clause keyword in the statement
        // and classify based on that. Prevents unrelated StatementStart keywords
        // (e.g., "DELETE FROM") from appearing mid-statement.
        var byClause = DetectByLastClauseKeyword(upper);
        if (byClause != KeywordContext.None)
            return byClause;

        // Default: statement start + block (could be anywhere in a BEGIN...END)
        return KeywordContext.StatementStart | KeywordContext.Block;
    }

    /// <summary>
    /// Finds the most recent top-level clause keyword in the text and returns the
    /// matching context. Used as a fallback when the text doesn't end with a
    /// recognizable clause keyword (e.g., mid-expression like "SELECT *").
    /// </summary>
    private static KeywordContext DetectByLastClauseKeyword(string upper)
    {
        // Ordered by specificity — longer/multi-word keywords first to avoid
        // partial matches (e.g., matching "SET" inside "OFFSET").
        var clauses = new (string Keyword, KeywordContext Context)[]
        {
            ("ORDER BY", KeywordContext.AfterOrderBy),
            ("GROUP BY", KeywordContext.AfterGroupBy),
            ("HAVING", KeywordContext.AfterGroupBy),
            ("WHERE", KeywordContext.AfterWhere),
            ("INSERT INTO", KeywordContext.AfterInsert),
            ("UPDATE", KeywordContext.AfterUpdate),
            ("FROM", KeywordContext.AfterFrom),
            ("JOIN", KeywordContext.AfterFrom),
            ("SELECT", KeywordContext.AfterSelect | KeywordContext.Expression),
        };

        int bestIdx = -1;
        KeywordContext best = KeywordContext.None;

        foreach (var (kw, ctx) in clauses)
        {
            int idx = LastIndexOfWholeWord(upper, kw);
            if (idx > bestIdx)
            {
                bestIdx = idx;
                best = ctx;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the index of the last whole-word occurrence of <paramref name="keyword"/>
    /// in <paramref name="text"/>, or -1 if not found. Word boundaries are non-word chars.
    /// </summary>
    private static int LastIndexOfWholeWord(string text, string keyword)
    {
        int searchFrom = text.Length;
        while (searchFrom > 0)
        {
            int idx = text.LastIndexOf(keyword, searchFrom - 1, StringComparison.Ordinal);
            if (idx < 0)
                return -1;

            bool leftOk = idx == 0 || !IsWordChar(text[idx - 1]);
            int afterIdx = idx + keyword.Length;
            bool rightOk = afterIdx >= text.Length || !IsWordChar(text[afterIdx]);

            if (leftOk && rightOk)
                return idx;

            searchFrom = idx;
        }
        return -1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool EndsWithKeyword(string upper, string keyword)
    {
        if (!upper.EndsWith(keyword))
            return false;

        int beforeIdx = upper.Length - keyword.Length - 1;
        // Must be preceded by whitespace or start of string
        return beforeIdx < 0 || !char.IsLetterOrDigit(upper[beforeIdx]);
    }

    private static bool EndsWithSelectTop(string upper)
    {
        // Matches SELECT TOP N at end
        int idx = upper.LastIndexOf("SELECT TOP");
        if (idx < 0) return false;
        string after = upper.Substring(idx + "SELECT TOP".Length).TrimStart();
        // Should be digits optionally followed by whitespace
        foreach (char c in after)
        {
            if (!char.IsDigit(c) && !char.IsWhiteSpace(c))
                return false;
        }
        return true;
    }

    private static bool ContainsKeywordBefore(string upper, params string[] keywords)
    {
        foreach (string kw in keywords)
        {
            if (upper.LastIndexOf(kw, StringComparison.Ordinal) >= 0)
                return true;
        }
        return false;
    }

    private static bool EndsAfterInsertTable(string upper)
    {
        // Pattern: INSERT INTO <table> at end
        int idx = upper.LastIndexOf("INSERT INTO", StringComparison.Ordinal);
        if (idx < 0) return false;
        string after = upper.Substring(idx + "INSERT INTO".Length).TrimStart();
        // Should end with a table name (word chars and dots)
        return after.Length > 0 && !after.Contains("VALUES") && !after.Contains("SELECT");
    }

    private static bool EndsAfterUpdateTable(string upper)
    {
        // Pattern: UPDATE <table> at end (no SET yet)
        int idx = upper.LastIndexOf("UPDATE", StringComparison.Ordinal);
        if (idx < 0) return false;
        string after = upper.Substring(idx + "UPDATE".Length).TrimStart();
        return after.Length > 0 && !after.Contains("SET");
    }

    /// <summary>
    /// Returns true when the most recent table-introducing keyword is a JOIN rather than
    /// a plain FROM, i.e. the cursor sits after a joined table and an ON clause is expected.
    /// CROSS JOIN / CROSS APPLY / OUTER APPLY take no ON, so they don't count.
    /// </summary>
    private static bool ControllingClauseIsJoin(string upper)
    {
        int joinIdx = LastIndexOfWholeWord(upper, "JOIN");
        int fromIdx = LastIndexOfWholeWord(upper, "FROM");
        if (joinIdx <= fromIdx)
            return false;

        // Exclude CROSS JOIN (its preceding token is CROSS) — those use no ON.
        string beforeJoin = upper.Substring(0, joinIdx).TrimEnd();
        if (beforeJoin.EndsWith("CROSS"))
            return false;

        return true;
    }

    private static bool IsAfterTableInFromClause(string upper)
    {
        // Check if the last significant keyword was FROM/JOIN and we've had a table name since
        int fromIdx = Math.Max(
            upper.LastIndexOf("FROM ", StringComparison.Ordinal),
            upper.LastIndexOf("JOIN ", StringComparison.Ordinal));

        if (fromIdx < 0) return false;

        string afterFrom = upper.Substring(fromIdx).TrimEnd();
        // If after FROM/JOIN + table name, and no WHERE/ORDER/GROUP yet
        return !afterFrom.Contains("WHERE") && !afterFrom.Contains("ORDER BY") &&
               !afterFrom.Contains("GROUP BY") && !afterFrom.Contains("HAVING") &&
               afterFrom.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length >= 2;
    }
}
