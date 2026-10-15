# Safety: UPDATE / DELETE without WHERE

`UnsafeDmlGuard` asks before SSMS executes an UPDATE or DELETE with no WHERE clause and cancels the execution on
No (Settings → Editor → Execution safety, `SQLExtendedSettings.UnsafeDmlGuardEnabled`, **on by default**).

- **The hook is `CommandEvents.BeforeExecute` on the shell's `Query.Execute`**, the one command behind F5,
  Ctrl+E and the toolbar's Execute, and the one `ScriptLibrary` runs through `dte.ExecuteCommand`. Its guid/id
  belong to SSMS's SQL editor package and are undocumented, so they are **resolved by name** at startup. When that
  fails the guard is simply absent — on screen identical to "no unsafe statements" — which is why the session log
  records `Hooked Query.Execute (…)` or a warning. Check that line first when the dialog never appears.
- **The `CommandEvents` object is held in a static.** DTE event sources are COM objects; an unreferenced one is
  collected and its handler stops firing some minutes into the session, with no error.
- **It fails open.** Any exception in the check is logged and the execution proceeds — blocking every F5 because
  the guard broke is worse than the guard being off. A script that does not parse is also let through (the server
  reports the syntax error); SQLCMD-mode scripts (`:setvar`, `$(var)`) fall in this bucket and are not checked.
- **What runs is what is checked**: the selection when there is one, otherwise the whole document — as SSMS does.
- **The dialog defaults to No**, so an Enter pressed out of habit cancels.

`UnsafeDmlAnalyzer` is ScriptDom-based (comments and strings never count as a WHERE) and free of VS, so the tests
link it (`SQLExtended.Tests/Safety/`). Deliberate exemptions, each pinned by a test: table variables and `#temp`
targets, a target that is one side of an INNER JOIN whose ON names it (not an inner join between other
tables, nor `ON 1 = 1`), `WHERE CURRENT OF`, and statements inside CREATE/ALTER
PROCEDURE/FUNCTION/TRIGGER (note `TriggerStatementBody` does **not** derive from `ProcedureStatementBodyBase`).
`TOP (n)` is still flagged — a `DELETE TOP (n)` loop is a whole-table purge. MERGE and TRUNCATE are out of scope.

Not yet observed in a running SSMS 22: that `Query.Execute` resolves by that name, and that `CancelDefault`
stops the execution.
