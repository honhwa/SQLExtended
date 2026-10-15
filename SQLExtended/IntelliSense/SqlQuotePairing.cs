namespace SQLExtended.IntelliSense;

/// <summary>
/// Decides whether a typed single quote should bring its closing quote with it. Pure text logic, so the
/// tests can pin it without an editor; <see cref="QuotePairCommandFilter"/> does the typing.
/// </summary>
internal static class SqlQuotePairing
{
    internal enum Region { Code, String, LineComment, BlockComment, Bracket, DoubleQuoted }

    /// <summary>
    /// What the end of <paramref name="text"/> sits inside, lexing from its start. Scanned from the top of the
    /// document rather than the line, because a string or block comment opened on an earlier line is exactly
    /// where pairing must not happen - pairing the quote that closes a multi-line string opens a new one.
    /// <c>''</c> inside a string needs no special case: the toggle closes and reopens it.
    /// </summary>
    internal static Region RegionAt(string text)
    {
        var region = Region.Code;
        int depth = 0; // T-SQL block comments nest
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char next = i + 1 < text.Length ? text[i + 1] : '\0';
            switch (region)
            {
                case Region.Code:
                    if (c == '\'') region = Region.String;
                    else if (c == '[') region = Region.Bracket;
                    else if (c == '"') region = Region.DoubleQuoted;
                    else if (c == '-' && next == '-') { region = Region.LineComment; i++; }
                    else if (c == '/' && next == '*') { region = Region.BlockComment; depth = 1; i++; }
                    break;
                case Region.String:
                    if (c == '\'') region = Region.Code;
                    break;
                case Region.Bracket:
                    if (c == ']') { if (next == ']') i++; else region = Region.Code; }
                    break;
                case Region.DoubleQuoted:
                    if (c == '"') { if (next == '"') i++; else region = Region.Code; }
                    break;
                case Region.LineComment:
                    if (c == '\n' || c == '\r') region = Region.Code;
                    break;
                case Region.BlockComment:
                    if (c == '/' && next == '*') { depth++; i++; }
                    else if (c == '*' && next == '/') { i++; if (--depth == 0) region = Region.Code; }
                    break;
            }
        }
        return region;
    }

    /// <summary>
    /// True when a quote typed after <paramref name="before"/> (everything ahead of the caret) and in front of
    /// <paramref name="next"/> ('\0' at the end of the document) opens a literal that should be closed for the user.
    /// </summary>
    internal static bool ShouldPair(string before, char next)
    {
        // A word straight after the caret is text being wrapped, and a quote there is an existing literal.
        if (IsWordChar(next) || next == '\'')
            return false;

        // Glued to a word it is an apostrophe (O'Brien) or a typo - except the N of an N'…' literal.
        if (before.Length > 0)
        {
            char prev = before[before.Length - 1];
            if (IsWordChar(prev))
            {
                bool unicodePrefix = (prev == 'N' || prev == 'n') && (before.Length == 1 || !IsWordChar(before[before.Length - 2]));
                if (!unicodePrefix)
                    return false;
            }
        }

        return RegionAt(before) == Region.Code;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';
}
