# Environment Tabs

`EnvTabs/` colours and renames query tabs by the server and database they are connected to, so production
is distinguishable from development at a glance (SQLExtended menu → Environment Tabs…). Inspired by
[SSMS-EnvTabs](https://github.com/Blake-goofy/SSMS-EnvTabs); no code is shared with it, and the colouring
mechanism is deliberately different — see below.

**There is no API for tab colour, and the one that looks like it isn't one.** Colouring is a VS 2026 shell
feature that SSMS 22 inherits (`Tools > Options > Environment > Tabs and Windows > Colorize document tabs →
by regex`). The shell's `RegexFileProvider` reads a file of regexes, matches each open document's **full
path** against them in order, and treats the first matching line as that document's colour *group*. So the
only way to say "this tab is production" is to write a regex that matches that tab's path.

**Colours are pinned through `FileColorService.SetFileColorAsync`, not by salting the regex.** This is the
one place this implementation departs from the published technique, and the reason is worth keeping. The
shell derives a group's colour as `Math.Abs(HashHelpers.GetStableHashCode(pattern) % 16)` — the hash of the
pattern *text*. EnvTabs forces a chosen colour by appending a regex comment (`(?#salt:9)`), which changes
the hash without changing what the pattern matches, and brute-forcing the salt until the hash lands on the
wanted index. That works, but it requires reimplementing `GetStableHashCode` byte-for-byte from an internal
helper in an assembly we cannot reference — and if the copy ever disagrees with the shell's, **every colour
silently becomes a different colour**, which reads as working software. `FileColorService.SetFileColorAsync`
(the implementation behind the tab's own "Set Tab Color" command) is `public` and records an explicit
`groupId → colorIndex` entry that outranks the hash. We let the shell compute its own group id from our
pattern and just tell it which colour that group is. Nothing to reproduce, nothing to re-verify when
Microsoft changes it.

Everything is reached by reflection (`FileColorServiceProxy`, `ColorByRegexConfigStore`): the service, the
`ViewManager.Instance.Preferences` flags, and `IVsSolutionWorkingFolders` all live in assemblies that ship
inside SSMS and appear in no SDK package. Types are found among **already-loaded** assemblies, for the
identity reason documented under `JobDialogLauncher`.

Things that are easy to get wrong here, all of which fail silently:

- **The shell calls `Trim()` on each config line and throws the result away.** That is literal — the parser
  really does `text.Trim();` as a statement and then uses the untrimmed line. So leading whitespace becomes
  part of a pattern, and an *indented* `//` is not recognised as a comment and is compiled as a regex.
  Every line is emitted flush-left and the block is never indented for readability; a test asserts it.
- **Patterns match full paths, not file names.** Two query windows always differ by path (that is how the
  RDT keys them), whereas an unsaved window is `SQLQuery1.sql` in every folder SSMS has ever used — so a
  name-only pattern hands one server's colour to another server's tab. The cost is that the pattern is what
  the tab's tooltip displays; correctness wins.
- **The managed block is written first and foreign lines are preserved.** The shell seeds this file itself
  (`^.*\.cs$` and friends) and users may have added their own. First match wins, so a stray `^.*\.sql$`
  below ours is harmless while one above would swallow every query window.
- **Colours are re-pinned for a few ticks after any config write.** The shell reloads the file through
  `IVsFileChangeEx` on its own schedule, so a colour pinned in the same tick the pattern was written finds
  no group yet and `SetFileColorAsync` silently returns. Retrying for a bounded number of ticks makes this
  self-healing without needing to know when the reload lands.
- **The palette is copied from the binary, not from the EnvTabs wiki** (they differ by a digit or two).
  It exists only so our own picker shows the swatch the user will actually get; nothing computes from it.

**It polls rather than hooking the RDT, and connections are sticky per path.** Reacting to document-open
events gets you the tab but not the thing that matters — its connection. `ConnectionHelper` can only report
the connection of the *active* query window, and a tab's connection changes with no document event at all
when the user picks another database from the toolbar dropdown or reconnects the window. So the connection
is sampled from whichever tab is active each tick and remembered against that document's path. Once known
it is kept until the tab closes: a background tab cannot be re-read, and dropping it to "unknown" would
strip the colour off every tab that isn't focused — precisely when the colour is doing its job.

**Off by default** (`SQLExtendedSettings.EnvTabsEnabled`). Enabling it is not a private change: it turns on the
shell's own `ColorizeDocumentTabs` preference, repoints it at the regex provider, and writes to a shell
config file. Turning it off removes the managed block and restores the captions rather than freezing the
last state. The preference is set once at startup, not every poll — someone who turns tab colouring off in
Tools > Options has said something, and a poll that re-enabled it would be fighting them.

The auto-prompt offers three answers, not two: "Not now" is per session, "Never for this" persists
(`EnvTabsDeclined`). A single Cancel cannot tell "I'm busy" from "never colour this server", and getting
that wrong means either a dialog every session forever or silently never offering again. Only the *active*
tab is ever offered, once per connection per session — a prompt per unmapped background tab would open a
stack of dialogs at startup. Both dialogs set the shell as owner in `OnSourceInitialized` for the reason
`SchemaDialog` does.

`EnvTabsDiagnostics` is a small in-memory ring surfaced in the rules dialog. Everything in this subsystem
fails soft, which means it can quietly do nothing and look identical to "no rules match yet" — and the
ActivityLog is not written unless SSMS was launched with `/log`, so it is not there when the failure
happens.

`EnvTabPalette`, `EnvTabRule`, `EnvTabRuleSet`, `TabCaptionFormatter` and `ColorByRegexConfigText` are free
of VS, WPF and SqlClient so the test project links them
(`SQLExtended.Tests/EnvTabs/`). Those are the parts whose failures are silent: a rule that over-matches
paints production in the development colour, `Strip` must be the exact inverse of `Format` or a re-formatted
tab accumulates prefixes ("1. Prod — 1. Prod — 1. QA"), and a config line the shell rejects colours nothing.
