# IntelliSense

## IntelliSense: bracketing inserted names

`IntelliSense/SqlIdentifierQuoting.cs` decides when a completion inserts `[Ongoing Qty]` rather than
`Ongoing Qty`. Two things make it necessary and **neither reports itself at the point it happens**:

- **A name with a space in it is not a syntax error.** `SELECT t.Ongoing Qty` parses — as the column
  `Ongoing` under the alias `Qty` — so it fails as "invalid column name Ongoing", naming a column nobody
  typed, or on a table that does have an `Ongoing` column it silently returns the wrong one under a
  surprising heading. Warehouse columns like `[Ongoing Qty]` and `[Est Ship Date]` make this the normal case.
- **A reserved word fails in every position, with the parser pointing at the punctuation beside it.**
  Verified against ScriptDom: `SELECT t.Order`, `SELECT Order`, `INSERT INTO t (Order)`, `SET Order = 1` and
  `GROUP BY Order` all fail. Non-reserved keywords (`Value`, `Name`, `Status`, `Type`) are perfectly legal
  column names and are left bare — which is why this consults its own reserved list and **not**
  `SqlKeywords`, whose contents would bracket a large share of ordinary column names.

**The reserved list is verified, not asserted.** `SqlIdentifierQuotingTests` cross-checks it against ScriptDom
in both directions — every word on it must actually be rejected as an identifier, and no word the parser
rejects may be missing (the candidate universe being every single word `SqlKeywords` and
`SqlBuiltInFunctions` know about). A wrong entry either way is invisible at runtime. Seven entries
(SECURITYAUDIT, IDENTITYCOL, DUMP, LOAD, DISK, ROWGUIDCOL, PRECISION) are documented as reserved but
*accepted* by ScriptDom; they are kept and named in the test's allowance list, because bracketing a name that
did not need it costs nothing while preferring the parser to the documentation risks the opposite.

**`QuoteObjectIfNeeded` is a second rule, not a convenience wrapper, because two prefixes carry meaning.**
`@Orders` is a table variable and `[@Orders]` is a table *called* "@Orders" — bracketing changes what the SQL
means, so a name starting with `@` is returned untouched. `#tmp` / `##tmp` are legal bare (brackets round them
also work), and bracketing the most frequently completed name in the list would be a daily irritation for no
gain — so the prefix is set aside and the remainder judged on its own. A temp table with a space in it still
gets brackets, around the whole name including the prefix.

- **Only the inserted text is quoted; the list still displays the bare name** — that is what the user is
  typing to filter on, and what the database-name items already did. The quoted form is appended to
  `filterText` as well, or typing `[` would filter the item out of its own list.
- **Every place a name is written into the editor goes through it.** Columns: the `alias.column` items, the
  plain column items, `*` expansion, the foreign-key JOIN predicates, and both INSERT templates (column list
  and `col = value` assignments). Objects: tables/views, system catalog objects, local temp tables and table
  variables, procedures, user functions and system functions — plus the schema qualifier where one is
  emitted. Missing one leaves the same broken SQL arriving from a different menu. What is deliberately *not*
  quoted is everything that is not a user-supplied name: keywords, built-in function names, collation names,
  snippet names, DBCC commands.
- **`FindApplicableSpan` had to change with it.** The dot-qualified branch (`alias.`, `schema.table`) walked
  back over identifier characters but *not* over `[`, so a user who typed `t.[Ong` — which is how anyone
  reaches a name with a space in it — kept their bracket and got `t.[[Ongoing Qty]`. Brackets are now part
  of the replaced segment, which also fixes the pre-existing `[dbo.MyTable` case.
- **A `]` inside the name is doubled.** Legal in a name, and the one input that would otherwise close the
  bracket early and produce SQL that does not parse at all.
- `IsSimpleIdentifier` is deliberately **stricter than T-SQL's rule** for a regular identifier, which also
  allows `@`, `#` and `$` — a column called `#ET` does parse bare. It is bracketed anyway: over-bracketing
  produces SQL that runs, and the prefixes where bracketing would change *meaning* are handled by
  `QuoteObjectIfNeeded` rather than by loosening this.

Free of the VS editor assemblies so the test project links it (`SQLExtended.Tests/SqlIdentifierQuotingTests.cs`),
the same split `ExportFileNaming` exists for. The behaviour tests **re-parse** `SELECT t.<quoted> FROM dbo.T AS t`
and assert one select element carrying **no alias** — comparing strings cannot tell a bracketed name from a
name plus an alias, which is the entire bug — and check the quoted forms in the four positions a bare reserved
word was shown to fail in.

## IntelliSense: the system catalog

`IntelliSense/` completes from `SchemaCache`, and that cache loads **`is_ms_shipped = 0` only** — so every
system object was absent from it by construction and `sys.` produced an empty list. `Cache/SystemCatalogCache.cs`
holds the other half: the `sys` and `INFORMATION_SCHEMA` surface (catalog views, DMVs, table-valued DMVs,
system functions, and every column on them), read with `is_ms_shipped = 1` as the exact complement so nothing
is loaded twice.

**It is keyed per server, not per database, and that is the whole reason it is a separate class.** Dropping
the `is_ms_shipped` filter in `SchemaCacheLoader` would have been the two-character version of this feature,
and it would then load ~1,100 objects and ~9,000 columns again *for every database on the instance*. The
catalog surface belongs to the engine build, not to the database — every database on an instance exposes the
same catalog views — so it is read once per server and shared. One query, per server, per session.

That has a price worth knowing: the read runs against whatever database the connection is already pointing at,
with no `USE`, so **whichever database the first completion happened in is the one that answers for the server
all session**. Visible only on a contained database or Azure SQL Database, whose surface differs from a box
instance's. A load per database costs far more than that edge is worth.

- **Nothing is persisted to SQLite.** It is one query, and the answer changes only when the instance is patched.
- **Both a load-in-flight and a failed load are memoised, per server.** Completion asks on every keystroke:
  without the first a slow instance stacks a query per character, and without the second a login that cannot
  read the catalog pays the full command timeout per character for an answer that will not change.
  `SchemaCache.ClearAll` clears it — the only way to retry a server that failed earlier in the session, the
  same arrangement `ModuleDecryptionService`'s negative memos have.
- **System objects are only ever offered behind a typed schema qualifier.** Folding them into the bare `FROM`
  list would bury a database's own tables under ~1,100 system objects. The user asks by typing `sys.`.
- **The column fallback in `GetColumnsWithFlags` fires after the schema cache misses, not before it**, so a
  user table in a schema literally named `sys` still wins.
- Types are restricted to V/U/IF/TF/FN. **System stored and extended procedures (P/X) are deliberately not
  loaded** — they belong to the EXEC completion path, which does not read this cache. S (internal base tables,
  DAC-only) is not usefully queryable.

`Cache/SystemCatalogSql.cs` is free of SqlClient so the test project can link it
(`SQLExtended.Tests/Cache/SystemCatalogSqlTests.cs`), the same split `ExportFileNaming` and
`MonitorCollection` exist for — and for the same reason, that every failure here is silent: the cache
swallows the exception and memoises the server as failed, which on screen is indistinguishable from a
permission problem or from the load not having finished. The tests pin that it parses, that it still returns
**exactly two result sets** in the order the reader's single `NextResult` steps through (one statement more or
less and the columns are read as objects — a populated, entirely wrong list rather than an error), and that it
reads `sys.all_objects`/`sys.all_columns` rather than `sys.objects`/`sys.columns`. That last substitution is
the one that leaves the feature parsing, connecting and succeeding while returning nothing.

Parsing cannot tell whether a column exists on a given release. **None of this has been run against a live
instance** — worth doing before trusting the column shapes.
