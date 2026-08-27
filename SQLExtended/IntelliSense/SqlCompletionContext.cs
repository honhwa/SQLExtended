using System.Text.RegularExpressions;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Determines the completion context by scanning text backward from the cursor position.
/// Identifies whether we're in a position where table/view names are expected.
/// </summary>
internal static class SqlCompletionContext
{
    /// <summary>
    /// Keywords after which an object name (table/view) is expected.
    /// Matches multi-word keywords like "LEFT JOIN", "INSERT INTO", etc.
    /// </summary>
    private static readonly Regex ObjectContextPattern = new Regex(
        @"(?i)\b(?:FROM|(?:INNER|LEFT|RIGHT|CROSS|FULL(?:\s+OUTER)?)\s+JOIN|JOIN|INTO|UPDATE|DELETE\s+FROM|TRUNCATE\s+TABLE|ALTER\s+TABLE|DROP\s+TABLE|INSERT\s+INTO|TABLE)\s+$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Checks whether the text leading up to the cursor expects a table/view name.
    /// Looks at the last ~500 characters before the cursor to handle multi-line queries.
    /// </summary>
    public static bool IsObjectNameExpected(string textBeforeCursor)
    {
        if (string.IsNullOrEmpty(textBeforeCursor))
            return false;

        // Only check the last 500 chars for performance
        if (textBeforeCursor.Length > 500)
            textBeforeCursor = textBeforeCursor.Substring(textBeforeCursor.Length - 500);

        return ObjectContextPattern.IsMatch(textBeforeCursor);
    }

    /// <summary>
    /// Extracts the schema prefix if the user typed "dbo." — returns "dbo" (without the dot).
    /// Returns null if no schema prefix is present.
    /// </summary>
    public static string GetSchemaPrefix(string textBeforeCursor)
    {
        if (string.IsNullOrEmpty(textBeforeCursor))
            return null;

        // Match a schema name followed by a dot at the end: "dbo." or "[dbo]."
        var match = Regex.Match(textBeforeCursor, @"(?:\[?(\w+)\]?)\.\s*$");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Returns the qualifier segments that appear before the final dot at the cursor,
    /// i.e. the already-typed prefix of a (possibly multi-part) object reference. The
    /// trailing partial being typed after the last dot is NOT included.
    ///   "FROM "                  → []                  (no reference yet)
    ///   "FROM Cust"              → []                  ("Cust" is the partial, no dot)
    ///   "FROM dbo."              → ["dbo"]
    ///   "FROM dbo.Cust"          → ["dbo"]
    ///   "FROM MyDb.dbo."         → ["MyDb", "dbo"]
    ///   "FROM MyDb.dbo.Cust"     → ["MyDb", "dbo"]
    /// Brackets are stripped from each segment.
    /// </summary>
    public static System.Collections.Generic.List<string> GetQualifierParts(string textBeforeCursor)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (string.IsNullOrEmpty(textBeforeCursor))
            return parts;

        // Scan back over the qualified identifier token at the cursor. Bracketed segments
        // ([My Db]) may contain spaces, so consume everything between matching brackets.
        int end = textBeforeCursor.Length;
        int i = end - 1;
        bool inBracket = false;
        while (i >= 0)
        {
            char c = textBeforeCursor[i];
            if (inBracket)
            {
                if (c == '[') inBracket = false;
                i--;
                continue;
            }
            if (c == ']') { inBracket = true; i--; continue; }
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '#' || c == '@') { i--; continue; }
            break;
        }

        string token = textBeforeCursor.Substring(i + 1, end - (i + 1));
        if (token.Length == 0)
            return parts;

        // Split on top-level dots (dots inside brackets are part of the name); the final
        // element is the partial being typed after the last dot and is dropped.
        var segments = SplitQualifiedSegments(token);
        for (int s = 0; s < segments.Count - 1; s++)
            parts.Add(segments[s]);

        return parts;
    }

    /// <summary>Splits a qualified identifier on top-level dots, stripping brackets from each segment.</summary>
    private static System.Collections.Generic.List<string> SplitQualifiedSegments(string token)
    {
        var segments = new System.Collections.Generic.List<string>();
        var sb = new System.Text.StringBuilder();
        bool inBracket = false;

        foreach (char c in token)
        {
            if (c == '[') { inBracket = true; continue; }
            if (c == ']') { inBracket = false; continue; }
            if (c == '.' && !inBracket) { segments.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(c);
        }
        segments.Add(sb.ToString().Trim());
        return segments;
    }

    /// <summary>
    /// Returns true if the character could be part of a SQL identifier (for filtering).
    /// </summary>
    public static bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@';
    }

    /// <summary>
    /// Checks if two strings match using camelCase/PascalCase hump matching.
    /// Typing "OD" matches "OrderDetails", "od" matches "order_details".
    /// </summary>
    public static bool IsCamelCaseMatch(string typedText, string candidateName)
    {
        if (string.IsNullOrEmpty(typedText) || string.IsNullOrEmpty(candidateName))
            return false;

        // First try simple substring match (case-insensitive)
        if (candidateName.IndexOf(typedText, System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // CamelCase hump matching: each typed char matches the start of a "hump"
        int candidateIdx = 0;
        for (int i = 0; i < typedText.Length; i++)
        {
            char target = char.ToUpperInvariant(typedText[i]);
            bool found = false;

            while (candidateIdx < candidateName.Length)
            {
                char c = candidateName[candidateIdx];
                candidateIdx++;

                // Match at uppercase boundary or after underscore
                if (char.ToUpperInvariant(c) == target &&
                    (candidateIdx == 1 || char.IsUpper(c) || candidateName[candidateIdx - 2] == '_'))
                {
                    found = true;
                    break;
                }
            }

            if (!found) return false;
        }

        return true;
    }
}
