# Agent jobs dashboard

> Pinning, staged collection and the rules common to all four dashboards are in
> `SQLExtended/Monitoring/CLAUDE.md` — read that first.

`Monitoring/Jobs/` (Ctrl+Alt+J, `JobsToolWindow`). Tabs: Jobs, Steps, History. It exists because **Object
Explorer Details is not extensible**: OED's per-node column sets are declared in `.ssmc` registration XML
(the Jobs view lives in `Extensions\Application\Microsoft.SqlServer.Management.SqlStudio.Explorer.dll`,
resource `…Explorer.Configuration.SqlExplorer.ssmc`, under `<UrnPath>Server/JobServer/JobsFolder</UrnPath>`),
and the list of `.ssmc` sources SSMS loads is itself an embedded resource (`SqlStudioReferences.full.ssmc` in
`Microsoft.SqlServer.Management.SqlStudio.dll`) with hardcoded `resource://` URIs. No pkgdef key, folder scan
or MEF export lets a VSIX add one. Don't go looking again.

Two deliberate deviations from the other dashboards (pinning is now common to all four — see above):
- The connection is forced to **`msdb`**, not `master` — everything read here lives there.
- It is the only one with an **Object Explorer entry** (Jobs folder / a job → "Agent Jobs Dashboard…"), which is
  therefore the one launch point needing no query window at all: the node carries its own connection
  (`NodeContext.ConnectionString`), so any server connected in OE can be pinned. The same `Show(package,
  connectionString, serverLabel)` signature is on all four commands if the others ever want OE entries too.
- The permission is **`SQLAgentReaderRole`** (or sysadmin/`SQLAgentOperatorRole`), *not* `VIEW SERVER STATE`.
  A login with none of them silently sees only the jobs it owns, so the probe checks role membership and
  records a warning. A short list that looks complete is the worst outcome available here.

Query notes (`JobQueryService`, one round trip, four result sets stitched on `job_id`):
- **Next run and running state come from `sysjobactivity`** for the current Agent session, not from
  `sysjobschedules`/`sysschedules`. `next_scheduled_run_date` is already a `datetime`, which skips decoding
  the `freq_*` columns entirely. Elapsed is `DATEDIFF(second, start_execution_date, GETDATE())` — computed
  server-side, because a client clock offset would otherwise produce a negative elapsed.
- **Last run and average come from `sysjobhistory` where `step_id = 0`** — the job-level summary row Agent
  writes after the last step, so one row per execution rather than one per step. The average covers
  `run_status = 1` only; a job that failed after two seconds would otherwise drag it somewhere useless.
- **Dates and durations are decoded client-side** in `JobValueParser` (`run_date` YYYYMMDD, `run_time` and
  `run_duration` HHMMSS-as-int, hours uncapped). `msdb.dbo.agent_datetime` would do the first half but is
  undocumented and throws on the zero dates Agent writes for jobs that never ran. This is the one piece with
  unit tests (`SQLExtended.Tests/Monitoring/JobValueParserTests.cs`); the test csproj links
  `JobValueParser.cs` alone because the rest of the folder pulls in SqlClient and WPF.
- Steps and history load **on demand** for the selected job, and only while one of those tabs is showing —
  `sysjobhistory` is the largest table in msdb on a busy instance.

`JobActionService` is the **only writing code in the subsystem** — Run now / Stop / Enable / Disable, each a thin
call to the msdb procedure SSMS itself uses (`sp_start_job`, `sp_stop_job`, `sp_update_job @enabled`). Notes:
- Jobs are addressed by **`@job_id`, never `@job_name`**: names are not unique across master/target servers and
  can be renamed underneath a dashboard that polls every few seconds.
- **Every action confirms first**, naming the job *and* the server. Pinning removed one hazard here (the window no
  longer re-points itself between reading and clicking) but not the other: with several windows open on different
  servers the tabs look identical, and a misclicked Stop on a production job does not undo. The prompt defaults to
  No, and it names the server for exactly that reason.
- **Nothing is permission-pre-checked.** These need SQLAgentOperatorRole (or ownership) to start/stop and
  ownership or sysadmin to change the enabled state — a step up from the read path's SQLAgentReaderRole. The
  server's own error ("job is already running", a permissions refusal) is more accurate than any guess, so it is
  surfaced verbatim.
- Start/Stop wait ~1s before refreshing. `sp_start_job` returns once Agent accepts the request, and
  `sysjobactivity` catches up a moment later; refreshing immediately shows the old state and reads as a no-op.
- Run now is offered on disabled jobs too — disabling only stops *scheduled* execution.

**Double-click opens SSMS's real Job Properties dialog** (`JobDialogLauncher`), reached entirely by runtime
reflection — there is no public API. The recipe was read out of SSMS's own Object Explorer menu definition
(`ObjectExplorer.dll`, embedded `sqlexplorermenuitems.xml`), where the Job node's default action is
`<Object name='JobProperties' base='PropertiesItem'>` naming
`Microsoft.SqlServer.Management.SqlManagerUI.JobPropertySheet` in `SqlManagerUi.dll`. So:
`CDataContainer` (public, `SqlMgmt.dll`) carrying the job's URN → `JobPropertySheet`'s public
`ctor(CDataContainer)` → hosted in `LaunchForm`, whose `ctor(ISqlControlCollection, IServiceProvider)` is also
public and which `SqlMgmtTreeViewControl` (the sheet's base) satisfies.

**`LaunchForm` resolves its host by service query, not by cast.** It throws "Host service provider MUST implement
ILaunchFormHost", but `InitializeForm` actually calls `provider.GetService(typeof(ILaunchFormHost))` — so passing
an object that *is* an `ILaunchFormHost` still fails with that exact message. Two pieces are needed: SSMS's
`UI.VSIntegration.ObjectExplorer.LaunchFormHost` (public, in `SqlMgmt.dll` despite the namespace, ctor takes the
provider to wrap), served by a tiny `HostServiceProvider` that returns it for any interface it satisfies and
delegates everything else to the package. Don't "simplify" that indirection away — it is the fix.

Three more things established by running the chain against a live Enterprise instance:
- `CDataContainer` derives **neither `ServerName` nor `ObjectName`** from the params document; set both directly.
  With them set it reports `IsNewObject = false` and resolves `SqlDialogSubject` to the right SMO `Job`.
- `LaunchForm` copies no caption off the hosted control in its ctor (the sheet pushes one through
  `ILaunchForm.Caption` later), so `form.Text` is assigned explicitly.
- Express editions throw `UnsupportedFeatureException` from the sheet's ctor ("Agent is not supported on this
  edition") — that is the dialog working, not a bug, and it means the local Express instance cannot test this path.

Four things that are easy to get wrong here:
- **Hand the connection over as a populated `SqlConnectionInfoWithConnection`**, not through CDataContainer's
  simpler `(serverName, trusted, user, password)` ctor. That ctor keeps only those four values and SMO rebuilds
  the connection from its own defaults, dropping the `TrustServerCertificate=true` that `ConnectionHelper` sets
  deliberately. It must be the `WithConnection` subclass specifically: the `(ServerType, object, bool)` ctor casts
  the object to it, so passing the `SqlConnectionInfo` base throws `InvalidCastException`. The subclass inherits
  every property and has a public parameterless ctor, so populating it costs nothing. Only primitives cross into
  SSMS's assemblies — which is also why our NuGet SMO 181 types must never be passed in; SSMS ships 18.100.
- **The params document must be rooted at `formdescription`**, not `params`. The dialogs read their inputs with
  absolute XPaths (`/formdescription/params/urn`, `/formdescription/params/servername`); the exact template is a
  string literal inside `SqlMgmt.dll`. A `<params>`-rooted document binds nothing and opens an empty sheet.
- **`<jobid>` and `<job>` are what put the sheet into edit mode — the `<urn>` alone does not.** `JobData`'s ctor
  reads only those two to decide (`originalName.Length > 0 || jobIdString != null` → `DialogMode.Properties`,
  else `Create` + `SetDefaults()`), and every panel loader (`CheckAndLoadGeneralData`, `CheckAndLoadOwner`, steps,
  schedules, notifications) returns immediately in Create mode. With just the URN the dialog opens, connects,
  resolves the job — and shows a blank New Job sheet, which is indistinguishable from broken reflection. The id is
  preferred over the name because `JobData.Job` resolves it via `Jobs.ItemById` and back-fills both name and URN,
  so a job renamed since the last poll still opens. The URN stays in the document as the no-id fallback
  (`GetSmoObject(urn)`) and for the scripting path.
- **The URN's server name must be `SERVERPROPERTY('ServerName')`**, not the connection string's Data Source.
  Through an AG listener or a CNAME the two differ, and it is the SMO name the URN has to carry.
- Assemblies are resolved from **already-loaded** ones first. A second copy loaded off disk gets a different
  identity, and the `ISqlControlCollection` check then fails on a sheet that is actually fine.

Only integrated and SQL auth can be expressed (Entra/AAD would need an `IRenewableToken` a harvested connection
string cannot supply), so those get a clear "use Object Explorer" message instead.

**Failures have to be legible.** Reflection failures arrive wrapped in `TargetInvocationException`, whose own
message ("Exception has been thrown by the target of an invocation") says nothing — so `JobDialogLauncher.Step`
unwraps each stage and reports the real error plus what was being attempted, and the banner shows the whole
exception chain with type names, full trace on hover. Do **not** route diagnostics through the ActivityLog alone:
VS only writes `ActivityLog.xml` when launched with `/log`, so for a normal SSMS session it is not there when the
failure happens. Error banners are also **sticky** (`ShowNotice(..., sticky: true)`) and the post-action refresh
runs only on success — otherwise the next poll's empty warning text overwrites the message within a second. The
banner carries **Copy** (message + stack trace + server + timestamp) and a **✕** to dismiss; dismissing counts as
acknowledgement and clears the sticky flag, as does pressing Refresh or acting on a job.

The container/sheet chain was verified outside SSMS against a local instance (see the probe approach in this
file's history): connection info → `CDataContainer` → `ObjectUrn` parsing → `JobPropertySheet`'s ctor reaching the
server. Worth repeating that way if it ever breaks — it isolates the failing stage far faster than retrying in SSMS.

When the question is *what the SSMS dialog actually reads* rather than *which stage threw*, decompile it instead of
guessing: `dotnet tool install --tool-path ./tools ilspycmd`, then
`ilspycmd -t Microsoft.SqlServer.Management.SqlManagerUI.JobData "<SSMS>/Release/Common7/IDE/SQLManagerUI.dll"`.
That is how the `<jobid>`/`<job>` requirement above was found; the params a dialog honours are not documented
anywhere else. Note the SSMS 22 filenames are `SQLManagerUI.dll` and `sqlmgmt.dll` — assembly-name resolution is
case-insensitive but the file paths are not.

Unlike the other two dashboards, category and name filtering happen through the grid's `ICollectionView`
rather than the WHERE clause. Job counts are small, so filtering client-side makes the "Show hidden
categories" toggle and every keystroke instant, and lets the status line report how many rows the filter is
holding back — which a server-side filter cannot. Rows still merge in place by `job_id` via `RowMerge`.
