# Schema cache

`SchemaCache` is a process-lifetime singleton over an in-memory map and a SQLite store
(`SchemaCacheSqliteStore`), initialised once from the package. `SystemCatalogCache` is the `sys.` half and
has its own notes in `IntelliSense/CLAUDE.md`.

## The four Schema Cache settings, and why each is where it is

`AutoRefreshIntervalMinutes`, `MaxCacheAgeDays`, `AutoLoadOnConnect` and `DetectDdlChanges` were persisted
and bound to their controls and **never read** — the refresh timer was hardcoded to five minutes, the purge
to seven days, and the other two to "always". The general case is recorded in `IntelliSense/CLAUDE.md`; what
is specific here is that all four are read on the UI thread and pushed down, never read where they are used:

- **`SQLExtendedSettings.Current` faults itself in on first touch and must not do that from a worker thread**
  (`Diagnostics/CLAUDE.md`), and every place these values are *needed* is one — the refresh timer's callback,
  the store's open, `DatabaseChangeMonitor`'s poll. So `Initialize` reads the settings object once and passes
  `MaxCacheAgeDays` into `_store.Initialize`, `ApplyRefreshSettings` copies `DetectDdlChanges` into a field
  for the timer thread, and the monitor reads `AutoLoadOnConnect` inside the `SwitchToMainThreadAsync` block
  it already had. Reading them in place would work on every machine where something else had already touched
  the settings first, which is most of them.
- **The refresh interval and DDL detection are re-applied on `SQLExtendedSettings.Changed`, not only at
  startup.** The dialog does not say "applies on the next start" for these two the way it does for
  `DatabaseChangePollSeconds` — it says "set to 0 to disable", and a disable that needs an SSMS restart is
  the same silence the dead setting was. `SchemaCache` unsubscribes in `Dispose`, as that event requires.
- **Interval 0 disposes the timer outright**; explicit refreshes and the connection-triggered load are
  untouched either way. `DetectDdlChanges` off returns from the tick instead, leaving the timer armed, so
  turning it back on needs no restart.
- **`DetectDdlChanges` *is* the incremental refresh.** "Detect DDL changes and auto-refresh affected objects"
  is exactly what `IncrementalRefreshAsync` does — `LoadModifiedSince` against `modify_date` — so with it off
  the periodic tick has nothing else to do.
- **`MaxCacheAgeDays <= 0` purges nothing.** A cache nobody asked to expire is not stale, it is cold, and
  dropping it costs a full reload of every database on the next completion.
- **`AutoLoadOnConnect` off still tracks the switch**, so the snippet placeholders and the next explicit load
  see the right database — only the fetch is skipped. The status bar says *why* nothing loaded
  ("not cached (auto-load off)") rather than going quiet, because an uncached database with a silent status
  bar reads as a broken cache.
