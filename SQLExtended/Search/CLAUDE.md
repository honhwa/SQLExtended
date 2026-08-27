# SQL Search: Agent job steps

`Search/` searches the schema cache — object names and columns from memory, definitions from the SQLite FTS
index. `Search/JobStepSearchService.cs` adds the one source that is not in the cache at all: SQL Server Agent
job step commands, matched alongside step and job names, behind the **Agent jobs** checkbox in "Search in".

Jobs are a *server* object, and that is what shapes everything here:

- **It runs once per search, not once per selected database.** A step's target database is a column on the
  step (`sysjobsteps.database_name`, and only meaningful for the TSQL subsystem), not the database the job
  lives in — scoping the search to the database selection would drop every non-TSQL step. The type filter
  (Tables/Views/…) does not apply to it either. The checkbox's tooltip says both.
- **It is read live, not cached.** `sysjobsteps` is a few hundred rows behind a server-side LIKE, the search
  is already debounced, and job steps are edited far more casually than modules — a stale command body is
  worth less than the round trip that avoids it.
- **The match is server-side under an explicit `COLLATE Latin1_General_CI_AS`.** On a case-sensitive instance
  msdb would otherwise answer case-sensitively while every other part of the search is `OrdinalIgnoreCase`,
  so the same term would find a procedure and miss the job step that calls it. *Which* field matched is then
  worked out client-side, where the text is in hand and the snippet has to be built anyway — and a row the
  client scan cannot place (CI and OrdinalIgnoreCase disagree on width, kana and a few accent pairs) is kept
  with a generic label rather than dropped. The server is the authority on what matched.
- **The LIKE pattern escapes `[` as well as `%` and `_`.** Searching for a bracketed identifier is a normal
  thing to type here, and unescaped it opens a character class that swallows the rest of the pattern — the
  search then returns nothing, which is indistinguishable from no matches. The escape character is `\`, which
  is not LIKE's default, so the `ESCAPE '\'` clause is load-bearing; `JobStepSearchTests` pins both, along
  with parsing the two batches.
- **A login outside SQLAgentReaderRole silently sees only the jobs it owns**, and an instance with no Agent
  returns nothing at all. Both are probed and reported on the status line, for the reason the Agent jobs
  dashboard does it: a short list that looks complete is the worst outcome available.
- A job whose *name* matches returns all of its steps, deliberately — the step is the addressable thing, each
  row says which field matched, and a job named for what you searched for is the one whose steps you wanted.

Results ride the same list as object results but are **not** `SearchResult`s: `JobStepMatch` is a separate
payload on `SearchResultViewModel` (`IsJobStep`), because a job step has no schema, no database and nothing to
script, and squeezing it into the schema/object fields would put a job name where every consumer expects a
schema. The item template swaps its name line on two Visibility properties rather than a converter. Selecting
one shows the step's command with no round trip — the text arrived with the search — and double-click opens
**SSMS's own Job Properties dialog** via `JobDialogLauncher`, the job equivalent of "Open in Schema Viewer".
That needs `SERVERPROPERTY('ServerName')` rather than the connection string's Data Source (they differ behind
an AG listener or a CNAME), which is why the probe returns it and the control remembers it.
