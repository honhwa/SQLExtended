# Diagnostics: the session log

`Diagnostics/` is where the failures this codebase deliberately swallows go. Almost every catch block in
the extension is a soft one on purpose — a cache load that throws must not take SSMS with it, a dashboard
section that fails must cost one tab — and the cost is that the reason disappears. **Neither of the two
places it could have gone works here**, which is the whole reason this exists: `Debug.WriteLine` is
`[Conditional("DEBUG")]` and so is not compiled into a Release VSIX at all, and `ActivityLog.xml` is only
written when SSMS was launched with `/log`, which on the machine where the problem is happening it was not.
`EnvTabsDiagnostics` already reached this conclusion for one subsystem; this is that, for all of them.

Reached from **Settings → Diagnostics**. Two switches (`SQLExtendedSettings.DiagnosticLogEnabled`,
`DiagnosticLogToFile`), a live grid, and Copy All.

- **Off by default, and a session.** Nothing is captured while `SQLExtendedLog.Enabled` is false, and what is
  captured lives in a 500-entry ring and dies with SSMS. The switch is not a verbosity level.
- **The log view is hidden until logging is on, but the two switches are not** — otherwise there is nowhere
  to turn it on from. The "off" note takes the grid's place so the tab never reads as an empty log.
- **`SQLExtendedLog` never reads settings, and never throws.** The first because most callers are on a worker
  thread and `SQLExtendedSettings.Current` must not be faulted in from one (the rule `PerfRecentDumpDays`
  follows) — the flags are pushed in by `Configure` from the UI thread, at the *top* of the package's
  `InitializeAsync` so that the ~30 command registrations below it can report, and again from the settings
  dialog's OK. The second because every caller is already handling a failure, and a logger that throws
  turns a handled one into a crash.
- **Repeats are counted, not appended.** Everything that logs here is on a timer — the cache refreshes every
  few minutes, the dashboards poll every five seconds, completion runs per keystroke — so one unreachable
  server produces the same line indefinitely and would push the whole ring out inside a minute, losing
  exactly the earlier entries that explain it. Only the entry *at the end* of the ring collapses: matching a
  non-adjacent one would reorder the timeline. Both ends of a run are kept, so collapsing never hides when
  it started.
- **The exception chain is recorded, not just the message.** Reflection failures arrive wrapped in
  `TargetInvocationException`, whose own message says nothing at all (the same problem `JobDialogLauncher`
  unwraps by hand), and a `SqlException`'s **number** is what a login or permission failure is actually
  looked up by. Never the connection string — it can carry a password.
- **The file sink is the separate opt-in**, at `%APPDATA%\SQLExtended\SSMS\logs\sqlextended-yyyy-MM-dd.log`, for a
  problem that has to leave the machine. It writes **every occurrence** where the ring collapses them (a file
  being grepped later wants the timeline), which is why it needs its own bounds: 20,000 lines a session,
  daily files, pruned after a week. **A write failure turns the file sink off and says so in the ring** — a
  sink that silently stopped is read as "no errors since", and losing the file is not a reason to lose the
  log. Pruning filters on the extension rather than trusting the `sqlextended-*.log` wildcard, for the reason
  the schema export documents.

`ActivityLogHelper.LogError` **mirrors into the ring first**, which picks up everything already routed
through it (all four monitoring dashboards, the history window, the job dialog) without touching them.
Beyond that, the call sites wired so far are the ones whose silence has actually cost time: `SchemaCache`'s
full and incremental loads, the encrypted-module list and decryption, `SystemCatalogCache`'s failure memo
(which is never retried in a session, so that catch is the only chance to say why `sys.` went quiet),
both database enumerations (`SqlCompletionSource.GetDatabaseNames` and `ObjectExplorerHelper.GetDatabases`),
`SchemaQueryService`'s cache path, and the package's own command registrations. **The rest of the codebase's
soft catches are still silent** — `SQLExtendedLog.Error` is simply available to them now.

`ObjectExplorerHelper.GetDatabases` is the one where the logging changed the shape of the method. It merges
the server's list over the cache's, so **a server it cannot reach still returns a plausible list** — and
every consumer ("cache all databases", SQL Search's all-databases scope, Schema Validation) reads what comes
back as the whole server. It excludes databases the login cannot open, as the completion path's enumeration
does — listing one costs a failed cache load per database, which reads as the server being broken rather than
as a database nobody granted — but it **selects `HAS_DBACCESS` rather than filtering on it**, so the ones left
out are counted and reported (`Enumerated 9 of 12 database(s) … 3 skipped as not open to this login`) instead
of quietly going missing. Filtering server-side would be shorter and would silently shrink the list, which is
the failure this file keeps running into. `ISNULL` is load-bearing: the function returns NULL for a database
not in a state to be opened, and nothing guarantees the ONLINE predicate is evaluated first. The cached names
merged in are *not* access-checked — the cache is per server and persists across sessions, so it can still
carry a database an earlier login could open.

The cache read and the master query have **separate** try/catches: sharing one
made "the server refused us" and "the cache was empty" indistinguishable, and it put the reader loop inside
the same handler, so a failure part way through the rows truncated the list silently. The failure line says
how many databases are being returned and that they may not be all of them. It also reports when
`GetMasterConnectionString` could not rewrite the catalog — that method hands back its input on a parse
failure, so the enumeration then runs against the connection's own database, which on Azure SQL Database
answers with master plus that one database and looks like a real answer.

`ConnectionHelper.GetActiveConnectionString` is the one that earns its own note: it reports which of the
three reflection strategies answered, what authentication the result expresses, and — loudly — when an
Azure-looking server has been harvested as **integrated security**. `BuildConnectionString` can only spell
Windows auth or SQL auth with a password reflection could reach, so every Entra mode and every SQL login
whose password it could not read both fall back to integrated security against a server that has no idea
what a Windows account is. It then fails at the far end, on a background thread, as a login error naming
nothing about where the credentials came from. That is the most likely reason an Azure SQL database will
not cache, and without this line nothing on the machine says so.

`Diagnostics/DiagnosticLogBuffer.cs` holds the ring and is free of VS, WPF and SqlClient so the test project
can link it (`SQLExtended.Tests/Diagnostics/DiagnosticLogBufferTests.cs`) — the static facade is the half
that needs them and is in its own file for that reason, the same split `MonitorCollection` /
`MonitorStatusReporter` exist for. Its clock is a parameter for the same reason. Worth pinning because
everything it holds is itself a report of something that already failed quietly: a ring that evicts the
wrong entry, collapses two different errors into one line, or loses a run's start time is
**indistinguishable on screen from the failure never having been recorded**.

One WPF detail in the tab: WPF forwards a *single property* change to the dispatcher itself, which is what
lets a repeat count update in place from the poll thread that logged it, but a **collection** change from
off-thread throws — hence the `Dispatcher.BeginInvoke` around every add and remove, and `BeginInvoke`
rather than `Invoke` because the logging thread is mid-failure and must not be made to wait on a UI thread
that is itself blocked on the modal dialog.
