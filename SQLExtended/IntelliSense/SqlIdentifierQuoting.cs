using System;
using System.Collections.Generic;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Decides when an identifier completion has to insert brackets. Free of the Visual Studio editor
/// assemblies so the test project can link it — the same split <c>ExportFileNaming</c> and
/// <c>MonitorCollection</c> exist for, and for the same reason: every mistake it can make is silent at the
/// point it is made. Over-quoting produces SQL that still runs; under-quoting produces SQL whose error
/// arrives later and names something else.
/// </summary>
public static class SqlIdentifierQuoting
{
    /// <summary>
    /// True when the name can be written without brackets *on its characters alone*. Deliberately stricter
    /// than T-SQL's own rule for a regular identifier, which also allows <c>@</c>, <c>#</c> and <c>$</c> —
    /// a column called <c>#ET</c> does in fact parse bare. It is bracketed anyway because over-bracketing
    /// produces SQL that runs and the reverse does not, and because the prefixes that genuinely must survive
    /// unbracketed (a table variable's <c>@</c>, a temp table's <c>#</c>) are ones where bracketing changes
    /// *meaning* rather than just appearance — those are handled by <see cref="QuoteObjectIfNeeded"/> rather
    /// than by loosening this.
    /// </summary>
    public static bool IsSimpleIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsDigit(name[0]))
            return false;

        foreach (char c in name)
            if (!char.IsLetterOrDigit(c) && c != '_')
                return false;

        return true;
    }

    /// <summary>
    /// Wraps an identifier in brackets when it cannot stand on its own, and leaves it alone when it can.
    /// This is the rule for columns and for anything else with no special prefix.
    ///
    /// Two things make it necessary, and neither announces itself:
    /// - **A name with a space in it is not a syntax error.** <c>SELECT t.Ongoing Qty</c> parses — as the
    ///   column <c>Ongoing</c> under the alias <c>Qty</c> — so it fails as "invalid column name Ongoing",
    ///   naming a column nobody typed, or on a table that does have an <c>Ongoing</c> column it silently
    ///   returns the wrong one under a surprising heading.
    /// - **A reserved word fails in every position**, and the message points at the punctuation around it
    ///   rather than at the word: <c>SELECT t.Order</c>, <c>INSERT INTO t (Order)</c>, <c>SET Order = 1</c>
    ///   and <c>GROUP BY Order</c> all fail. Non-reserved keywords (<c>Value</c>, <c>Name</c>, <c>Status</c>,
    ///   <c>Type</c>) are perfectly legal column names and are deliberately left bare — which is why this
    ///   consults <see cref="IsReservedKeyword"/> and not the completion keyword list, whose job is
    ///   different and whose contents would bracket a large share of ordinary column names.
    ///
    /// Brackets rather than double quotes, because they do not depend on QUOTED_IDENTIFIER. A <c>]</c>
    /// inside the name is doubled: legal in a name, and the one input that would otherwise close the
    /// bracket early and produce SQL that does not parse at all.
    /// </summary>
    public static string QuoteIfNeeded(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        return IsSimpleIdentifier(name) && !IsReservedKeyword(name) ? name : Bracket(name);
    }

    /// <summary>
    /// The rule for table, view and function names, which differs from <see cref="QuoteIfNeeded"/> in one
    /// way: **a leading <c>@</c> or <c>#</c> carries meaning and cannot be bracketed away.**
    ///
    /// <c>@Orders</c> is a table variable and <c>[@Orders]</c> is not a reference to it — it names a table
    /// called "@Orders". So a name beginning with <c>@</c> is returned untouched; variable names follow
    /// identifier rules anyway, so there is nothing there to escape.
    ///
    /// <c>#tmp</c> and <c>##tmp</c> are temporary tables. Brackets around those *are* legal
    /// (<c>SELECT * FROM [#tmp]</c> resolves normally), but every hand-written query spells them bare, and
    /// bracketing the most frequently completed name in the list would be a daily irritation for no gain.
    /// So the prefix is set aside and the rest of the name judged on its own; a temp table with a space in
    /// it still gets brackets, around the whole name including the prefix.
    /// </summary>
    public static string QuoteObjectIfNeeded(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        if (name[0] == '@')
            return name;

        string bare = name.StartsWith("##", StringComparison.Ordinal) ? name.Substring(2)
                    : name[0] == '#' ? name.Substring(1)
                    : null;

        if (bare != null)
            return IsSimpleIdentifier(bare) && !IsReservedKeyword(bare) ? name : Bracket(name);

        return QuoteIfNeeded(name);
    }

    private static string Bracket(string name) => "[" + name.Replace("]", "]]") + "]";

    /// <summary>True for a word T-SQL will not accept as an identifier without brackets.</summary>
    public static bool IsReservedKeyword(string word) =>
        !string.IsNullOrEmpty(word) && ReservedKeywords.Contains(word);

    /// <summary>
    /// SQL Server's reserved keywords. **The list is cross-checked against ScriptDom by
    /// <c>SqlIdentifierQuotingTests</c> in both directions** — every word here must actually be rejected as
    /// an identifier, and no word the parser rejects may be missing — so it is verifiable rather than
    /// folklore. Add to it only with that test passing; a wrong entry in either direction is invisible at
    /// runtime (a bracketed name that needed none looks fine, a bare one that needed brackets fails
    /// somewhere else).
    ///
    /// Deliberately *only* the reserved list. The ODBC and "future reserved" lists in the same
    /// documentation page are not enforced by the engine, and bracketing on them would quote a great many
    /// ordinary column names.
    ///
    /// Seven entries — SECURITYAUDIT, IDENTITYCOL, DUMP, LOAD, DISK, ROWGUIDCOL, PRECISION — are documented
    /// as reserved but *accepted* by ScriptDom as identifiers. They are kept, because bracketing a name that
    /// did not need it costs nothing while trusting the parser over the documentation risks the opposite.
    /// The test names them explicitly so the cross-check still fails on a word added here by mistake.
    /// </summary>
    private static readonly HashSet<string> ReservedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUTHORIZATION",
        "BACKUP", "BEGIN", "BETWEEN", "BREAK", "BROWSE", "BULK", "BY",
        "CASCADE", "CASE", "CHECK", "CHECKPOINT", "CLOSE", "CLUSTERED", "COALESCE", "COLLATE",
        "COLUMN", "COMMIT", "COMPUTE", "CONSTRAINT", "CONTAINS", "CONTAINSTABLE", "CONTINUE",
        "CONVERT", "CREATE", "CROSS", "CURRENT", "CURRENT_DATE", "CURRENT_TIME",
        "CURRENT_TIMESTAMP", "CURRENT_USER", "CURSOR",
        "DATABASE", "DBCC", "DEALLOCATE", "DECLARE", "DEFAULT", "DELETE", "DENY", "DESC", "DISK",
        "DISTINCT", "DISTRIBUTED", "DOUBLE", "DROP", "DUMP",
        "ELSE", "END", "ERRLVL", "ESCAPE", "EXCEPT", "EXEC", "EXECUTE", "EXISTS", "EXIT", "EXTERNAL",
        "FETCH", "FILE", "FILLFACTOR", "FOR", "FOREIGN", "FREETEXT", "FREETEXTTABLE", "FROM", "FULL",
        "FUNCTION",
        "GOTO", "GRANT", "GROUP",
        "HAVING", "HOLDLOCK",
        "IDENTITY", "IDENTITY_INSERT", "IDENTITYCOL", "IF", "IN", "INDEX", "INNER", "INSERT",
        "INTERSECT", "INTO", "IS",
        "JOIN",
        "KEY", "KILL",
        "LEFT", "LIKE", "LINENO", "LOAD",
        "MERGE",
        "NATIONAL", "NOCHECK", "NONCLUSTERED", "NOT", "NULL", "NULLIF",
        "OF", "OFF", "OFFSETS", "ON", "OPEN", "OPENDATASOURCE", "OPENQUERY", "OPENROWSET", "OPENXML",
        "OPTION", "OR", "ORDER", "OUTER", "OVER",
        "PERCENT", "PIVOT", "PLAN", "PRECISION", "PRIMARY", "PRINT", "PROC", "PROCEDURE", "PUBLIC",
        "RAISERROR", "READ", "READTEXT", "RECONFIGURE", "REFERENCES", "REPLICATION", "RESTORE",
        "RESTRICT", "RETURN", "REVERT", "REVOKE", "RIGHT", "ROLLBACK", "ROWCOUNT", "ROWGUIDCOL", "RULE",
        "SAVE", "SCHEMA", "SECURITYAUDIT", "SELECT", "SEMANTICKEYPHRASETABLE",
        "SEMANTICSIMILARITYDETAILSTABLE", "SEMANTICSIMILARITYTABLE", "SESSION_USER", "SET", "SETUSER",
        "SHUTDOWN", "SOME", "STATISTICS", "SYSTEM_USER",
        "TABLE", "TABLESAMPLE", "TEXTSIZE", "THEN", "TO", "TOP", "TRAN", "TRANSACTION", "TRIGGER",
        "TRUNCATE", "TRY_CONVERT", "TSEQUAL",
        "UNION", "UNIQUE", "UNPIVOT", "UPDATE", "UPDATETEXT", "USE", "USER",
        "VALUES", "VARYING", "VIEW",
        "WAITFOR", "WHEN", "WHERE", "WHILE", "WITH", "WRITETEXT",
    };
}
