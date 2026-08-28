# Rainbow parentheses

Colours `(` / `)` in the query editor by nesting depth, and — behind a setting — `BEGIN`/`END`,
`CASE`/`END` and the TRY/CATCH forms. The idea is [RainbowBraces](https://github.com/madskristensen/RainbowBraces)'
(Apache-2.0); **none of its code is here**, and it could not have been used as-is: its manifest targets the
VS product IDs rather than SSMS, it exports for the code content types rather than `SQL`, and its
`AllowanceResolver` family decides whether a bracket is real by reading classifications from a VS language
service that has no SSMS equivalent. The tagger shape is standard VS SDK; the language half is ScriptDom.

Four files: `RainbowPairScanner` (pure, linked into the test project), `RainbowPair`,
`RainbowClassifications` (the eight colours), `RainbowTagger` + its provider.

## Why it reads tokens and not characters

`RainbowPairScanner` walks the ScriptDom **token stream**. A parenthesis inside a string literal, a `--` or
`/* */` comment, a `[bracketed]` identifier or a `"quoted"` identifier never surfaces as a
`LeftParenthesis`/`RightParenthesis` token at all, so none of those cases needs handling — which is the
entire reason for the dependency. `SqlIdentifierQuotingTests`' lesson applies: the five masking cases are
asserted **individually**, because one combined script passes while four of the five rules are broken.

The same is what makes the block pass possible: a word counts as `BEGIN` only when the lexer says it is
the keyword, so `SELECT 'BEGIN'` and `SELECT [BEGIN]` cost nothing to get right.

- `GetTokenStream` **reports lexical errors through its out-parameter rather than throwing**, and still
  returns what it read. An unterminated string or comment is the normal state of a script being typed, so
  the errors are deliberately ignored rather than treated as "don't colour this".
- `initialQuotedIdentifiers: true` matches `SqlFormatterService` and `LocalTableScanner`. It does not change
  the answer here — a paren inside `"x"` is not a paren token either way — it is kept consistent so all
  three agree about what a script means.
- **The opener is emitted immediately as unmatched and rewritten in place when its partner arrives.** So
  anything left on a stack at EOF is already in the list, already flagged, with no cleanup pass — and the
  results come out in position order for free.
- The `Scan(string)` fast path tests for **both** parenthesis characters. Testing only `(` was the first bug:
  a script holding just `)` has no pairs but does have an unmatched token to colour, and it went silently
  uncoloured. The shortcut is skipped entirely when blocks are on, which has no single character to look for.

## Blocks: not every BEGIN opens one

Verified by lexing against ScriptDom, not from the documentation: **`TRY`, `CATCH` and `ATOMIC` are
non-reserved and come back as `Identifier`**, so they are recognised by text; `TRAN`, `TRANSACTION` and
`DISTRIBUTED` have their own token types.

- **`BEGIN TRAN` / `TRANSACTION` / `DISTRIBUTED TRANSACTION` must not be pushed.** They are closed by COMMIT
  or ROLLBACK, never by END. Pushing one swallows the next real `END` and shifts the colour of every block
  below it for the rest of the script — a failure that is invisible in a two-line test and disfiguring in a
  real procedure. `BEGIN DIALOG` and `END CONVERSATION` (Service Broker) are skipped for the same reason.
- **`BEGIN ATOMIC` is the exception** — natively compiled procedures, and it really does end with `END`.
- **Only the `BEGIN`/`END`/`CASE` keyword is tagged, never the `TRY` or `CATCH` word beside it.** A comment
  is legal between the two words, and one span covering both would colour that comment.
- A mismatched `END TRY` **unwinds past** an unclosed inner `BEGIN` rather than mis-pairing with it; what it
  skipped stays unmatched, since its own END would have had to come first.
- **Parentheses and blocks count depth on separate stacks.** A `CASE` inside two parens is at block depth 0.
  One shared counter makes both look wrong — the parens jump colour the moment a block opens.

## The tagger

- **`[Order(After = Priority.High)]` on every format definition is load-bearing.** SSMS classifies editor
  punctuation itself, and without it the built-in colour can win: the extension loads, composes and scans
  correctly while nothing on screen changes, with nothing anywhere saying why.
- **`GetTags` translates spans** from the scan's snapshot to the requested one. The scan is always at least
  one edit behind the keystroke that triggered it, so without the translation tags land a character out for
  the whole debounce window — which reads as a bug in the depth logic rather than in the timing.
- **Debounced (300 ms), lexed off the UI thread, and bounded at 1 MB.** A version check drops a slow scan a
  newer one has already overtaken, with an exception for the very first result so something colours
  immediately. The size bail is sticky and logs once — a generated script pasted into a query window is a
  real case.
- **Settings are copied into fields on the UI thread**; `GetTags` never reads `SQLExtendedSettings.Current`.
  That is the rule `SQLExtendedLog` and `PerfRecentDumpDays` already follow.
- **Every failure here is silent on screen**, so the provider logs a construction failure and the size bail
  through `SQLExtendedLog` — enough to tell "never composed" from "composed, found nothing" from "found
  pairs, wrong colour won".
- The colours are `[UserVisible(true)]`, so they are edited in **Tools → Options → Environment → Fonts and
  Colors**. That is why the settings tab has no palette.

## `SQLExtendedSettings.Changed`

Added for this feature, and used by anything else holding live state built from settings. `Save()` had only
been assigning `_current`, so an open query window kept its old colours until it was reopened — the settings
UI appeared not to work. It fires **only when the write succeeded**, and **from its own try/catch outside the
save's**: a throwing subscriber inside that catch is indistinguishable from the file write failing. It is
static, so a subscriber that does not unsubscribe outlives its own window.

Palette size and unmatched-tinting only need a re-tag from the cached scan. **Toggling blocks needs a fresh
scan**, because it changes what the scan collects, not how it is coloured.

## Not verified in SSMS

The scanner is covered by `SQLExtended.Tests/Rainbow/RainbowPairScannerTests.cs` and runs without SSMS.
**Everything from the classifications outward has only been compiled**: whether the ordering actually beats
SSMS's own punctuation colouring, whether the query-window buffer is exactly `ContentType("SQL")`
(`ContentTypeSniffer.cs`, DEBUG-only, answers this), and how the colours read against the dark and light
editor themes are all open. Worth settling before trusting anything above.
