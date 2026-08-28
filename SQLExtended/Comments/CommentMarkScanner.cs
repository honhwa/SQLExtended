using Microsoft.SqlServer.TransactSql.ScriptDom;
using System;
using System.Collections.Generic;
using System.IO;

namespace SQLExtended.Comments;

/// <summary>
/// Finds the tagged comments in a script — <c>-- !</c>, <c>-- ?</c>, <c>-- todo</c> and <c>-- *</c>, in both
/// the <c>--</c> and <c>/* */</c> forms — and pulls a banner header apart into its parts, so the editor can
/// colour each of them separately.
///
/// <para>Like <c>RainbowPairScanner</c> this reads the ScriptDom <em>token stream</em> rather than the text,
/// and for the same reason: only the lexer knows where a comment actually is. A <c>--</c> inside a string
/// literal, inside a <c>[bracketed]</c> identifier, or inside an enclosing <c>/* */</c> never surfaces as a
/// <see cref="TSqlTokenType.SingleLineComment"/>/<see cref="TSqlTokenType.MultilineComment"/> token, so none
/// of those cases needs handling here. That is the whole hard half of a comment colouriser, and the parser
/// has already done it.</para>
///
/// <para>Free of the VS editor assemblies so the test project links it, the same split
/// <c>RainbowPairScanner</c> and <c>SqlIdentifierQuoting</c> exist for.</para>
/// </summary>
public static class CommentMarkScanner
{
    /// <summary>The word form. Only <c>todo</c>, which is CommentsVS' own default.</summary>
    private const string TaskWord = "todo";

    /// <summary>How many rule characters in a row make a line chrome rather than text. Four, so <c>/****</c> opens a banner.</summary>
    private const int MinRuleLength = 4;

    /// <summary>Longer than this and a bare line is prose, not a heading. <c>Change History</c> is two.</summary>
    private const int MaxLabelWords = 4;

    private static readonly CommentMark[] None = [];

    /// <summary>
    /// Tokenizes <paramref name="sql"/> and returns every coloured run in it, ordered by position.
    /// Returns an empty list — never throws — when the script cannot be tokenized at all.
    /// </summary>
    public static IReadOnlyList<CommentMark> Scan(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return None;

        // A comment needs one of these two characters to start, so a script with neither cannot hold one.
        if (sql.IndexOf('-') < 0 && sql.IndexOf('/') < 0)
            return None;

        try
        {
            // initialQuotedIdentifiers: true matches SqlFormatterService, LocalTableScanner and
            // RainbowPairScanner. It does not change the answer here — a "--" inside a quoted identifier
            // is not a comment either way — it is kept consistent so all of them agree about the script.
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(sql);

            // GetTokenStream reports lexical errors through the out-parameter rather than throwing, and
            // still returns what it read, so a script mid-edit is scannable and the errors are ignored.
            //
            // One consequence is worth knowing: an unterminated /* is NOT handed back as a half-read
            // MultilineComment. The lexer drops it — and the rest of the stream with it — and reports
            // error 46032 instead. A block comment therefore stays uncoloured until its */ is typed,
            // while the comments ahead of it keep their colours. Verified against the lexer, not assumed.
            var tokens = parser.GetTokenStream(reader, out _);
            return Scan(tokens);
        }
        catch
        {
            return None;
        }
    }

    /// <summary>Classifies an already-lexed token stream. Callers that have one should use this rather than re-lexing.</summary>
    public static IReadOnlyList<CommentMark> Scan(IList<TSqlParserToken> tokens)
    {
        if (tokens == null || tokens.Count == 0)
            return None;

        List<CommentMark> results = null;

        foreach (var token in tokens)
        {
            if (token?.Text == null || token.TokenType is not (TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment))
                continue;

            // A banner is checked first and takes the whole comment: it opens with a rule of stars, so
            // the tag pass below would otherwise read that rule as a `*` highlight and paint the entire
            // header — all fifteen lines of it — one flat colour. That was the first thing this got wrong.
            if (token.TokenType == TSqlTokenType.MultilineComment && IsBanner(token.Text))
                AddBannerMarks(token.Text, token.Offset, results ??= []);
            else if (TryClassify(token.Text, out var kind, out int length))
                (results ??= []).Add(new CommentMark(token.Offset, length, kind));
        }

        return results ?? (IReadOnlyList<CommentMark>)None;
    }

    /// <summary>
    /// Reads the tag out of one comment's text.
    /// </summary>
    /// <param name="text">The comment token's text, opener included.</param>
    /// <param name="kind">The tag found.</param>
    /// <param name="length">How much of <paramref name="text"/> to colour: everything up to the last non-whitespace character.</param>
    /// <returns>False for an ordinary comment, which is left in the editor's own comment colour.</returns>
    public static bool TryClassify(string text, out CommentMarkKind kind, out int length)
    {
        kind = default;
        length = 0;

        if (string.IsNullOrEmpty(text) || text.Length < 3)
            return false;

        bool block;
        if (text[0] == '-' && text[1] == '-')
            block = false;
        else if (text[0] == '/' && text[1] == '*')
            block = true;
        else
            return false;

        // A single-line comment's token runs to the end of the line, newline included; a block comment's
        // may have space before its closer. Neither belongs in the coloured span or in the body below.
        int span = text.Length;
        while (span > 2 && char.IsWhiteSpace(text[span - 1]))
            span--;

        int bodyEnd = span;
        if (block && bodyEnd - 2 >= 2 && text[bodyEnd - 1] == '/' && text[bodyEnd - 2] == '*')
        {
            bodyEnd -= 2;
            while (bodyEnd > 2 && char.IsWhiteSpace(text[bodyEnd - 1]))
                bodyEnd--;
        }

        int i = 2;
        while (i < bodyEnd && (text[i] == ' ' || text[i] == '\t'))
            i++;

        if (i >= bodyEnd)
            return false;

        char c = text[i];
        bool symbolic = true;

        switch (c)
        {
            case '!': kind = CommentMarkKind.Alert; break;
            case '?': kind = CommentMarkKind.Query; break;

            // One star is the tag; two or more is decoration. `/*** Section ***/` is a divider someone
            // drew, not a highlight they asked for, and it is the same complaint as the banner in
            // miniature — the star run is what makes a comment look decorated, at any length.
            case '*' when i + 1 >= bodyEnd || text[i + 1] != '*': kind = CommentMarkKind.Highlight; break;
            case '*': return false;
            default:
                symbolic = false;
                if (!IsTaskWord(text, i, bodyEnd))
                    return false;

                kind = CommentMarkKind.Task;
                break;
        }

        // A divider — `-- ******`, `/****************/`, `-- !!!!!!!!` — is punctuation, not a tag. Without
        // this every banner line in a script lights up, which is most of what a comment colouriser gets
        // wrong in practice. `-- !!! this one matters` still tags: it has something to say after the run.
        if (symbolic && IsAllOneCharacter(text, i, bodyEnd, c))
            return false;

        length = span;
        return true;
    }

    // --- banner headers ---
    //
    // The house-style header block that opens a stored procedure:
    //
    //     /**********************************************************
    //     ** Description : builds the JSON for the Elastic index
    //     **********************************************************
    //     ** Change History
    //     **********************************************************
    //     ** Date         Author    Ticket   Description
    //     ** -----------  --------  -------  ---------------------
    //     ** 11-Jun-24    AT        NA       Excluded INTERLEAVED2OF5
    //     **********************************************************/
    //
    // Around 60% of the characters in one of these are asterisks carrying no information, so every part
    // gets its own role and a palette decides which of them recede. The `**` prefix is split from the rule
    // it looks like precisely so the outline can drop to near-background without taking the text with it.

    /// <summary>The shapes a banner line can have. Decided in the first pass, used by the second.</summary>
    private enum BannerShape
    {
        /// <summary>A full-width rule of stars, delimiters included.</summary>
        Rule,

        /// <summary>A bare <c>**</c> with nothing after it — the blank line of the box.</summary>
        Spacer,

        /// <summary>A <c>**</c> prefix followed by a rule of dashes.</summary>
        Dashes,

        /// <summary>A <c>**</c> prefix followed by text.</summary>
        Content
    }

    /// <summary>One line of a banner, already carved into its prefix and its content.</summary>
    private struct BannerLine
    {
        public int Start, End;
        public int PrefixStart, PrefixEnd;
        public int ContentStart, ContentEnd;
        public BannerShape Shape;
    }

    /// <summary>
    /// A block comment whose <em>first line is nothing but stars</em>, over more than one line. Both halves
    /// matter: the star rule is what distinguishes a banner from an ordinary <c>/* */</c>, and the line
    /// count is what stops a one-line <c>/**** note ****/</c> being torn into headings.
    /// </summary>
    private static bool IsBanner(string text)
    {
        int newline = text.IndexOf('\n');
        if (newline < 0)
            return false;

        int end = newline;
        while (end > 2 && char.IsWhiteSpace(text[end - 1]))
            end--;

        int stars = 0;
        for (int i = 2; i < end; i++)
        {
            if (text[i] != '*')
                return false;

            stars++;
        }

        return stars >= MinRuleLength;
    }

    /// <summary>
    /// Two passes. The first carves every line into prefix and content and names its shape; the second
    /// assigns roles. <b>They are separate because the column header can only be recognised by looking
    /// ahead</b> — what makes <c>Date  Author  Ticket</c> a header rather than another change row is the
    /// rule of dashes on the line below it, which a single forward pass has not read yet.
    /// </summary>
    private static void AddBannerMarks(string text, int offset, List<CommentMark> results)
    {
        var lines = new List<BannerLine>();

        int pos = 0;
        while (pos < text.Length)
        {
            int newline = text.IndexOf('\n', pos);
            int lineEnd = newline < 0 ? text.Length : newline;

            if (TryReadLine(text, pos, lineEnd, out var line))
                lines.Add(line);

            pos = newline < 0 ? text.Length : newline + 1;
        }

        for (int i = 0; i < lines.Count; i++)
            AddLineMarks(text, offset, lines, i, results);
    }

    /// <summary>Carves one raw line into its parts. False for a blank line, which produces no marks at all.</summary>
    private static bool TryReadLine(string text, int lineStart, int lineEnd, out BannerLine line)
    {
        line = default;

        int start = lineStart, end = lineEnd;
        while (start < end && char.IsWhiteSpace(text[start]))
            start++;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
            end--;

        if (start == end)
            return false;

        // The comment's own delimiters are chrome like the rest of the line, so they are stripped for the
        // tests below but stay inside the span: the /* of the opening rule and the */ of the closing one
        // are the parts that make those lines read as the top and bottom of the box.
        int content = start, contentEnd = end;
        if (contentEnd - content >= 2 && text[content] == '/' && text[content + 1] == '*')
            content += 2;
        if (contentEnd - content >= 2 && text[contentEnd - 1] == '/' && text[contentEnd - 2] == '*')
            contentEnd -= 2;

        line.Start = start;
        line.End = end;

        // Tested before the ** prefix is split off, which catches the all-star rules; and again after,
        // which catches `** ---------  --------`. One test either side, because a rule of dashes behind a
        // prefix of stars is two characters, and a single-character test rejects it.
        if (IsRule(text, content, contentEnd))
        {
            line.Shape = BannerShape.Rule;
            return true;
        }

        line.PrefixStart = content;
        while (content < contentEnd && text[content] == '*')
            content++;

        line.PrefixEnd = content;

        while (content < contentEnd && (text[content] == ' ' || text[content] == '\t'))
            content++;

        line.ContentStart = content;
        line.ContentEnd = contentEnd;

        line.Shape = content >= contentEnd ? BannerShape.Spacer
            : IsRule(text, content, contentEnd) ? BannerShape.Dashes
            : BannerShape.Content;

        return true;
    }

    private static void AddLineMarks(string text, int offset, List<BannerLine> lines, int index, List<CommentMark> results)
    {
        var line = lines[index];

        if (line.Shape == BannerShape.Rule)
        {
            results.Add(new CommentMark(offset + line.Start, line.End - line.Start, CommentMarkKind.BannerRule));
            return;
        }

        if (line.PrefixEnd > line.PrefixStart)
            results.Add(new CommentMark(offset + line.PrefixStart, line.PrefixEnd - line.PrefixStart, CommentMarkKind.BannerPrefix));

        switch (line.Shape)
        {
            case BannerShape.Spacer:
                return;

            case BannerShape.Dashes:
                results.Add(new CommentMark(offset + line.ContentStart, line.ContentEnd - line.ContentStart, CommentMarkKind.BannerDashes));
                return;
        }

        int start = line.ContentStart, end = line.ContentEnd;

        // `Description : the rest of the line`
        int colon = LabelColon(text, start, end);
        if (colon > start)
        {
            int labelEnd = colon;
            while (labelEnd > start && text[labelEnd - 1] == ' ')
                labelEnd--;

            results.Add(new CommentMark(offset + start, labelEnd - start, CommentMarkKind.BannerLabel));
            results.Add(new CommentMark(offset + colon, 1, CommentMarkKind.BannerPunctuation));

            int prose = colon + 1;
            while (prose < end && (text[prose] == ' ' || text[prose] == '\t'))
                prose++;

            if (prose < end)
                results.Add(new CommentMark(offset + prose, end - prose, CommentMarkKind.BannerProse));

            return;
        }

        var columns = SplitColumns(text, start, end);

        if (columns.Count >= 2 && IsDateLike(text, columns[0].Start, columns[0].End))
        {
            AddChangeRow(text, offset, columns, end, results);
            return;
        }

        // The header of the change table, recognised by the rule of dashes beneath it rather than by
        // anything about the row itself. Its own words say nothing a house style has to agree about.
        if (columns.Count >= 2 && index + 1 < lines.Count && lines[index + 1].Shape == BannerShape.Dashes)
        {
            results.Add(new CommentMark(offset + start, end - start, CommentMarkKind.BannerColumnHeader));
            return;
        }

        results.Add(new CommentMark(offset + start, end - start, IsSection(text, start, end) ? CommentMarkKind.BannerSection : CommentMarkKind.BannerProse));
    }

    /// <summary>
    /// Assigns the four column roles.
    ///
    /// <para>A row with only three columns has skipped one, and which one is decided by content rather than
    /// position: a ticket is a single token, a description is prose. So a third column <em>containing a
    /// space</em> is the description — the <c>03-Mar-26  DB  Performance tuning</c> row, which has no
    /// ticket. Counting from the left instead would colour its description as a ticket on every such row.</para>
    ///
    /// <para>The description always runs to the end of the line, so extra columns beyond the fourth fold
    /// into it rather than going uncoloured.</para>
    /// </summary>
    private static void AddChangeRow(string text, int offset, List<Column> columns, int lineEnd, List<CommentMark> results)
    {
        Add(columns[0], CommentMarkKind.BannerDate);
        Add(columns[1], CommentMarkKind.BannerAuthor);

        if (columns.Count == 3)
        {
            var third = columns[2];
            bool prose = text.IndexOf(' ', third.Start, third.End - third.Start) >= 0;

            results.Add(new CommentMark(offset + third.Start, lineEnd - third.Start, prose ? CommentMarkKind.BannerDescription : CommentMarkKind.BannerTicket));
        }
        else if (columns.Count >= 4)
        {
            Add(columns[2], CommentMarkKind.BannerTicket);
            results.Add(new CommentMark(offset + columns[3].Start, lineEnd - columns[3].Start, CommentMarkKind.BannerDescription));
        }

        void Add(Column column, CommentMarkKind kind) => results.Add(new CommentMark(offset + column.Start, column.End - column.Start, kind));
    }

    private readonly struct Column(int start, int end)
    {
        public int Start { get; } = start;

        public int End { get; } = end;
    }

    /// <summary>
    /// Splits a row on <b>runs of two or more spaces, or any tab</b> — never on a character offset.
    /// Real files mix tabs and spaces in these tables, and a column found by counting characters lands
    /// somewhere different under every tab-width setting the reader might have.
    /// </summary>
    private static List<Column> SplitColumns(string text, int start, int end)
    {
        var columns = new List<Column>();
        int i = start;

        while (i < end)
        {
            while (i < end && (text[i] == ' ' || text[i] == '\t'))
                i++;

            if (i >= end)
                break;

            int columnStart = i;
            int columnEnd = i;

            while (i < end)
            {
                if (text[i] == '\t')
                    break;

                if (text[i] == ' ' && i + 1 < end && text[i + 1] == ' ')
                    break;

                if (!char.IsWhiteSpace(text[i]))
                    columnEnd = i + 1;

                i++;
            }

            columns.Add(new Column(columnStart, columnEnd));
        }

        return columns;
    }

    /// <summary>
    /// True for the first column of a change row. Deliberately loose about the format — <c>11-Jun-24</c>,
    /// <c>2024-06-11</c> and <c>11/06/24</c> all pass — because it only has to tell a date from a word, and
    /// the row it is deciding about has already been split into columns.
    /// </summary>
    private static bool IsDateLike(string text, int start, int end)
    {
        int digits = 0;
        bool separator = false;

        for (int i = start; i < end; i++)
        {
            char c = text[i];

            if (char.IsDigit(c))
                digits++;
            else if (c is '-' or '/' or '.')
                separator = true;
            else if (!char.IsLetter(c))
                return false;
        }

        return separator && digits >= 2;
    }

    /// <summary>
    /// True when start..end is a run of one repeated rule character and whitespace — <c>*****</c>,
    /// <c>-----</c>, <c>=====</c>, and the multi-column <c>---  ---  ---</c> form. Mixing two of them is not
    /// a rule, which is what keeps <c>** --- ***</c> from qualifying.
    /// </summary>
    private static bool IsRule(string text, int start, int end)
    {
        char rule = '\0';
        int count = 0;

        for (int i = start; i < end; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
                continue;

            if (c is not ('*' or '-' or '=' or '_'))
                return false;

            if (rule == '\0')
                rule = c;
            else if (c != rule)
                return false;

            count++;
        }

        return count >= MinRuleLength;
    }

    /// <summary>
    /// Index of the colon that ends a field label, or -1 when the line does not open with one. Words then a
    /// colon, and no run of two spaces before it — <c>Date  Author  Ticket : x</c> is a table row that
    /// happens to contain a colon, not a label.
    /// </summary>
    private static int LabelColon(string text, int start, int end)
    {
        int words = 1;
        bool letters = false;

        for (int i = start; i < end; i++)
        {
            char c = text[i];

            if (c == ':')
                return letters ? i : -1;

            if (c == ' ')
            {
                if (i + 1 < end && text[i + 1] == ' ')
                    return -1;

                if (++words > MaxLabelWords)
                    return -1;

                continue;
            }

            if (!char.IsLetter(c))
                return -1;

            letters = true;
        }

        return -1;
    }

    /// <summary>
    /// True for a standalone heading such as <c>Change History</c>: a few plain words, single-spaced.
    ///
    /// <para><b>Column spacing is what separates it from a table row</b> — a heading has single spaces
    /// between its words, a row is aligned with runs of two or more. Being between two rules does not work
    /// as a test: <c>Change History</c> sits between two rules, and so does the column header.</para>
    /// </summary>
    private static bool IsSection(string text, int start, int end)
    {
        if (!char.IsLetter(text[start]))
            return false;

        int words = 1;

        for (int i = start; i < end; i++)
        {
            char c = text[i];

            if (c == ' ')
            {
                if (i + 1 < end && text[i + 1] == ' ')
                    return false;

                if (++words > MaxLabelWords)
                    return false;

                continue;
            }

            if (!char.IsLetter(c))
                return false;
        }

        return true;
    }

    /// <summary>True when the letters starting at <paramref name="start"/> spell exactly <c>todo</c>, so <c>todos</c> does not tag.</summary>
    private static bool IsTaskWord(string text, int start, int end)
    {
        int i = start;
        while (i < end && char.IsLetter(text[i]))
            i++;

        return i - start == TaskWord.Length && string.Compare(text, start, TaskWord, 0, TaskWord.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static bool IsAllOneCharacter(string text, int start, int end, char c)
    {
        for (int i = start; i < end; i++)
        {
            if (text[i] != c && !char.IsWhiteSpace(text[i]))
                return false;
        }

        return true;
    }
}
