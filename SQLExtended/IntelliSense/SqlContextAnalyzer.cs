using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Analyzes the cursor position in a SQL query to determine what kind of
/// completion should be offered: table names, column names, or nothing.
/// </summary>
internal static class SqlContextAnalyzer
{
    internal enum CompletionType
    {
        None,
        TableName,
        ColumnAfterDot,
        ColumnInContext,
        JoinOnCondition,
        ProcedureName,
        Keyword,
        InsertColumnTemplate,
        DatabaseName,
        FunctionArgument,
        DbccCommand,
        AlterTarget,
        AlterTableAction,
        AlterIndexAction,
        AlterIndexName,
        CollationName,
        StarExpansion
    }

    internal sealed class AnalysisResult
    {
        public CompletionType Type { get; set; }

        /// <summary>
        /// For ColumnAfterDot: the identifier before the dot (alias or table name).
        /// </summary>
        public string DotPrefix { get; set; }

        /// <summary>
        /// The full SQL text from the start of the current statement to the cursor,
        /// used for alias resolution.
        /// </summary>
        public string StatementText { get; set; }

        /// <summary>For InsertColumnTemplate: target database for cross-database inserts (may be null).</summary>
        public string TargetDatabase { get; set; }

        /// <summary>For InsertColumnTemplate: target schema (may be null).</summary>
        public string TargetSchema { get; set; }

        /// <summary>For InsertColumnTemplate: target table name.</summary>
        public string TargetTable { get; set; }

        /// <summary>For FunctionArgument: the kind of value expected (data type, datepart).</summary>
        public SqlArgKind ArgumentKind { get; set; }

        /// <summary>
        /// For JoinOnCondition: the reference name (alias or table) of the just-joined
        /// table — the table immediately before the ON that owns this join condition.
        /// </summary>
        public string JoinedTableReference { get; set; }

        /// <summary>
        /// For StarExpansion: length of the "*" or "alias.*" token ending at the cursor —
        /// the text a committed expansion replaces. The alias (if any) is in DotPrefix.
        /// </summary>
        public int StarReplaceLength { get; set; }
    }

    /// <summary>
    /// Analyzes the text before the cursor to determine the completion context.
    /// </summary>
    public static AnalysisResult Analyze(string fullText, int cursorPosition)
    {
        if (string.IsNullOrEmpty(fullText) || cursorPosition <= 0)
            return new AnalysisResult { Type = CompletionType.None };

        // Inside a comment (-- line or /* block */) — never offer suggestions.
        if (IsInsideComment(fullText, cursorPosition))
            return new AnalysisResult { Type = CompletionType.None };

        // Get the statement containing the cursor (delimited by GO or semicolons)
        string statementText = ExtractCurrentStatement(fullText, cursorPosition);
        string textBeforeCursor = fullText.Substring(
            Math.Max(0, cursorPosition - Math.Min(cursorPosition, 500)),
            Math.Min(cursorPosition, 500));

        // 0. Cursor right after a SELECT-list "*" (or "alias.*") — offer to expand the
        // star into the explicit column list. Only meaningful when tables are in scope.
        var starMatch = SelectStarPattern.Match(textBeforeCursor);
        if (starMatch.Success && HasFromClause(statementText))
        {
            return new AnalysisResult
            {
                Type = CompletionType.StarExpansion,
                DotPrefix = starMatch.Groups["bp"].Success ? starMatch.Groups["bp"].Value
                          : starMatch.Groups["wp"].Success ? starMatch.Groups["wp"].Value : null,
                StarReplaceLength = starMatch.Groups["tok"].Length,
                StatementText = statementText
            };
        }

        // 1. Check for dot-triggered column completion: "alias." or "table."
        // (uses raw textBeforeCursor because it specifically needs the trailing dot+partial)
        var dotResult = CheckDotContext(textBeforeCursor);
        if (dotResult != null)
        {
            dotResult.StatementText = statementText;
            return dotResult;
        }

        // 1b. Inside a built-in function call whose argument draws from a known set —
        // e.g. CONVERT(<data type>, …), DATEADD(<datepart>, …), CAST(x AS <data type>).
        var argResult = CheckFunctionArgumentContext(fullText, cursorPosition);
        if (argResult != null)
        {
            argResult.StatementText = statementText;
            return argResult;
        }

        // For table/procedure/column-clause detection, strip the trailing partial
        // identifier the user is typing. This lets Ctrl+Space re-trigger completion
        // mid-word (e.g., "FROM Time<cursor>" is equivalent to "FROM <cursor>").
        string textBeforeIdent = StripTrailingPartialIdentifier(textBeforeCursor);

        // 2. Check if we're in a procedure-name-expecting context (EXEC, EXECUTE)
        if (IsProcedureContext(textBeforeIdent))
        {
            return new AnalysisResult
            {
                Type = CompletionType.ProcedureName,
                StatementText = statementText
            };
        }

        // 2b. USE <database> — only database names belong here.
        if (UseContextPattern.IsMatch(textBeforeIdent))
        {
            return new AnalysisResult
            {
                Type = CompletionType.DatabaseName,
                StatementText = statementText
            };
        }

        // 2c. DBCC <command> — offer the DBCC command names.
        if (DbccContextPattern.IsMatch(textBeforeIdent))
        {
            return new AnalysisResult
            {
                Type = CompletionType.DbccCommand,
                StatementText = statementText
            };
        }

        // 2c-i. COLLATE <collation name> — offer server collations (database default first).
        if (CollateContextPattern.IsMatch(textBeforeIdent))
        {
            return new AnalysisResult
            {
                Type = CompletionType.CollationName,
                StatementText = statementText
            };
        }

        // 2d. ALTER TABLE [schema.]name <action> — offer the table sub-actions.
        // Checked before the bare ALTER target so a named table goes to actions, and
        // before object-name detection (which only matches the bare "ALTER TABLE ").
        if (AlterTableActionPattern.IsMatch(textBeforeIdent))
        {
            return new AnalysisResult
            {
                Type = CompletionType.AlterTableAction,
                StatementText = statementText
            };
        }

        // 2d-i. ALTER INDEX {name | ALL} ON [schema.]object <action> — offer index actions.
        if (AlterIndexActionPattern.IsMatch(textBeforeIdent))
        {
            return new AnalysisResult
            {
                Type = CompletionType.AlterIndexAction,
                StatementText = statementText
            };
        }

        // 2d-ii. ALTER INDEX {name | ALL} ON <object> — the object is a table/view name.
        if (AlterIndexOnPattern.IsMatch(textBeforeIdent))
        {
            return new AnalysisResult
            {
                Type = CompletionType.TableName,
                StatementText = statementText
            };
        }

        // 2d-iii. ALTER INDEX <index name | ALL> — offer the ALL keyword.
        if (AlterIndexNamePattern.IsMatch(textBeforeIdent))
        {
            return new AnalysisResult
            {
                Type = CompletionType.AlterIndexName,
                StatementText = statementText
            };
        }

        // 2e. ALTER <object kind> — offer the alterable object kinds.
        if (AlterTargetPattern.IsMatch(textBeforeIdent))
        {
            return new AnalysisResult
            {
                Type = CompletionType.AlterTarget,
                StatementText = statementText
            };
        }

        // 3a. Check for "INSERT INTO [schema.]table " — offer all-columns template.
        // Must be checked before generic keyword context so the space-after-table
        // doesn't fall through to keyword completion.
        var insertMatch = InsertTableContextPattern.Match(textBeforeCursor);
        if (insertMatch.Success)
        {
            string q1 = insertMatch.Groups["q1"].Success ? insertMatch.Groups["q1"].Value : null;
            string q2 = insertMatch.Groups["q2"].Success ? insertMatch.Groups["q2"].Value : null;
            return new AnalysisResult
            {
                Type = CompletionType.InsertColumnTemplate,
                TargetDatabase = q2 != null ? q1 : null,
                TargetSchema = q2 ?? q1,
                TargetTable = insertMatch.Groups["tbl"].Value,
                StatementText = statementText
            };
        }

        // 3. Check if we're in a table-name-expecting context (FROM, JOIN, etc.)
        if (SqlCompletionContext.IsObjectNameExpected(textBeforeIdent))
        {
            return new AnalysisResult
            {
                Type = CompletionType.TableName,
                StatementText = statementText
            };
        }

        // 3b. JOIN [schema.]table [alias] ON <cursor> — the join condition. Offer
        // foreign-key-based predicates first, then plain columns. Checked before the
        // generic column context so the just-joined table can be identified for FK pairing.
        var joinOnMatch = JoinOnConditionPattern.Match(textBeforeIdent);
        if (joinOnMatch.Success)
        {
            return new AnalysisResult
            {
                Type = CompletionType.JoinOnCondition,
                JoinedTableReference = joinOnMatch.Groups["alias"].Success
                    ? joinOnMatch.Groups["alias"].Value
                    : joinOnMatch.Groups["tbl"].Value,
                StatementText = statementText
            };
        }

        // 4. Check if we're in a column-expecting context (SELECT list, WHERE, ON, ORDER BY, etc.)
        // Pass the full statement for FROM clause detection (FROM may be after cursor in SELECT list)
        if (IsColumnContext(textBeforeIdent, statementText))
        {
            return new AnalysisResult
            {
                Type = CompletionType.ColumnInContext,
                StatementText = statementText
            };
        }

        // No specific schema-object context — offer keywords and snippets
        return new AnalysisResult
        {
            Type = CompletionType.Keyword,
            StatementText = statementText
        };
    }

    /// <summary>
    /// Determines whether <paramref name="cursorPosition"/> sits inside a SQL comment —
    /// either a "--" line comment (to end of line) or a "/* ... */" block comment.
    /// Scans from the start of the text, skipping over string and bracketed-identifier
    /// literals so that "--" or "/*" appearing inside them isn't treated as a comment.
    /// </summary>
    private static bool IsInsideComment(string text, int cursorPosition)
    {
        int end = Math.Min(cursorPosition, text.Length);
        int i = 0;

        while (i < end)
        {
            char c = text[i];

            // String literal — skip to its close (handles '' escapes).
            if (c == '\'')
            {
                i++;
                while (i < end)
                {
                    if (text[i] == '\'')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\'') { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }

            // Bracketed identifier — skip to its close.
            if (c == '[')
            {
                i++;
                while (i < end && text[i] != ']') i++;
                if (i < end) i++;
                continue;
            }

            // Line comment — the cursor is inside it if it falls before the line ends.
            if (c == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                i += 2;
                while (i < text.Length && text[i] != '\n' && text[i] != '\r') i++;
                if (i >= end) return true;   // line ended at/after the cursor → cursor is in the comment
                continue;
            }

            // Block comment — the cursor is inside it if it falls before "*/".
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                if (i + 1 >= text.Length) return true;  // unterminated block runs to EOF → cursor is inside
                if (i + 2 > end) return true;           // "*/" closes at/after the cursor → cursor is inside
                i += 2;
                continue;
            }

            i++;
        }

        return false;
    }

    /// <summary>
    /// Removes a trailing partial identifier (possibly schema-qualified) from the text,
    /// so that context detection sees "FROM dbo.Time<cursor>" as "FROM ". This allows
    /// Ctrl+Space to re-trigger completion mid-word, and handles the case where the user
    /// has partially typed an identifier after a clause keyword.
    /// </summary>
    private static string StripTrailingPartialIdentifier(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        int end = text.Length;

        // Strip trailing unbracketed identifier chars
        while (end > 0)
        {
            char c = text[end - 1];
            if (char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#')
                end--;
            else
                break;
        }

        // Strip a trailing bracketed identifier like "[My Table"
        if (end > 0 && text[end - 1] == '[')
        {
            end--;
        }
        else if (end > 0 && text[end - 1] == ']')
        {
            int bracketStart = text.LastIndexOf('[', end - 1);
            if (bracketStart >= 0) end = bracketStart;
        }

        // Strip any further leading "qualifier." segments. A reference may be qualified
        // by up to three parts (database.schema.object), so strip them all so that, e.g.,
        // "FROM MyDb.dbo.Cust" is classified the same as "FROM " (object name expected).
        while (end > 0 && text[end - 1] == '.')
        {
            end--; // the dot
            if (end > 0 && text[end - 1] == ']')
            {
                int bracketStart = text.LastIndexOf('[', end - 1);
                if (bracketStart >= 0) { end = bracketStart; continue; }
            }
            while (end > 0)
            {
                char c = text[end - 1];
                if (char.IsLetterOrDigit(c) || c == '_')
                    end--;
                else
                    break;
            }
        }

        return text.Substring(0, end);
    }

    /// <summary>
    /// Checks if the cursor is right after "identifier." — indicating column completion.
    /// Returns the identifier before the dot, or null if not in a dot context.
    /// </summary>
    private static AnalysisResult CheckDotContext(string textBeforeCursor)
    {
        if (string.IsNullOrEmpty(textBeforeCursor))
            return null;

        // Must end with: identifier.  (with optional partial typing after the dot)
        // Match: word. or [bracketed]. at end, possibly followed by partial identifier
        var match = DotPrefixPattern.Match(textBeforeCursor);
        if (!match.Success)
            return null;

        string prefix = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;

        // Don't trigger for schema.table patterns that are in FROM/JOIN context
        // (those are handled by table completion)
        // But DO trigger for alias. patterns
        // Heuristic: if there's a FROM/JOIN keyword right before the identifier, it's table completion
        int identStart = match.Index;
        if (identStart > 0)
        {
            string beforeIdent = textBeforeCursor.Substring(
                Math.Max(0, identStart - 50), Math.Min(identStart, 50)).TrimEnd();
            if (TableContextBeforeDot.IsMatch(beforeIdent))
                return null; // This is schema.table, not alias.column
        }

        return new AnalysisResult
        {
            Type = CompletionType.ColumnAfterDot,
            DotPrefix = prefix
        };
    }

    /// <summary>
    /// Detects whether the cursor sits at an argument position of a built-in function
    /// that expects a value from a known set (a data type or a datepart). Returns a
    /// FunctionArgument result with the expected kind, or null if not applicable.
    /// </summary>
    private static AnalysisResult CheckFunctionArgumentContext(string fullText, int cursorPosition)
    {
        var call = SignatureHelpParser.ParseCallAtCursor(fullText, cursorPosition);
        // Built-in functions are never schema-qualified.
        if (call == null || call.Schema != null)
            return null;

        int start = call.ParametersStart;
        if (start < 0 || start > cursorPosition || cursorPosition > fullText.Length)
            return null;

        string argText = fullText.Substring(start, cursorPosition - start);

        SqlArgKind kind;
        if (SqlBuiltInFunctions.UsesAsDataType(call.ObjectName))
            kind = ExpectsTypeAfterAs(argText) ? SqlArgKind.DataType : SqlArgKind.None;
        else
            kind = SqlBuiltInFunctions.GetArgumentKind(call.ObjectName, call.CurrentParameterIndex);

        if (kind == SqlArgKind.None)
            return null;

        return new AnalysisResult { Type = CompletionType.FunctionArgument, ArgumentKind = kind };
    }

    // Matches a trailing "AS <partial type>" — i.e. CAST/PARSE expects a data type here.
    private static readonly Regex AfterAsPattern = new Regex(@"(?i)\bAS\s+\w*$", RegexOptions.Compiled);

    private static bool ExpectsTypeAfterAs(string argText)
    {
        if (string.IsNullOrEmpty(argText))
            return false;
        // PARSE has a trailing culture arg; only the segment after the last top-level
        // comma can hold the "AS <type>" clause.
        return AfterAsPattern.IsMatch(LastTopLevelSegment(argText));
    }

    /// <summary>
    /// Returns the substring after the last top-level (paren-depth-zero, outside string
    /// literals) comma. Used to isolate the current argument segment.
    /// </summary>
    private static string LastTopLevelSegment(string text)
    {
        int depth = 0;
        int lastComma = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\'')
            {
                i++;
                while (i < text.Length && text[i] != '\'') i++;
                continue;
            }
            if (c == '(') depth++;
            else if (c == ')') { if (depth > 0) depth--; }
            else if (c == ',' && depth == 0) lastComma = i;
        }
        return text.Substring(lastComma + 1);
    }

    // Matches a SELECT-list star with the cursor immediately after it: the star must be
    // preceded by SELECT (with optional TOP/DISTINCT) or a list comma, optionally through
    // an "alias." / "[alias]." qualifier. COUNT(*) and multiplication ("a * b") don't
    // match because '(' or an operand sits before the star instead.
    // Groups: tok = the full "*"/"alias.*" token, wp/bp = plain/bracketed alias.
    private static readonly Regex SelectStarPattern = new Regex(
        @"(?i)(?:\bSELECT\s+(?:TOP\s*(?:\(\s*\d+\s*\)|\d+)\s+)?(?:DISTINCT\s+)?|,\s*)(?<tok>(?:(?:\[(?<bp>[^\]]+)\]|(?<wp>[@#]*\w+))\s*\.\s*)?\*)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches "JOIN [db.][schema.]table [AS] [alias] ON " at the end of (partial-stripped) text.
    // Anchored at end so it binds to the nearest JOIN that immediately precedes the ON.
    // Bracketed qualifiers may contain dots ([DataBaseName].dbo.Tasks).
    private static readonly Regex JoinOnConditionPattern = new Regex(
        @"(?i)\bJOIN\s+(?:(?:\[[^\]]+\]|\w+)\s*\.\s*){0,2}(?:\[(?<tbl>[^\]]+)\]|(?<tbl>\w+))(?:\s+(?:AS\s+)?(?<alias>\w+))?\s+ON\s+$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches: identifier. at end of text (with optional partial word after dot)
    // Group 1: unbracketed identifier, Group 2: bracketed identifier
    // A leading #/##/@ sigil is captured so local temp tables ("#tmp.") and table
    // variables ("@tv.") resolve their columns the same way a plain alias does.
    private static readonly Regex DotPrefixPattern = new Regex(
        @"(?:([@#]*\w+)|(?:\[([^\]]+)\]))\.\w*$",
        RegexOptions.Compiled | RegexOptions.RightToLeft);

    // Keywords that indicate the dot is part of [database.]schema.object, not alias.column.
    // A qualifier chain (e.g. "FROM MyDb.dbo.") is allowed after the keyword so three-part
    // references are still recognized as object-name (table) completion, not column-after-dot.
    // Bracketed qualifiers may contain dots (e.g. "FROM [DataBaseName].dbo.").
    private static readonly Regex TableContextBeforeDot = new Regex(
        @"(?i)\b(?:FROM|JOIN|INTO|UPDATE|DELETE\s+FROM|TRUNCATE\s+TABLE|ALTER\s+TABLE|DROP\s+TABLE|INSERT\s+INTO|TABLE|EXEC|EXECUTE)\b\s*(?:(?:\[[^\]]+\]|\w+)\s*\.\s*)*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Determines if the cursor is in a context where column names are expected.
    /// This covers: SELECT list, WHERE clause, ON clause, ORDER BY, GROUP BY, HAVING, SET clause.
    /// </summary>
    private static bool IsColumnContext(string textBeforeCursor, string fullStatementText)
    {
        if (string.IsNullOrEmpty(textBeforeCursor))
            return false;

        // Must have a FROM clause somewhere in the statement — columns only make sense when tables are referenced.
        // Check full statement because FROM may come after cursor (e.g., in SELECT list).
        if (!HasFromClause(fullStatementText ?? textBeforeCursor))
            return false;

        return ColumnContextPattern.IsMatch(textBeforeCursor);
    }

    // Patterns that indicate a column name is expected.
    // After SELECT (at start or after comma), WHERE, AND, OR, ON, ORDER BY, GROUP BY,
    // HAVING, SET (in UPDATE), WHEN, THEN, CASE, comparison operators
    private static readonly Regex ColumnContextPattern = new Regex(
        @"(?i)(?:" +
            @"\bSELECT\s+(?:TOP\s+\d+\s+)?(?:DISTINCT\s+)?$" +    // SELECT [TOP N] [DISTINCT]
            @"|\bSELECT\b.*,\s*$" +                                // After comma in SELECT list
            @"|\bWHERE\s+$" +                                      // WHERE
            @"|\bAND\s+$" +                                        // AND
            @"|\bOR\s+$" +                                         // OR
            @"|\bON\s+$" +                                         // ON (JOIN condition)
            @"|\bORDER\s+BY\s+$" +                                 // ORDER BY
            @"|\bORDER\s+BY\b.*,\s*$" +                            // After comma in ORDER BY
            @"|\bGROUP\s+BY\s+$" +                                 // GROUP BY
            @"|\bGROUP\s+BY\b.*,\s*$" +                            // After comma in GROUP BY
            @"|\bHAVING\s+$" +                                     // HAVING
            @"|\bSET\s+$" +                                        // SET (UPDATE)
            @"|\bSET\b.*,\s*$" +                                   // After comma in SET
            @"|\bCASE\s+$" +                                       // CASE expression
            @"|\bWHEN\s+$" +                                       // WHEN
            @"|\bTHEN\s+$" +                                       // THEN
            @"|\bELSE\s+$" +                                       // ELSE
            @"|(?:=|<>|!=|<=|>=|<|>)\s*$" +                          // After comparison operator
            @"|\bLIKE\s+$" +                                       // LIKE
            @"|\bBETWEEN\s+$" +                                    // BETWEEN
            @"|\bIN\s*\(\s*$" +                                    // IN (
            @")",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches "INSERT INTO [db.][schema.]table " (trailing whitespace, no open paren yet).
    // Two qualifiers → database.schema.table; one → schema.table. Bracketed qualifiers
    // may contain dots ([Database].dbo.Projects).
    private static readonly Regex InsertTableContextPattern = new Regex(
        @"(?i)\bINSERT\s+INTO\s+(?:(?:\[(?<q1>[^\]]+)\]|(?<q1>\w+))\s*\.\s*(?:(?:\[(?<q2>[^\]]+)\]|(?<q2>\w+))\s*\.\s*)?)?(?:\[(?<tbl>[^\]]+)\]|(?<tbl>\w+))\s+$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches the USE keyword at the end of (stripped) text, expecting a database name next.
    private static readonly Regex UseContextPattern = new Regex(
        @"(?i)\bUSE\s+$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches "DBCC " at the end of (stripped) text, expecting a DBCC command name next.
    private static readonly Regex DbccContextPattern = new Regex(
        @"(?i)\bDBCC\s+$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches "COLLATE " at the end of (stripped) text, expecting a collation name next.
    private static readonly Regex CollateContextPattern = new Regex(
        @"(?i)\bCOLLATE\s+$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches "ALTER " at the end of (stripped) text, expecting an object kind next.
    private static readonly Regex AlterTargetPattern = new Regex(
        @"(?i)\bALTER\s+$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches "ALTER TABLE [schema.]name " at the end of (stripped) text, expecting a
    // table sub-action (ADD, ALTER COLUMN, DROP …) next.
    private static readonly Regex AlterTableActionPattern = new Regex(
        @"(?i)\bALTER\s+TABLE\s+(?:\[?\w+\]?\s*\.\s*)?\[?\w+\]?\s+$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches "ALTER INDEX " at the end of (stripped) text, expecting an index name or ALL.
    private static readonly Regex AlterIndexNamePattern = new Regex(
        @"(?i)\bALTER\s+INDEX\s+$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches "ALTER INDEX {name | ALL} ON " — the object (table/view) name is expected.
    private static readonly Regex AlterIndexOnPattern = new Regex(
        @"(?i)\bALTER\s+INDEX\s+(?:\[[^\]]+\]|\w+)\s+ON\s+$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches "ALTER INDEX {name | ALL} ON [schema.]object " — an index action is expected.
    private static readonly Regex AlterIndexActionPattern = new Regex(
        @"(?i)\bALTER\s+INDEX\s+(?:\[[^\]]+\]|\w+)\s+ON\s+(?:(?:\[[^\]]+\]|\w+)\s*\.\s*)?(?:\[[^\]]+\]|\w+)\s+$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches EXEC or EXECUTE keyword, optionally followed by partial schema.name typing
    private static readonly Regex ProcedureContextPattern = new Regex(
        @"(?i)\b(?:EXEC|EXECUTE)\s+(?:\[?\w+\]?\.)?\w*$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Checks if the cursor is in a context expecting a stored procedure name (after EXEC/EXECUTE).
    /// </summary>
    private static bool IsProcedureContext(string textBeforeCursor)
    {
        if (string.IsNullOrEmpty(textBeforeCursor))
            return false;

        if (textBeforeCursor.Length > 500)
            textBeforeCursor = textBeforeCursor.Substring(textBeforeCursor.Length - 500);

        return ProcedureContextPattern.IsMatch(textBeforeCursor);
    }

    /// <summary>
    /// Checks if a FROM clause exists in the text, indicating tables are in scope.
    /// </summary>
    private static bool HasFromClause(string text)
    {
        return text.IndexOf("FROM", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("JOIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("UPDATE", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("DELETE", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Batch separator: a line containing only GO (any casing). Compiled and cached as a
    // static — ExtractCurrentStatement runs on every analysis (per keystroke), and building
    // an uncompiled Regex each call was a measurable cost on large scripts.
    private static readonly Regex GoBatchPattern = new Regex(
        @"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Extracts the full current SQL statement containing the cursor position.
    /// Includes text both before and after the cursor for complete alias/FROM resolution.
    /// Statements are delimited by GO batches, semicolons, or by a top-level statement
    /// keyword (UPDATE/DELETE/INSERT/MERGE/SELECT/WITH) appearing at the start of a line.
    /// The latter handles the common case where users write multiple statements on
    /// consecutive lines without trailing semicolons.
    /// </summary>
    private static string ExtractCurrentStatement(string fullText, int cursorPosition)
    {
        int pos = Math.Min(cursorPosition, fullText.Length);

        // Find the start and end of the current batch (GO on its own line)
        int batchStart = 0;
        int batchEnd = fullText.Length;
        foreach (Match m in GoBatchPattern.Matches(fullText))
        {
            if (m.Index + m.Length <= pos)
                batchStart = m.Index + m.Length;
            else if (m.Index >= pos)
            {
                batchEnd = m.Index;
                break;
            }
        }

        string batch = fullText.Substring(batchStart, batchEnd - batchStart);
        int cursorInBatch = pos - batchStart;

        var boundaries = FindStatementBoundaries(batch);

        int stmtStart = 0;
        int stmtEnd = batch.Length;
        foreach (int b in boundaries)
        {
            if (b <= cursorInBatch) stmtStart = b;
            else { stmtEnd = b; break; }
        }

        return batch.Substring(stmtStart, stmtEnd - stmtStart);
    }

    private static readonly HashSet<string> StatementStartKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "UPDATE", "INSERT", "DELETE", "MERGE", "WITH",
        "EXEC", "EXECUTE", "USE", "DECLARE", "CREATE", "ALTER", "DROP", "TRUNCATE",
        "IF", "WHILE", "BEGIN", "COMMIT", "ROLLBACK", "PRINT", "RETURN", "SET", "GO"
    };

    /// <summary>
    /// Returns the offsets within <paramref name="batch"/> at which a new statement begins.
    /// Handles ';' terminators and top-level statement keywords at line-start (paren-depth 0,
    /// outside strings/comments). UPDATE/DELETE/INSERT/MERGE never appear in sub-queries, so
    /// they're safe to treat as unconditional boundaries; SELECT/WITH are only boundaries
    /// when paren depth is zero.
    /// </summary>
    private static List<int> FindStatementBoundaries(string batch)
    {
        var boundaries = new List<int>();
        int parenDepth = 0;
        bool atLineStart = true;
        int i = 0;

        while (i < batch.Length)
        {
            char c = batch[i];

            if (c == '\'')
            {
                i++;
                while (i < batch.Length)
                {
                    if (batch[i] == '\'')
                    {
                        if (i + 1 < batch.Length && batch[i + 1] == '\'') { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                atLineStart = false;
                continue;
            }

            if (c == '[')
            {
                i++;
                while (i < batch.Length && batch[i] != ']') i++;
                if (i < batch.Length) i++;
                atLineStart = false;
                continue;
            }

            if (c == '-' && i + 1 < batch.Length && batch[i + 1] == '-')
            {
                while (i < batch.Length && batch[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < batch.Length && batch[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < batch.Length && !(batch[i] == '*' && batch[i + 1] == '/')) i++;
                if (i + 1 < batch.Length) i += 2;
                atLineStart = false;
                continue;
            }

            if (c == '(') { parenDepth++; atLineStart = false; i++; continue; }
            if (c == ')') { if (parenDepth > 0) parenDepth--; atLineStart = false; i++; continue; }

            if (c == ';' && parenDepth == 0)
            {
                boundaries.Add(i + 1);
                atLineStart = true;
                i++;
                continue;
            }

            if (c == '\n' || c == '\r')
            {
                atLineStart = true;
                i++;
                continue;
            }

            if (c == ' ' || c == '\t')
            {
                i++;
                continue;
            }

            if (atLineStart && char.IsLetter(c))
            {
                int keyEnd = i;
                while (keyEnd < batch.Length && (char.IsLetter(batch[keyEnd]) || batch[keyEnd] == '_')) keyEnd++;
                string word = batch.Substring(i, keyEnd - i);

                bool isBoundaryKeyword =
                    StatementStartKeywords.Contains(word) &&
                    // SELECT/WITH inside parens are sub-queries, not new statements
                    (parenDepth == 0 || (!word.Equals("SELECT", StringComparison.OrdinalIgnoreCase) &&
                                          !word.Equals("WITH", StringComparison.OrdinalIgnoreCase)));

                if (isBoundaryKeyword && parenDepth == 0 && i > 0)
                    boundaries.Add(i);

                i = keyEnd;
                atLineStart = false;
                continue;
            }

            atLineStart = false;
            i++;
        }

        return boundaries;
    }

}
