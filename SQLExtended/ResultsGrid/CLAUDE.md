# Results Grid

## Results Grid Aggregates

`ResultsGrid/Aggregates/` totals whatever is selected in a results grid — sum, average, min, max, distinct,
null and blank counts — into a dockable window (Ctrl+Alt+G, the results grid's right-click menu, or the
SQLExtended menu). Modelled on SSMSBoost's Aggregates pane and on Excel's status bar.

**It shows one row per selected column, not one figure for the whole selection.** Selecting three columns
and being given a single Sum answers a question nobody asked — an order id added to a unit price. A
combined "All selected" row is still produced when more than one column is in the selection, for the
Excel-status-bar case, but it is the extra rather than the headline.

**Every value it works from is the text the grid displays.** `IGridStorage.GetCellDataAsString` is the only
value accessor SSMS exposes; there is no typed path to the underlying data. So "numeric" means *the
displayed values all parse as numbers*, not *the column's SQL type is numeric*, and three consequences
follow that are worth knowing before trusting a figure — all of them noted on the window's Settings tab: a
`varchar` column of digits will be summed, `Distinct` counts distinct *renderings* (two `datetime2(7)`
values differing below the displayed precision count once), and "Text length" counts characters on screen,
not `DATALENGTH` bytes. NULLs come from `QEResultSet.IsCellDataNull` by reflection, as in
`ResultsGridReader`; without it a real NULL and the literal string `'NULL'` are indistinguishable.

### Reading the selection

`GridSelectionReader` is the half that touches `GridControl`, and everything it does was established by
decompiling `Microsoft.SqlServer.GridControl.dll` rather than inferred — each of these fails silently:

- **Which of a block's bounds are meaningful depends on `GridControl.SelectionType`.** For a whole-column
  selection (`ColumnBlocks`/`SingleColumn`) the block's `Y`/`Bottom` describe only where the click landed;
  the real extent is every row. For a whole-row selection it is `X`/`Right` that mean nothing. Reading all
  four unconditionally totals *one cell* of a column the user selected entirely — and selecting a column to
  total it is the primary gesture this feature exists for. The control's own clipboard path
  (`GetClipboardTextForSelectionBlock`) switches on exactly this, and is the reference.
- **Column indexes are UI indexes for four of the six selection types and storage indexes for the other
  two.** `AdjustColumnIndexesInSelectedCells` — which both `SelectedCells` and
  `SelectionChangedEventArgs.SelectedBlocks` run through — returns the collection *untouched* for
  `CellBlocks`/`ColumnBlocks`/`RowBlocks`/`SingleRow` and remaps only `SingleCell`/`SingleColumn`. Getting
  this backwards reads a neighbouring column once columns have been reordered. Use the public
  `GetStorageColumnIndexByUIIndex` for the UI-indexed cases; never assume identity.
- **`Right` and `Bottom` are inclusive** (`Width == Right - X + 1`), and **storage column 0 is the
  row-number column** — data starts at 1, the same convention `ResultsGridReader` uses. A whole-row
  selection always includes column 0, and totalling row numbers is never what was meant.

**An over-cap selection computes nothing at all rather than a prefix.** Reading N cells is N calls into
`IGridStorage` on the UI thread (it is a WinForms control; there is nowhere else to read it from), so the
cap is what keeps a Ctrl+A on a million-row grid from freezing SSMS. The tempting alternative — total the
first N and note the truncation — puts a plausible, wrong number in the one place a wrong number is
invisible. So it refuses, names the real size, and points at `GridAggregatesMaxCells`.

### Arithmetic

`GridAggregateCalculator.cs` is free of the GridControl assembly so the test project can link it
(`SQLExtended.Tests/ResultsGrid/GridAggregateCalculatorTests.cs`) — the same split `ExportFileNaming` and
`MonitorCollection` exist for, and for the same reason: every mistake it can make is a plausible-looking
number rather than an error. Nothing throws, no grid goes blank.

- **Sums accumulate in `decimal` wherever they fit.** The columns people select and total are money and
  decimal columns, and accumulating those in `double` loses cents silently — a total that is *nearly*
  right. Floating point is used only when a value cannot be a decimal at all (a `float` rendered as
  `1E+300`) or the running total overflows, and the result then carries `Approximate` so the window can say
  so. A sum is formatted to the widest scale that went into it, so a money column still shows cents.
- **A column is numeric only if *every* non-null, non-blank value parses.** One stray value and no Sum is
  offered at all — better than a total that quietly skipped rows.
- **`NumberStyles.AllowThousands` is deliberately off, and current culture is tried before invariant.** The
  grid renders numerics unseparated (`1234.5600`), so allowing group separators buys nothing and actively
  breaks non-English locales: .NET's invariant parse of `"1,5"` reads the comma as a group separator and
  returns **15**. Pinned by `ACommaIsNotReadAsAThousandsSeparator`.
- **The date test requires a `-`, `:` or `/` and six characters before `DateTime.TryParse` is consulted.**
  On its own that method is permissive enough to accept `"1,5"` as a date under an English culture, which
  labels a text column Date/time and orders its Min/Max chronologically. Every shape SQL Server renders a
  date, time, `datetime2` or `datetimeoffset` in carries one of those characters, so the narrowing costs
  nothing real.
- **Min/Max display the original cell text**, never a reformatted value — re-rendering would imply a
  precision the grid did not show. They are *compared* numerically or chronologically when the column is,
  which is what stops `"9" > "10"`.
- Distinct and non-null follow SQL semantics (`COUNT(DISTINCT col)` ignores NULL, counts the empty string).

### When it runs

`GridAggregatesWatcher` attaches to grids **only while the window is visible**, driven by the control's own
`IsVisibleChanged` — which also covers the pane being tabbed behind another, indistinguishable from closed
as far as this is concerned. A user who never opens it never has a handler on an SSMS grid.

- **Grids are found by polling**, because SSMS builds a fresh `GridControl` per result set on every
  execution and raises no event for it. Handlers are dropped on `Disposed` so a session's executions do not
  accumulate subscriptions.
- **Recomputes are debounced.** `SelectionChanged` fires continuously through a drag; recomputing per
  mouse-move would re-read the whole range and make the drag itself stutter.
- **`GridAggregatesAutoShow` is the one thing that arms the watcher with the window closed**, which is why
  it is off by default and why `SettingsCommand` re-arms it after the dialog closes rather than only at
  package load. It opens the window only for an actual range — otherwise every single click in a results
  grid would pop a tool window.

## Find in Results Grid

`ResultsGrid/Find/` searches the text of a results grid (Ctrl+Alt+S, or the grid's right-click menu),
tints every match, and steps the grid's own selection from one to the next. It reads cells the same way
the aggregates pane does — `IGridStorage.GetCellDataAsString` is the only value accessor SSMS exposes — so
**it searches what is on screen**: a NULL arrives as the word `NULL`, and searching for it finds real NULLs
*and* a varchar cell containing the word. Comparison is ordinal, not culture-sensitive; over hundreds of
thousands of cells a culture-sensitive `IndexOf` is both slower and willing to call strings equal that do
not look equal.

**Matches are always collected; "Highlight all" only decides what is painted.** The obvious design — an
incremental find that stops at the first hit — cannot say "3 of 47", re-scans on every step, and re-reads
everything again to step backwards. Since the scan is sliced and capped anyway, collecting is cheaper in
aggregate and is the only way the count is honest. It also means toggling an option that cannot change
what matches repaints instead of re-reading the grid — that is what `GridFindOptions.MatchingEquals`
exists for, and why it deliberately ignores `HighlightAll`.

**Scanning is sliced across dispatcher ticks, never awaited.** Grid storage is readable only on the UI
thread, so `GridFindScan.Step` does a bounded amount of work and returns; the controller runs ~12 ms
slices at `DispatcherPriority.Background`. The window stays usable, and stepping works against what has
been found so far while the rest is still being read. Two caps bound it (`GridFindMaxCells`,
`GridFindMaxMatches`) and **hitting either is recorded and shown as "N+"** — a partial count presented as
a total is the one failure nobody would question.

Four things about `GridControl` decide whether any of this works. All were read out of the decompiled
control (`ilspycmd -t …UI.Grid.GridControl Microsoft.SqlServer.GridControl.dll`), and each fails silently:

- **`SelectedCells`' *setter* wants storage indexes for `SingleCell`/`SingleColumn` and UI indexes for the
  other four selection types.** `AdjustColumnIndexesInSelectedCells(…, bFromUIToStorage: false)` converts
  only those two and passes the rest through — exactly mirroring the getter asymmetry `GridSelectionReader`
  documents. Getting it backwards selects a neighbouring column as soon as anyone drags a column header.
  `GridFindController.SelectionColumn` is the one place that decides this.
- **`EnsureCellIsVisible` is different again: it takes a *storage* index and converts internally.** So the
  same call site passes a converted column to the selection and a raw one to the scroll.
- **`CustomizeCellGDIObjects` reports the storage column index** (the grid passes `m_Columns[n].ColumnIndex`,
  not the on-screen position), so a reordered grid needs no adjustment when painting — but column 0 is still
  the row-number column and data starts at 1.
- **The grid never disposes the brushes it is handed**, so `GridFindHighlighter`'s are static and shared.
  And **never set `CellFont`**: the grid reuses one event-args instance for every cell and reads `CellFont`
  back unconditionally, so a font set for one match leaks onto the whole grid and stays there.

Two consequences of where the focus is:

- **`AlwaysHighlightSelection` has to be forced on.** The search box holds the keyboard focus, and the grid
  paints no selection at all while unfocused — the current match would be selected and invisible. The prior
  value is restored on dispose; it is a visible change to a control we do not own.
- **Grids are found through `DTE.ActiveDocument`, not `DTE.ActiveWindow`.** `ActiveWindow` is *this tool
  window* the moment the user clicks into the search box, so reading it would look for grids inside the
  search window and find none — the feature would work only while the grid had focus, which is never,
  because typing is how a search starts. (The aggregates watcher gets away with `ActiveWindow` because it
  polls while the user is in the grid.)

Selection highlight is applied *after* the paint hook, so the current match paints itself in the selection
colour with no special casing. Grids are polled for the same reason the aggregates window polls them, and
highlights come off on hide: a tinted results grid with no window on screen to explain it is not something
the user can undo.

`GridFindOptions`, `GridCellSource`, `GridFindMatcher` and `GridFindScan` are free of the grid assembly so
the test project links them (`SQLExtended.Tests/ResultsGrid/GridFindTests.cs`) — the same split
`ExportFileNaming` and `MonitorCollection` exist for. The walk is what is worth pinning: a wrong order puts
the "next" match behind you, and a wrapped scan that stops one cell early or late either hides exactly one
match or reports the starting cell twice. `\A…\z` anchors "whole cell" regexes rather than `^…$`, which in
.NET also match at line boundaries — a multi-line cell would otherwise satisfy "whole cell" on the strength
of one of its lines.
