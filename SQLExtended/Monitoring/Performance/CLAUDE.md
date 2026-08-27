# Performance dashboard

> Pinning, staged collection and the rules common to all four dashboards are in
> `SQLExtended/Monitoring/CLAUDE.md` — read that first.

`Monitoring/Performance/` (Ctrl+Alt+P, `PerfMonitorToolWindow`). Tabs: Live, Activity, Blocking, Waits,
Top queries, File I/O, Server info.

The central idea is **deltas, not totals**. Wait stats, file I/O stalls and most performance counters are
cumulative since instance start; read once they describe the server's whole life, not the slowdown happening
now. `PerfDeltaTracker` keeps the previous reading of each and subtracts on the next poll. Notes:
- The interval comes from the server's own `sys.dm_os_sys_info.ms_ticks`, not the client clock.
- A negative delta means the counters were cleared or the host restarted; it is reported as null, never as a
  negative rate.
- The **first** poll per server reads the cumulative sources twice, 1 s apart (`BaselineSampleMs`), so the rate
  columns have real numbers immediately rather than a grid of dashes. That cost is paid once, not per poll.
- Toggling "include benign waits" changes the server-side filter, so it clears the tracker — the stored
  baseline no longer matches the row set the next poll returns.
- CPU is the exception to all of this: `RING_BUFFER_SCHEDULER_MONITOR` already holds an hour of per-minute
  samples, so that chart is populated on the first poll and needs no local history.

Blocking chains are derived **client-side** from the already-collected activity rows rather than by a second
query — two round trips would let the Activity and Blocking tabs disagree about the same instant. The walk
guards against cycles (a deadlock mid-resolution). The activity query deliberately UNIONs in sleeping sessions
holding an open transaction: they have no row in `sys.dm_exec_requests` at all yet are a classic head blocker.
Every `NULL` in that second branch is explicitly `CONVERT`ed — a bare `NULL` literal is an `int`, and `int`
outranks `nvarchar` in UNION type precedence.

`ActiveGrid()` and `SqlForActiveTab()` switch on named `Tab*` constants, for the reason given under the Always
On monitor — they were bare indices until the Server info tab was added.

## Server info tab

`PerfServerInfoQuery.cs` reads what the instance says about itself; `SqlBuildCatalog.cs` says where its build
sits in the servicing and support timeline, against a snapshot of
[sqlserverbuilds.blogspot.com](https://sqlserverbuilds.blogspot.com/) generated into `SqlBuildData.cs`.

It is the **one tab not on the poll timer.** Nothing on it changes on a five-second scale except uptime, so it
is collected on the first poll for a server and on an explicit Refresh (`includeServerInfo`), and the tile strip
names the collection time because that is the "as at" for uptime. That is also what makes its capability probe
affordable.

- **Optional DMVs and columns are probed, never branched on by version** (`PerfServerInfoQuery.Capabilities`,
  the same shape as `AgCapabilities`). A batch binds as a whole, so one statement naming `sys.dm_os_host_info`
  against SQL Server 2016 does not cost that one row — it fails the command and empties the tab. Absent columns
  become `CONVERT(<type>, NULL) AS <alias>` so the reader can always address every column by name, and the two
  optional DMVs each have a third rendering that returns **no rows but still one result set**: losing it would
  shift every later result set by one and read the configuration rows as service rows. `PerfSqlTests` pins the
  result-set count at 7 for both a full and an empty capability set.
- **`SERVERPROPERTY` needs none of that** — it returns NULL for a property the release does not know, which is
  why the identity block names newer properties (`InstanceDefaultBackupPath`, `ProductBuildType`) freely.
- `sys.dm_os_windows_info` (the pre-2017 fallback) has **only** `windows_release`,
  `windows_service_pack_level`, `windows_sku` and `os_language_version` — no distribution or SKU *name* column.
  That alias is a typed NULL rather than an invented one, for the binding reason above.
- `value` vs `value_in_use` in `sys.configurations` differ on a setting changed but not restarted into, which
  the tab flags. A max-memory change nobody restarted for is invisible everywhere else.
- The handful of settings tinted amber (default max server memory, MAXDOP 0 on a wide host, cost threshold left
  at 5, priority boost, fibre mode, `xp_cmdshell`, uneven tempdb files, instant file initialization off, a recent
  memory dump) is deliberately short. **This is a facts tab, not a rules engine** — a screenful of amber hides the
  real ones.
- **`sys.dm_server_memory_dumps` is on the tab because nothing else reports a crash after the fact.** SQL Server
  writes a dump on an assertion, an access violation or a non-yielding scheduler and then carries on, so unless
  someone was reading the error log that day it leaves no trace anywhere a DBA routinely looks. Amber when the
  newest is within `SQLExtendedSettings.PerfRecentDumpDays` (30 by default; 0 lists them without flagging any). That
  value is **read on the UI thread and passed down** through `PerfQueryService.CollectAsync` — this collection runs
  on a worker and `SQLExtendedSettings.Current` must not be faulted in from one, which is why it is a parameter rather
  than a lookup. The rows are capped at 20 (newest first) while the **count comes from the view**, so the cap can
  never understate how many there are. Probed like the other optional DMVs: it is 2008 R2 SP1 and later and absent
  on Azure SQL Database, and its absent form still returns one empty result set for the reason every other one does.
- Properties are sorted into groups after collection (`SortByGroup`). They are added in result-set order, which
  interleaves them: uptime arrives with the second read and the support dates are worked out after the reader
  closes, so an unsorted grid runs "Instance" rows down the page in three separate stretches.

**The build catalog exists to answer "is this server patched", and the whole risk is answering it wrongly.**
`SqlBuildCatalog.cs` is free of SqlClient and WPF so the test project can link it along with the generated data,
because every rule in it fails silently on screen:

- **`SqlVersion` compares component by component, never as text.** `16.0.4265.3` sorts *below* `16.0.985.1`
  lexically, which reports a fully patched server as years behind and a stale one as current. Pinned by
  `SqlBuildCatalogTests`.
- **The release key is `(major, minor)`, not major.** `10.50` is 2008 R2 and `10.0` is 2008 — different products
  whose support dates are five years apart.
- **"Newest listed", "newer than the snapshot" and "not listed" are three different answers** and
  `SqlBuildMatch` keeps them apart. Collapsing the middle one into the first turns "the build list is older than
  this server, so I cannot tell" into a clean bill of health — and since the snapshot goes stale the moment
  Microsoft ships a CU, that is the *normal* case, not an edge case. `NewerThanCatalog` is worded as a statement
  about the snapshot. The snapshot date is on screen either way.
- The list does not claim to be exhaustive, so an unlisted build reports the **closest listed build below it**
  as "or later" rather than pretending to identify it.
- **Azure SQL Database, Managed Instance, Synapse, Edge and Fabric get no patch verdict at all**
  (`PerfServerInfo.IsAzureManaged`, by `EngineEdition`). Microsoft patches those and their `ProductVersion` does
  not correspond to a box-product build, so "3 CUs behind" would be nonsense.
- **Withdrawn and pre-release builds outrank being behind** in the verdict. Withdrawn is carried on the source
  page in a CVE-styled chip rather than a column, so it parses away by accident — it did once, and a test now
  asserts the snapshot still knows some builds were withdrawn.

**There is no runtime fetch, deliberately.** This runs inside SSMS on machines that are frequently offline or
locked down, a monitoring tab is the last place a surprise outbound request belongs, and parsing 600 KB of
someone else's HTML at runtime would fail on the first layout change. `SoluitionDocs/Tools/generate-sql-build-catalog.py`
regenerates `SqlBuildData.cs` from the page instead (`python … [saved.html]`); it is the only thing that makes
the data newer. Notes for whoever runs it next:
- It **reports any build row it could not key to a release** rather than skipping it quietly. That is how SQL
  Server 2008 R2 was found to have gone missing entirely: the summary table spells its name with `&nbsp;`, so a
  markup-level "SQL Server" match dropped the release, and with it all 75 of its builds — silently.
- Chrome has to come off before the text is read: the `class=lcu`/`lsp`/`lrtm` chips and the red `*new` marker
  otherwise land in the description ("Microsoft SQL Server 2025 RTM **RTM**").
- The derived servicing label (`CU19`, `SP2 CU17`, `CU6 + security update`) is a convenience; the list's own
  wording is stored verbatim alongside it so the label is always checkable. A test asserts ≥90% of builds for
  2016+ carry a label — a generator regression shows up as labels vanishing en masse, not one at a time.

`PerfSqlTests` parses every SQL constant in the dashboard with ScriptDom. These batches are long and assembled
from fragments, and a syntax error in one does not fail loudly — the section's try/catch turns it into a warning
banner and an empty tab, which reads as "this DMV is unavailable here". Parsing is the only check available
without an instance; it cannot tell whether a column exists on a given release, which is what the probe is for.
