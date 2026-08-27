# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SQLExtended is an SSMS 22 extension (VSIX) providing schema viewing and SQL formatting. It targets .NET Framework 4.8 and runs inside the Visual Studio 2026 shell that SSMS 22 is built on.

Two main commands:
- **View Schema** (Ctrl+Shift+D) — shows CREATE TABLE script, indexes, and foreign keys for the object under cursor
- **Format SQL** (Ctrl+K, Ctrl+F) — formats T-SQL with customizable options

## Build Commands

```bash
dotnet restore
dotnet build --configuration Release

# Run tests
dotnet test

# Run a specific test class
dotnet test --filter "ClassName=FormatterTests"
```

**Prerequisites**: Visual Studio 2022/2026 with "Visual Studio extension development" workload, .NET SDK 10.0.201+, and SSMS 22 installed. SSMS internal DLL reference paths in `SQLExtended/SQLExtended.csproj` are hardcoded to `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\` — update if your install differs.

**Debug target**: Set startup program to `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Ssms.exe`

## Architecture

### Request Flow (View Schema)

```
SchemaViewerCommand.Execute()
  → EditorHelper.GetObjectNameAtCursor()     // expands cursor to qualified name
  → ConnectionHelper.GetActiveConnectionString()  // reflection into SSMS internals
  → SchemaQueryService.GetSchemaScript()     // T-SQL queries against sys.* views, cached
  → SchemaDialog (WPF)                       // dark-themed display dialog
```

### Request Flow (Format SQL)

```
FormatCommand.ExecuteFormat()
  → FormatterOptions.Load()                  // from %APPDATA%\SQLExtended\SSMS\formatter-options.json
  → SqlFormatterService.Format()             // wraps TransactSql.ScriptDom parser
    → PostProcessor                          // cleans up ScriptDom output
  → Replace in editor with undo context
```

### Key Design Decisions

- **ConnectionHelper uses three reflection-based fallback strategies** to extract the active connection from undocumented SSMS internals (ServiceCache, ScriptFactory, ServiceProvider). All wrapped in try/catch to prevent SSMS crashes.
- **SchemaQueryService caches** results in a `ConcurrentDictionary` keyed by `"connStr|schema|name"`. Cache is per-session only (clears on SSMS restart).
- **All UI thread operations** must go through `ThreadHelper.JoinableTaskFactory`. Database queries run on background threads.
- **SSMS internal DLLs** (`SqlWorkbench.Interfaces.dll`, `SQLEditors.dll`, `Microsoft.SqlServer.GridControl.dll`) are undocumented and referenced from the SSMS install folder — marked `Private=false` so they're excluded from the VSIX output.

## Projects

| Project | Description |
|---------|-------------|
| `SQLExtended/` | Main VSIX extension (net48) |
| `SQLExtended.Tests/` | xUnit tests (links formatter source files directly) |
| `SoluitionDocs/` | Non-building documentation project |

## Subsystem notes

Each subsystem keeps its design notes in a `CLAUDE.md` beside its code, and those notes are
the record of what already shipped wrong there — almost every rule in them is guarding a
failure that was silent on screen. **Read the file for a subsystem before changing it**; they
load automatically when a file in that folder is opened, but not while merely planning.

| Area | Notes |
|------|-------|
| Formatting (`PostProcessor`, alias/CASE/comment passes) | `SQLExtended/Formatting/CLAUDE.md` |
| The session log, and what the soft catches swallow | `SQLExtended/Diagnostics/CLAUDE.md` |
| Schema export (SMO scripter, folder-compare shape) | `SQLExtended/Export/CLAUDE.md` |
| Encrypted modules (DAC, the XOR technique) | `SQLExtended/Decryption/CLAUDE.md` |
| SQL Search, including Agent job steps | `SQLExtended/Search/CLAUDE.md` |
| IntelliSense — identifier bracketing, the `sys.` catalog cache (`Cache/SystemCatalogCache.cs`) | `SQLExtended/IntelliSense/CLAUDE.md` |
| Results grid — aggregates pane and Find in grid | `SQLExtended/ResultsGrid/CLAUDE.md` |
| Statistics parser (vendored from Brent Ozar — don't edit) | `SQLExtended/Statistics/CLAUDE.md` |
| Monitoring — pinning and staged collection, common to all four dashboards | `SQLExtended/Monitoring/CLAUDE.md` |
| … Performance dashboard and the build catalog | `SQLExtended/Monitoring/Performance/CLAUDE.md` |
| … Agent jobs dashboard and `JobDialogLauncher` | `SQLExtended/Monitoring/Jobs/CLAUDE.md` |
| … Always On monitor | `SQLExtended/Monitoring/AlwaysOn/CLAUDE.md` |
| … Replication monitor | `SQLExtended/Monitoring/Replication/CLAUDE.md` |
| Environment Tabs (tab colouring by connection) | `SQLExtended/EnvTabs/CLAUDE.md` |
| Object Explorer context menu | `SQLExtended/ObjectExplorer/CLAUDE.md` |

## Releasing

Cutting or publishing a release, or touching `version.txt`, `publish-release.ps1` or the update
check: **invoke the `releasing` skill** (`.claude/skills/releasing/SKILL.md`). It carries the
three-consumers-must-agree version rules and the guards that enforce them; `SoluitionDocs/Deployment.md`
is the long form.

## Claude Code Instructions

- When running `dotnet build`, always use `-v q` (quiet verbosity). On success, do not read the output. On failure, only examine error lines: `dotnet build -v q 2>&1 | grep -E "error |failed"`
- When running `dotnet build` after a recent restore, use `--no-restore` to save time and output
- Exclude `bin/` and `obj/` folders from all file searches and reads
- Save plan files to `.claude/plans/` in the project root, not the user home directory

## Code style

- C# line width: 160 chars. Prefer fewer line breaks; let lines run rather than splitting method chains over many lines.
- Use file-scoped namespaces, target-typed `new`, primary constructors where they reduce noise.
- Don't reformat code outside the scope of the change.

## VSCT (Command Table)

`SsmsSchemaViewerPackage.vsct` defines menu entries and keyboard shortcuts. Commands appear both in a top-level "SQLExtended" menu and the editor context menu (right-click).

The `.vsct` and the three `*.vsixmanifest` files at the project root — which is committed, which is
generated, and which one `StampVsixVersion` actually edits — are documented in
`SQLExtended/MANIFESTS.md`. Read it before touching any of them.
