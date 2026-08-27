using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Parses stored procedure and function calls to extract the object name,
/// schema, and current parameter index. Used by SignatureHelpSource and testable
/// independently of VS SDK types.
/// </summary>
internal static class SignatureHelpParser
{
    /// <summary>
    /// Parses the text around the cursor to find a proc/function call and determine
    /// which parameter the cursor is currently on.
    /// </summary>
    // A proc/function call's enclosing EXEC keyword or opening paren is realistically a short
    // distance before the cursor (you're typing its arguments). Bounding the backward scan to
    // this window keeps the per-keystroke cost constant instead of O(document) — scanning the
    // whole prefix of a large script on every keystroke was a UI-thread stall.
    private const int LookbackWindow = 5000;

    internal static CallInfo ParseCallAtCursor(string text, int cursorPosition)
    {
        if (string.IsNullOrEmpty(text) || cursorPosition <= 0 || cursorPosition > text.Length)
            return null;

        int windowStart = Math.Max(0, cursorPosition - LookbackWindow);
        string textBefore = text.Substring(windowStart, cursorPosition - windowStart);

        // Strategy 1: EXEC proc_name @param1 = val1, @param2 = val2
        var execMatch = ExecCallPattern.Match(textBefore);
        if (execMatch.Success)
        {
            string schema = execMatch.Groups["schema"].Success && execMatch.Groups["schema"].Length > 0
                ? execMatch.Groups["schema"].Value : null;
            string name = execMatch.Groups["name"].Value;
            string paramText = execMatch.Groups["params"].Value;

            int parametersStart = windowStart + execMatch.Groups["params"].Index;
            int currentParam = CountParameters(paramText);

            return new CallInfo
            {
                Schema = schema,
                ObjectName = name,
                ParametersStart = parametersStart,
                CurrentParameterIndex = currentParam
            };
        }

        // Strategy 2: Function call with parentheses: schema.func_name(arg1, arg2)
        int parenDepth = 0;
        int openParenPos = -1;

        for (int i = cursorPosition - 1; i >= windowStart; i--)
        {
            char c = text[i];
            if (c == '\'')
            {
                i--;
                while (i >= windowStart)
                {
                    if (text[i] == '\'')
                    {
                        if (i > 0 && text[i - 1] == '\'')
                            i--;
                        else
                            break;
                    }
                    i--;
                }
                continue;
            }

            if (c == ')')
            {
                parenDepth++;
            }
            else if (c == '(')
            {
                if (parenDepth == 0)
                {
                    openParenPos = i;
                    break;
                }
                parenDepth--;
            }
        }

        if (openParenPos < 0)
            return null;

        // Extract the function name immediately before the open paren. Only the short tail
        // right before '(' can hold the name, so scan a small slice rather than the whole prefix.
        int nameStart = Math.Max(0, openParenPos - 256);
        string beforeParen = text.Substring(nameStart, openParenPos - nameStart);
        var funcMatch = FunctionNameBeforeParen.Match(beforeParen);
        if (!funcMatch.Success)
            return null;

        // Ensure the match reaches the end (right before the paren)
        if (funcMatch.Index + funcMatch.Length != beforeParen.Length)
            return null;

        string funcSchema = funcMatch.Groups["schema"].Success && funcMatch.Groups["schema"].Length > 0
            ? funcMatch.Groups["schema"].Value : null;
        string funcName = funcMatch.Groups["name"].Value;

        if (IsBuiltInKeyword(funcName))
            return null;

        string insideParens = text.Substring(openParenPos + 1, cursorPosition - openParenPos - 1);
        int funcCurrentParam = CountParameters(insideParens);

        return new CallInfo
        {
            Schema = funcSchema,
            ObjectName = funcName,
            ParametersStart = openParenPos + 1,
            CurrentParameterIndex = funcCurrentParam
        };
    }

    // Matches: EXEC[UTE] [schema.]proc_name [params...]
    private static readonly Regex ExecCallPattern = new Regex(
        @"(?i)\b(?:EXEC|EXECUTE)\s+" +
        @"(?:(?:\[?(?<schema>\w+)\]?)\.)?" +
        @"\[?(?<name>\w+)\]?" +
        @"\s+(?<params>.*)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches: [schema.]function_name at end of string
    private static readonly Regex FunctionNameBeforeParen = new Regex(
        @"(?:(?:\[?(?<schema>\w+)\]?)\.)?\[?(?<name>\w+)\]?\s*$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> BuiltInKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "IF", "WHILE", "IN", "EXISTS", "NOT", "CASE", "WHEN", "THEN", "ELSE",
        "END", "BEGIN", "RETURN", "PRINT", "RAISERROR", "THROW", "VALUES",
        "SELECT", "WHERE", "AND", "OR", "ON", "SET", "DECLARE"
    };

    private static bool IsBuiltInKeyword(string name) => BuiltInKeywords.Contains(name);

    /// <summary>
    /// Counts the number of comma-separated parameters in the given text,
    /// respecting parentheses nesting and string literals.
    /// Returns the 0-based index of the current parameter.
    /// </summary>
    internal static int CountParameters(string paramText)
    {
        if (string.IsNullOrWhiteSpace(paramText))
            return 0;

        int count = 0;
        int depth = 0;

        for (int i = 0; i < paramText.Length; i++)
        {
            char c = paramText[i];

            if (c == '\'')
            {
                i++;
                while (i < paramText.Length)
                {
                    if (paramText[i] == '\'')
                    {
                        if (i + 1 < paramText.Length && paramText[i + 1] == '\'')
                            i++;
                        else
                            break;
                    }
                    i++;
                }
                continue;
            }

            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0) count++;
        }

        return count;
    }

    internal sealed class CallInfo
    {
        public string Schema { get; set; }
        public string ObjectName { get; set; }
        public int ParametersStart { get; set; }
        public int CurrentParameterIndex { get; set; }
    }
}
