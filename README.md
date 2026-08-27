# SQLExtended for SSMS

**SSMS, extended.** A free, open-source suite of tools for SQL Server Management Studio 22 —
schema browsing and search, T-SQL formatting, IntelliSense, four live monitoring dashboards,
schema export built for folder diffing, and results-grid tooling, in one extension.

Every multi-tool suite for SSMS is commercial. This is the open-source one.

> **Status:** works, used daily, but young. Expect rough edges, and see *Known limitations* below.

## Requirements

- **SQL Server Management Studio 22** (built on the Visual Studio 2026 shell)
- Windows; .NET Framework 4.8

## What's in it

| Area | |
|---|---|
| **Schema** | View Schema (`Ctrl+Shift+D`) — CREATE script, indexes, foreign keys for the object under the cursor. Persistent schema cache, background refresh, Schema Cache window. |
| **Search** | SQL Search — object names, columns, and module definitions via a SQLite full-text index; optionally SQL Agent job step commands, which live outside the cache. |
| **Formatting** | Format SQL (`Ctrl+K, Ctrl+F`) — ScriptDom-based T-SQL formatter with a large option set: casing, comma position, list reflow, CASE/CTE/derived-table layout, alias style. |
| **IntelliSense** | Completion over the schema cache and the system catalog (`sys.`, `INFORMATION_SCHEMA`), signature help, snippets, automatic bracketing of names that need it. |
| **Monitoring** | Four dockable dashboards, each pinned to the server it was opened from: Performance (`Ctrl+Alt+P`), Agent Jobs (`Ctrl+Alt+J`), Always On (`Ctrl+Alt+A`), Replication (`Ctrl+Alt+R`). Live only — nothing is persisted. |
| **Results grid** | Grid Aggregates (`Ctrl+Alt+G`) — sum/avg/min/max/distinct/null per selected column. Find in Results (`Ctrl+Alt+S`) — search and highlight grid text. Script Results as INSERT. |
| **Export** | Script a database to one file, or to a folder tree of one file per object — deliberately free of timestamps and other volatile output, so two servers can be compared in WinMerge. |
| **Statistics** | Parse Statistics (`Ctrl+K, Ctrl+G`) — reads `STATISTICS IO`/`TIME` output from the Messages pane. |
| **Environment Tabs** | Colours and renames query tabs by the server and database they're connected to, so production is distinguishable at a glance. |
| **Validation** | Validate Schema References — finds references to objects and columns that no longer exist. |

Most of it is off or opt-in by default. Settings live under **SQLExtended Settings…**, plus a
Diagnostics tab with an in-session log, since almost every failure in an SSMS extension has to be
swallowed rather than thrown.

## Install

Grab the latest `SQLExtended-<version>.vsix` from
[Releases](https://github.com/JamTheRadar/SQLExtended/releases/latest).

Close SSMS, then double-click the `.vsix`. It installs per-user — no admin rights needed. Reopen SSMS and
the **SQLExtended** menu appears in the menu bar; if it doesn't, run `clearSSMScache.ps1` to clear SSMS's
MEF cache and relaunch. Uninstall from **SSMS → Extensions → Manage Extensions**.

Once installed, the extension checks for newer releases on startup (at most once per 20h, and switchable
off under SQLExtended Settings → Updates). It cannot update itself silently — no VS or SSMS extension can
replace its own loaded assembly — so it points you at the new `.vsix` and asks you to close SSMS first.

## Building

```bash
dotnet restore
dotnet build --configuration Release
dotnet test
```

**The extension project needs SSMS 22 installed on the build machine.** It references undocumented
SSMS assemblies (`SqlWorkbench.Interfaces.dll`, `SQLEditors.dll`, `Microsoft.SqlServer.GridControl.dll`)
by path from the install folder. They are referenced at compile time only and are never redistributed
in the output. If your install path differs from
`C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\`, edit the paths in
`SQLExtended/SQLExtended.csproj`.

The test project links its sources directly and needs none of that, so `dotnet test` runs anywhere —
which is also all that CI can check.

To debug, set the startup program to the SSMS executable in that folder.

## Known limitations

- SSMS 22 only. Earlier SSMS versions are a different shell.
- A good deal of this reaches into SSMS internals by reflection, because there is no public API for
  the active connection, the results grid, tab colouring, or the Messages pane. Those paths are
  defensive and fail soft, but a future SSMS release can break them.
- Parts of the system catalog and monitoring SQL have been verified by parsing rather than against
  every SQL Server release and edition. Bug reports with a version number are useful.

## Contributing

Issues and PRs welcome. Worth knowing before you start: `CLAUDE.md` in the root is the real
architecture document — it records *why* things are shaped the way they are, especially the many
places where the obvious implementation was tried and failed silently. Read the section for the area
you're touching first.

## Licence

MIT — see [LICENSE](LICENSE).

Includes third-party code; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The statistics
parser is vendored from [Brent Ozar Unlimited's Statistics Parser](https://github.com/BrentOzarULTD/StatisticsParserExtension)
(MIT). Environment Tabs was inspired by [SSMS-EnvTabs](https://github.com/Blake-goofy/SSMS-EnvTabs),
though no code is shared.

Not affiliated with or endorsed by Microsoft. SQL Server and SSMS are trademarks of Microsoft Corporation.
