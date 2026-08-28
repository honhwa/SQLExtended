using Microsoft.SqlServer.TransactSql.ScriptDom;
using System;
using System.Collections.Generic;
using System.IO;

namespace SQLExtended.Rainbow;

/// <summary>
/// Finds the parentheses in a script — and optionally its BEGIN/END blocks — and assigns each one its
/// nesting depth, so the editor can colour them by level.
///
/// <para>The work is done over the ScriptDom <em>token stream</em>, not the text: a parenthesis inside a
/// string literal, a comment, a [bracketed] identifier or a "quoted" identifier never surfaces as a
/// <see cref="TSqlTokenType.LeftParenthesis"/>/<see cref="TSqlTokenType.RightParenthesis"/> token at all,
/// so those cases need no special handling here. That is the whole reason this reads tokens rather than
/// characters, and it is what makes the block pass possible at all — a word only counts as BEGIN when the
/// lexer says it is the keyword.</para>
///
/// <para>Free of the VS editor assemblies so the test project links it, the same split
/// <c>SqlIdentifierQuoting</c> exists for.</para>
/// </summary>
public static class RainbowPairScanner
{
    /// <summary>How many distinct colours the caller may cycle through. Bounds <see cref="ColorIndex"/>.</summary>
    public const int MaxSupportedLevels = 7;

    private static readonly RainbowPair[] None = [];

    /// <summary>
    /// Tokenizes <paramref name="sql"/> and returns every paired token in it, ordered by position.
    /// Returns an empty list — never throws — when the script cannot be tokenized at all.
    /// </summary>
    /// <param name="sql">The script to scan.</param>
    /// <param name="includeBlocks">Also pair BEGIN/END, CASE/END, BEGIN TRY/END TRY and BEGIN CATCH/END CATCH.</param>
    public static IReadOnlyList<RainbowPair> Scan(string sql, bool includeBlocks = false)
    {
        if (string.IsNullOrEmpty(sql))
            return None;

        // Both parenthesis characters have to be tested: a script holding only ')' has no pairs, but it
        // does have an unmatched token to report, and skipping it would leave a stray closer uncoloured.
        // The shortcut is dropped entirely for the block pass, which has no single character to look for.
        if (!includeBlocks && sql.IndexOf('(') < 0 && sql.IndexOf(')') < 0)
            return None;

        try
        {
            // initialQuotedIdentifiers: true matches SqlFormatterService and LocalTableScanner —
            // under it "x" lexes as a QuotedIdentifier rather than a string literal. Either way a
            // paren inside it is not a paren token, so the setting does not change the answer here;
            // it is kept consistent so all three agree about what the script means.
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            using var reader = new StringReader(sql);

            // GetTokenStream reports lexical errors through the out-parameter rather than throwing,
            // and still returns the tokens it managed to read. An unterminated string or comment —
            // the normal state of a script being typed — is therefore scannable, so the errors are
            // deliberately ignored.
            var tokens = parser.GetTokenStream(reader, out _);
            return Scan(tokens, includeBlocks);
        }
        catch
        {
            return None;
        }
    }

    /// <summary>
    /// Assigns depths over an already-lexed token stream. Callers that have one (the formatter path)
    /// should use this rather than re-lexing.
    /// </summary>
    public static IReadOnlyList<RainbowPair> Scan(IList<TSqlParserToken> tokens, bool includeBlocks = false)
    {
        if (tokens == null || tokens.Count == 0)
            return None;

        var results = new List<RainbowPair>();

        // Each stack holds the index into `results` of a still-open token. The opener is emitted
        // immediately (as unmatched) and rewritten in place when its partner arrives, so anything left
        // on a stack at EOF is already in the list, already flagged unmatched, with nothing to clean up.
        //
        // Parentheses and blocks count depth separately: a CASE inside two parentheses is at block depth
        // 0, not 2. Sharing one counter makes both look wrong — the parens jump colour when a block opens.
        var parens = new Stack<int>();
        var blocks = new Stack<BlockFrame>();

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token == null)
                continue;

            switch (token.TokenType)
            {
                case TSqlTokenType.LeftParenthesis:
                    parens.Push(results.Count);
                    results.Add(new RainbowPair(token.Offset, TokenLength(token), parens.Count - 1, isMatched: false, isOpen: true));
                    break;

                case TSqlTokenType.RightParenthesis:
                    CloseParen(results, parens, token);
                    break;

                case TSqlTokenType.Begin when includeBlocks:
                    OpenBegin(results, blocks, tokens, i, token);
                    break;

                case TSqlTokenType.Case when includeBlocks:
                    PushBlock(results, blocks, token, BlockKind.Block);
                    break;

                case TSqlTokenType.End when includeBlocks:
                    CloseEnd(results, blocks, tokens, i, token);
                    break;
            }
        }

        return results;
    }

    /// <summary>
    /// Maps a nesting depth onto a palette slot, cycling once the depth runs past the palette.
    /// <paramref name="levels"/> is clamped, so a settings value that has drifted out of range
    /// still produces a colour rather than an exception.
    /// </summary>
    public static int ColorIndex(int depth, int levels)
    {
        if (levels < 1)
            levels = 1;
        else if (levels > MaxSupportedLevels)
            levels = MaxSupportedLevels;

        if (depth < 0)
            depth = 0;

        return depth % levels;
    }

    // --- parentheses ---

    private static void CloseParen(List<RainbowPair> results, Stack<int> open, TSqlParserToken token)
    {
        if (open.Count == 0)
        {
            // A ')' with nothing to close. Depth 0 is a placeholder — the caller colours unmatched
            // tokens by their own rule and never reads Depth for them.
            results.Add(new RainbowPair(token.Offset, TokenLength(token), 0, isMatched: false, isOpen: false));
            return;
        }

        int openerIndex = open.Pop();
        var opener = results[openerIndex];
        results[openerIndex] = new RainbowPair(opener.Start, opener.Length, opener.Depth, isMatched: true, isOpen: true);
        results.Add(new RainbowPair(token.Offset, TokenLength(token), opener.Depth, isMatched: true, isOpen: false, RainbowKind.Parenthesis));
    }

    // --- blocks ---

    /// <summary>What an END has to find on the stack to close something.</summary>
    private enum BlockKind
    {
        /// <summary>BEGIN, BEGIN ATOMIC and CASE — all closed by a bare END.</summary>
        Block,
        Try,
        Catch
    }

    private readonly struct BlockFrame(int resultIndex, BlockKind kind)
    {
        public int ResultIndex { get; } = resultIndex;

        public BlockKind Kind { get; } = kind;
    }

    /// <summary>
    /// Decides what a BEGIN starts, from the word after it.
    ///
    /// <para><b>Not every BEGIN opens a block.</b> BEGIN TRAN/TRANSACTION/DISTRIBUTED TRANSACTION is ended
    /// by COMMIT or ROLLBACK, and BEGIN DIALOG/CONVERSATION TIMER is a statement of its own — pushing
    /// either would swallow the next real END and shift the colour of every block below it for the rest
    /// of the script. BEGIN ATOMIC (natively compiled procedures) <em>is</em> a block and does end with END.</para>
    ///
    /// <para>TRY, CATCH, ATOMIC, DIALOG and CONVERSATION are all non-reserved words, so the lexer hands
    /// them back as <see cref="TSqlTokenType.Identifier"/> — verified against ScriptDom — and they have to
    /// be recognised by text. TRAN and TRANSACTION do have their own token types.</para>
    /// </summary>
    private static void OpenBegin(List<RainbowPair> results, Stack<BlockFrame> blocks, IList<TSqlParserToken> tokens, int index, TSqlParserToken token)
    {
        var next = NextSignificant(tokens, index);

        switch (next?.TokenType)
        {
            case TSqlTokenType.Tran:
            case TSqlTokenType.Transaction:
            case TSqlTokenType.Distributed:
                return; // BEGIN TRAN — closed by COMMIT/ROLLBACK, never by END.

            case TSqlTokenType.Identifier when IsWord(next, "TRY"):
                PushBlock(results, blocks, token, BlockKind.Try);
                return;

            case TSqlTokenType.Identifier when IsWord(next, "CATCH"):
                PushBlock(results, blocks, token, BlockKind.Catch);
                return;

            case TSqlTokenType.Identifier when IsWord(next, "DIALOG") || IsWord(next, "CONVERSATION"):
                return; // Service Broker statements, not blocks.

            default:
                PushBlock(results, blocks, token, BlockKind.Block);
                return;
        }
    }

    private static void PushBlock(List<RainbowPair> results, Stack<BlockFrame> blocks, TSqlParserToken token, BlockKind kind)
    {
        blocks.Push(new BlockFrame(results.Count, kind));
        results.Add(new RainbowPair(token.Offset, TokenLength(token), blocks.Count - 1, isMatched: false, isOpen: true, RainbowKind.Block));
    }

    /// <summary>
    /// Closes the innermost block an END can legally close.
    ///
    /// <para>Only the BEGIN/END/CASE keyword itself is coloured, never the TRY or CATCH beside it: the two
    /// words can be separated by a comment, and a span covering both would colour that comment too.</para>
    /// </summary>
    private static void CloseEnd(List<RainbowPair> results, Stack<BlockFrame> blocks, IList<TSqlParserToken> tokens, int index, TSqlParserToken token)
    {
        var next = NextSignificant(tokens, index);

        // END CONVERSATION is a Service Broker statement, the counterpart of the BEGIN DIALOG skipped above.
        if (next != null && next.TokenType == TSqlTokenType.Identifier && IsWord(next, "CONVERSATION"))
            return;

        BlockKind wanted =
            next != null && next.TokenType == TSqlTokenType.Identifier && IsWord(next, "TRY") ? BlockKind.Try :
            next != null && next.TokenType == TSqlTokenType.Identifier && IsWord(next, "CATCH") ? BlockKind.Catch :
            BlockKind.Block;

        // Unwind to the nearest frame this END can close. Anything skipped over can never be closed —
        // its own END would have had to come first — so it stays as it was emitted: unmatched.
        while (blocks.Count > 0 && blocks.Peek().Kind != wanted)
            blocks.Pop();

        if (blocks.Count == 0)
        {
            results.Add(new RainbowPair(token.Offset, TokenLength(token), 0, isMatched: false, isOpen: false, RainbowKind.Block));
            return;
        }

        var frame = blocks.Pop();
        var opener = results[frame.ResultIndex];
        results[frame.ResultIndex] = new RainbowPair(opener.Start, opener.Length, opener.Depth, isMatched: true, isOpen: true, RainbowKind.Block);
        results.Add(new RainbowPair(token.Offset, TokenLength(token), opener.Depth, isMatched: true, isOpen: false, RainbowKind.Block));
    }

    /// <summary>The next token that carries meaning — whitespace and comments are allowed between BEGIN and TRY.</summary>
    private static TSqlParserToken NextSignificant(IList<TSqlParserToken> tokens, int index)
    {
        for (int i = index + 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token == null)
                continue;

            if (token.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
                continue;

            return token;
        }

        return null;
    }

    private static bool IsWord(TSqlParserToken token, string word) => string.Equals(token.Text, word, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Token length, preferring the token's own text. ScriptDom always fills <c>Text</c> for
    /// punctuation and keywords, but a defaulted length of 1 is right for both parentheses regardless.
    /// </summary>
    private static int TokenLength(TSqlParserToken token) => string.IsNullOrEmpty(token.Text) ? 1 : token.Text.Length;
}
