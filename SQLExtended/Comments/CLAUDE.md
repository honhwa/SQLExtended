# Comment tags and banner headers

Two things behind one setting. **Comment tags**: a comment opening with `!`, `?`, `todo` or `*` colours
apart from an ordinary one, in both the `--` and `/* */` forms — the idea is
[CommentsVS](https://github.com/madskristensen/CommentsVS)' (Apache-2.0), **none of its code is here**, and
its fifth tag `//` is deliberately absent (the strike-through was not wanted, and `//` does not start a
comment in T-SQL). **Banner headers**: the house-style box at the top of a procedure, pulled apart into
twelve roles so the asterisks can recede and the text can dominate.

Six files, laid out like `Rainbow/`: `CommentMarkScanner` and `CommentThemes` (both pure, both linked into
the test project), `CommentMark`, `CommentClassifications` (sixteen roles), `CommentThemeApplier`,
`CommentTagger` + its provider.

## Why it reads tokens and not characters

Same reason as `RainbowPairScanner`, and it carries more weight here: **only the lexer knows where a comment
actually is.** A `--` inside a string literal, inside a `[bracketed]` or `"quoted"` identifier, or inside an
enclosing `/* */` never surfaces as a `SingleLineComment`/`MultilineComment` token, so none of those cases
needs handling. That is the whole hard half of a comment colouriser, and the parser has already done it.
The four masking cases are asserted **individually** — one combined script passes while three of the four
rules are broken, the lesson `SqlIdentifierQuotingTests` paid for.

`initialQuotedIdentifiers: true` matches `SqlFormatterService`, `LocalTableScanner` and
`RainbowPairScanner`. It does not change the answer here; it is kept consistent so all of them agree.

## An unterminated `/*` produces no token at all

Verified against the lexer, not assumed. `GetTokenStream` does **not** hand back a half-read
`MultilineComment` — it drops the comment, drops the rest of the stream with it, and reports error 46032
through the out-parameter. So:

- **A block comment stays uncoloured until its `*/` is typed.** Nothing the scanner can do about it while
  it reads tokens, and not worth a hand-rolled fallback for a state that ends when the closer arrives.
- **The comments ahead of it keep their colours**, so the gap is local to the comment being typed. That is
  the half worth protecting, and it has its own test.
- The single-line form has no such problem: `--` is closed by end-of-file as well as by a newline.

## Banner headers

**Checked before the tag pass, and it takes the whole comment.** It has to be: the box opens with a rule of
stars, so the `*` tag matched it and the entire fifteen-line header came back as one `Highlight` mark 961
characters long. Flat green over a screenful of text, from a feature whose whole job is picking comments out.

A banner is **a block comment whose first line is nothing but stars, over more than one line**. Both halves
matter — the star rule separates it from an ordinary `/* */` (without which every `Description :` in any
block comment lights up), and the line count stops a one-line `/**** Section ****/` being torn into
headings. `MinRuleLength` is 4, counted **after** the `/*`, so a fixture written `/****` is not a banner —
that cost a round of failing tests.

### Two passes, because the column header needs lookahead

`TryReadLine` carves every line into prefix and content and names its shape (`Rule`, `Spacer`, `Dashes`,
`Content`); `AddLineMarks` then assigns roles. They are separate for one reason: **what makes
`Date  Author  Ticket` a column header is the rule of dashes on the line below it**, which a single forward
pass has not read yet.

- **Rules are tested twice, once either side of stripping the `**` prefix.** A rule of dashes behind a
  prefix of stars is two characters, and the single-character test rejects it, so `** -------` is only
  caught by the second pass.
- **The comment's own `/*` and `*/` stay inside the rule span.** They are what make those two lines read as
  the top and bottom of the box; leaving them bright puts a lit fragment on each end.
- **The `**` prefix is its own role.** It looks like a rule but sits on a text line, and splitting it is the
  only way the outline can drop to near-background without taking the text beside it along.

### Telling the shapes apart

- **A bare heading is told from a table row by column spacing** — single spaces between words is a heading,
  a run of two or more is alignment. That is the one discriminator that holds without knowing the house
  style. *Being between two rules does not work*: `Change History` sits between two rules, and so does the
  column header row.
- **A change row is one whose first column is date-like.** `IsDateLike` is deliberately loose — `11-Jun-24`,
  `2024-06-11` and `11/06/24` all pass — because it only has to tell a date from a word.
- **Columns split on runs of two or more spaces, or any tab. Never on a character offset.** Real files mix
  the two, and a column found by counting characters lands somewhere different under every tab-width
  setting the reader might have.
- **A three-column row has skipped one, and which one is decided by content, not position**: a ticket is a
  single token, a description is prose, so a third column *containing a space* is the description. That is
  the `03-Mar-26  DB  Performance tuning` row. Counting from the left would colour its description as a
  ticket on every row laid out that way.
- The description always runs to the end of the line, so extra columns beyond the fourth fold into it
  rather than going uncoloured.

## One star is a tag, two is decoration

`-- * note` tags; `-- ** section **` and `/**** Section ****/` do not. A run of stars is what makes a
comment look *decorated*, at any length — the banner is only the large case of it, and without this rule
the small case goes green for the same reason.

Separate from the divider guard (`-- ******`, body is nothing but the tag character), which catches a rule
with no text in it. Neither rule subsumes the other: the banner has text, the divider has none.

## Colour schemes

Five, from a design handoff, each with a dark and a light variant over all sixteen roles. `CommentThemes`
holds them as plain `uint` **so the file stays free of the VS and WPF assemblies and the test project can
link it** — the palettes are data, and data like this fails silently: a palette one entry short, or written
in a different order from `CommentMarkKind`, paints every role after the gap with its neighbour's colour and
looks like a scheme somebody simply designed badly. `CommentThemesTests` asserts completeness, ordering,
that the two variants differ, and that the unknown-scheme fallback does not throw.

- **The enum order is load-bearing.** `CommentClassifications.AllNames` and every palette are indexed by
  `CommentMarkKind`. Append, never insert.
- **The four tag colours are shared by every scheme.** A scheme is about the banner; an alert is an alert
  whichever one is chosen, and re-hueing the tags would make `-- ! careful` change colour for a reason that
  has nothing to do with it.
- **Weight is not part of a scheme** — label, section and todo are bold in all of them. Otherwise a user who
  liked one scheme's emphasis could not keep it while changing its colours.
- **No scheme paints a background, including Tinted banner, which was designed around one.** Two reasons,
  both about a fill several lines tall: it fights the selection and the current-line highlight down the
  whole block, and it exposes a ragged right edge on lines of differing length, hideable only by padding
  every line to a fixed width — i.e. by editing the user's script. Its foregrounds ship as specified.

## Why a scheme has to be *written*, not declared

**The colours on the format definitions are only defaults.** As soon as SSMS has a stored value for a
classification — which it does the moment the user opens Fonts and Colors, and after any theme switch —
that stored value wins. So a scheme cannot be switched by changing what the format definitions declare.
`CommentThemeApplier` writes it into the classification format map, which is the same store the Fonts and
Colors dialog edits, and it persists the same way.

- **It writes only when the wanted scheme and variant differ from `CommentSchemeApplied`.** So: once on
  first run, on a deliberate scheme change, and on a dark/light switch. **That is what leaves hand-tuning
  alone** — recolour an entry by hand and nothing rewrites it, because nothing has changed. Clearing that
  setting forces a re-apply, which is the way back from a hand-tuning that went wrong.
- **The save it makes is guarded against re-entry**, because it is reached *through*
  `SQLExtendedSettings.Changed` — recording the applied scheme would otherwise raise the event that called it.
- **Dark or light is measured from the tool-window background, not matched against the stock theme names**,
  so a third-party or hand-edited theme still gets the right variant.
- It touches only this feature's own sixteen classifications, in one batch update.

## The tagger

It is `RainbowTagger` with one setting instead of four, and every note in `Rainbow/CLAUDE.md` about the
tagger applies unchanged — `[Order(After = Priority.High)]`, span translation, the 300 ms debounce, the
off-UI-thread lex, the 1 MB sticky bail, settings copied into fields on the UI thread, and logging the
construction failure and the size bail because **every failure here is silent on screen**.

**The ordering is more load-bearing than it is for the parentheses.** SSMS definitely classifies comments
already, so if the ordering fails the built-in comment colour wins outright and nothing changes on screen.

On `SQLExtendedSettings.Changed` there is only the on/off for the tagger, so coming back on always needs a
fresh scan — unlike the rainbow palette settings, there is never a cached scan worth recolouring.

## Deploying this to SSMS

Confirmed in SSMS: the classifications appear in Fonts and Colors and the tagger colours the editor. Getting
there cost an afternoon to a cause that was not in the code — **SSMS keys its MEF scan off
`extension.vsixmanifest`, not the DLL.** Copying a rebuilt `SQLExtended.dll` into the extension folder on
its own leaves the cached catalog untouched, so new MEF exports never compose: the feature is loaded, on
disk, and invisible, with the *older* half of the same assembly still working perfectly. Diagnosed by
grepping the cached catalog itself — `Microsoft.VisualStudio.Default.catalogs` under
`%LOCALAPPDATA%\Microsoft\SSMS\22.0_<hash>\ComponentModelCache` — which held `RainbowTagger` and not
`CommentTagger`. Delete that folder with SSMS closed, or touch the manifest. This masks **any** new MEF
export, not just this feature's.

## Not verified in SSMS

The scanner and the palettes are covered by `SQLExtended.Tests/Comments/` and run without SSMS. What has
only been compiled: **the theme applier** — whether the format-map write lands and persists the way Fonts
and Colors' own edits do, and whether `VSColorTheme.ThemeChanged` fires early enough to re-apply cleanly —
and **how each of the five schemes actually reads** against the dark and light editor themes, which is the
whole point of shipping five and the reason the light variants exist. Also unobserved: two view taggers now
attach to the same buffer, and how a rainbow classification composes with one of these where they meet. It
should not arise — a paren inside a comment is not a paren token — but it has not been seen.
