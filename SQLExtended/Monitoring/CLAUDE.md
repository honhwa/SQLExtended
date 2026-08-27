# Monitoring Subsystem

`Monitoring/` holds four dockable dashboards that read live server state from a harvested SSMS connection. All
are **live-only** — nothing is persisted — and all collect each section inside its own try/catch so one
unavailable view costs one tab, not the dashboard. The Performance and Always On dashboards need only
`VIEW SERVER STATE` and force the connection to `master` (the pinned connection may name a non-readable
secondary's database); the Agent jobs and Replication dashboards are the exceptions on both counts — see below.

**All four are pinned to the server they were opened from and are multi-instance** (`Monitoring/MonitorPinning.cs`).
None of them follows the active query window: these windows stay up for minutes or hours while you work in query
windows connected elsewhere, and one that silently re-pointed itself would move every reading — and for Agent jobs
and Replication, every *action* — onto whichever editor last had focus. The rules, shared by all four:
- `MonitorWindows.AcquireAsync` picks the window: the one already pinned to the requested server (activating it
  rather than opening a duplicate that would poll the same instance in parallel), else a new instance on the lowest
  free id, else — at the 10-window cap — the lowest-numbered window, re-pinned **with a banner saying so**.
  Silently displacing a window's contents is the one outcome that is not allowed.
- `MonitorPin` holds the connection **as harvested**, not pointed at a database. Each dashboard re-points it per
  poll (msdb, master, the distribution database), and Replication derives three at once — so normalising at pin
  time would throw away what it has to work from. `MonitorPin.Set` returns whether the *server* changed, which is
  the signal to drop capability probes, delta baselines, history buffers and the grids.
- `PinnedServerKey` is the connect target alone and deliberately **not** the login, so the same instance reached
  with different credentials reuses the window rather than quietly opening a second one for it.
- A poll is discarded if the pin moved while it was in flight, and re-queued in the `finally` — `BeginRefresh`
  refuses to overlap polls, so the re-pin's own refresh was already turned away.
- Panes are `Transient` as well as `MultiInstances`: a pinned connection cannot be restored at startup, so a window
  VS brought back would come up empty. Each pane frees its instance id from `Dispose` (`MonitorWindows.Forget`) —
  multi-instance panes are destroyed on close, not hidden, and a leaked id would burn a slot for the session.
- Captions carry the server (`Performance — PROD-SQL01`), starting from the connect target and switching to
  `SERVERPROPERTY('ServerName')` after the first poll; the header's tooltip keeps the connect target, since behind
  an AG listener or a CNAME the two differ. With several windows open the caption is the only thing telling their
  tabs apart, so it is not decoration.
- The header names the **login** beside the server (`PROD-SQL01  as CORP\alice`), on the same
  server-is-authoritative preference: `SUSER_SNAME()` once a poll returns one, and until then what the connection
  string can say — its `User ID`, or the process's Windows identity for integrated security, which is exactly what
  the server will report (`MonitorWindows.ConnectionLogin`, `MonitorPin.LoginFor`). It matters because these windows
  fail *quietly by permission*: a short job list, a blank distribution tab, a missing `VIEW SERVER STATE` section all
  read as "nothing there" rather than "not visible to this login" — and because the same server reached with
  different credentials deliberately reuses one window (`PinnedServerKey` excludes the login), so the header is the
  only place the difference shows. It is **not** in the caption: the tab strip has room for the server and nothing
  else, and the server is what distinguishes the tabs.
- **Every pane needs its own GUID.** Replication and Performance both shipped `…F60008` for a while; two panes
  sharing a GUID means VS cannot tell their frames apart. Replication is now `…F6000A`.

Two of them (Always On, Replication) carry a **Diagnostics tab**: a rules engine evaluated client-side over the
snapshot the other tabs are already built from, costing no extra round trip. Both follow the same contract —
findings sorted worst-first, each with a plain-English "what it means / what to do", an explicit all-clear row
when nothing fires (an empty grid reads equally as "healthy" and "this tab is broken"), and thresholds in
`SQLExtendedSettings` rather than hard-coded. The engines are pure functions of the snapshot, which is also why they
are the only part of either subsystem with unit tests.

Shared plumbing sits directly in `Monitoring/`: `MonitoringTheme.xaml` (buttons, tabs, grid chrome, converter
instances — merged by the controls via a pack URI, so the windows can't drift apart visually),
`MonitoringConverters.cs`, `RowMerge.cs`, `Sparkline.cs`, `MonitorPinning.cs` (pinning and window instances,
above) and `MonitorCollection.cs` (the section plan, below). Four copies of any of these would drift, and the
difference would be felt as one dashboard behaving unlike the others rather than as a bug anyone goes looking for.

## Staged collection

Each dashboard's `CollectAsync` builds a **`MonitorPlan`** — the ordered list of sections it intends to read — and
then runs it, rather than `await`ing a series of section calls in line. Three things fall out of that, all of them
answers to the same complaint: a window that says "Collecting…" and nothing else cannot be told apart from one that
has hung, gives no clue which read is the slow one, and withholds numbers that were ready in the first 200 ms until
the last and often least interesting section has finished.

- **Progress is reported per section** (`MonitorStep` → `MonitorStatusReporter` → the status line), as
  `Reading replica states…  (3 of 9)`. The denominator comes from the plan, so a section made conditional on a
  capability probe changes it by construction — a hand-kept total would be wrong on the first server that lacked
  one. `MonitorStatusReporter` is deliberately **not** `System.Progress<T>`: that captures
  `SynchronizationContext.Current` and silently falls back to the thread pool when there is none, which here means
  touching a `TextBlock` off the UI thread.
- **Sections marked `primary` run first and are shown before the rest are read.** They are the ones backing the tab
  the window opens on: groups/replicas/databases (Always On), the baseline and vitals plus waits and file I/O
  (Performance), jobs and their activity (Agent jobs), and the distributor-side reads (Replication). What is
  deferred is what costs the most and is furthest from view — the top-queries scan and the whole Server info read,
  `sysjobhistory`, the per-subscriber-database connections. Each control's `Apply` splits to match
  (`ApplyOverview`/`ApplyLive`/`ApplyJobs` + `ApplyRemainingTabs`), and the early half **must be idempotent** — it
  runs twice per user-initiated poll. Anything that accumulates (`history.Record`) belongs in the second half, or
  every chart gains two samples per poll.
- **The early-paint hook is `await`ed, not fired off.** That is the whole reason it is safe: the collection is
  stopped while the UI merges the snapshot's rows, so the two threads never touch it at once. Firing it off would
  cost a `List` being enumerated on one thread while another appends to it.
- **Both are passed only when the poll is user-initiated** — the first one and an explicit Refresh. On the
  five-second timer they would replace a settled summary with a flicker of step text and re-merge the first tab
  twice per tick, for a window that is already populated.
- The timing line now reads `14:03:21 · 9 sections · 212 ms` (`8 of 9 sections` when any failed). What a dashboard
  can cover varies with the release and with the login's rights, so a section count beside an empty tab says
  something the duration alone does not.

`MonitorCollection.cs` is free of the VS threading assembly so the test project can link it
(`SQLExtended.Tests/Monitoring/MonitorPlanTests.cs`) — `MonitorStatusReporter` is the half that needs it and is
in its own file for that reason. Everything the plan guarantees fails quietly on screen: a section run out of order
enriches rows that have not been read yet and just leaves columns blank, a hook fired at the wrong point paints a
half-collected tab as finished, and a swallowed exception looks like a server with nothing to report.

Two rules apply to the Performance and Always On dashboards:
- **Grids are merged in place by key** (`RowMerge`), not rebound. A 5-second refresh that swaps `ItemsSource`
  throws away selection and scroll, which is intolerable when you are watching one row. Rows therefore raise
  `PropertyChanged`, and history buffers are handed out as a **fresh array** each poll — a DependencyProperty
  set to a reference-equal value is a no-op, so a mutated buffer leaves sparklines frozen.
- **`Sparkline` is hand-rolled** on a `DrawingContext` rather than pulled from a charting package — see the
  `ProvideBindingPath` comment on the package for why shipping another third-party UI assembly is a bad trade.
