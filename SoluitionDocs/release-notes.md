DataByte for SSMS (unreleased)

New
- IntelliSense: column suggestions while writing an index. In `CREATE [UNIQUE] [CLUSTERED | NONCLUSTERED] [COLUMNSTORE] INDEX ix ON dbo.Orders (` the list offers the columns of dbo.Orders — in the key list, in the `INCLUDE (` list, and in a filtered index's `WHERE` (at its start and after each AND or OR, where a column can go). A column already named in the key or INCLUDE list is not offered again. `CREATE STATISTICS … ON table (` works the same way. `ON` itself now offers table names, including after `ON dbo.`, which used to open an empty column list.
- IntelliSense: query hints. Inside `OPTION (` the list offers the query hints — RECOMPILE, MAXDOP, OPTIMIZE FOR, OPTIMIZE FOR UNKNOWN, USE HINT, MAXRECURSION, FORCE ORDER, the join and union hints and the rest — each with a one-line description, and again after each comma. Inside `USE HINT (` it offers the hint names (DISABLE_PARAMETER_SNIFFING, ENABLE_QUERY_OPTIMIZER_HOTFIXES, the cardinality estimation hints, QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_n and others), inserted with their quotes.
- IntelliSense: table hints. After a table in FROM, JOIN, UPDATE, DELETE, INSERT INTO or MERGE, `WITH (` offers the table hints — NOLOCK, READPAST, UPDLOCK, ROWLOCK, TABLOCK, HOLDLOCK, FORCESEEK, INDEX and the rest. It is not offered after a CTE's WITH or an index's WITH (options), where those hints would not parse.
- IntelliSense: index names in index hints. `WITH (INDEX (` and `WITH (FORCESEEK (` list the indexes of the table being hinted, with each index's type and key columns beside it, primary key and clustered index first. An alias works too: in `UPDATE o WITH (INDEX (` the indexes come from the table `o` stands for.
- IntelliSense: SET options. After `SET` the list offers IDENTITY_INSERT, NOCOUNT, XACT_ABORT, ANSI_NULLS, QUOTED_IDENTIFIER, ARITHABORT, the other session options, STATISTICS IO / TIME / XML / PROFILE and TRANSACTION ISOLATION LEVEL; after `SET TRANSACTION ISOLATION LEVEL` it offers the five isolation levels.

Changed
- SQL Search: a column result now shows the table it belongs to. The name line reads database . schema . table . column, with the table in bold where every other result shows its object name; before, the column appeared twice and the table only in the small text underneath. The type label says COLUMN · TABLE or COLUMN · VIEW, so columns of views stand out from columns of tables.
- Keyword casing while typing now covers keywords containing an underscore or digit — IDENTITY_INSERT, NOCOUNT, XACT_ABORT, SQL_VARIANT, DATETIME2. They had never been recased. STATISTICS IO, ISOLATION LEVEL and the isolation levels are offered in the list but deliberately not recased as you type, because IO, Level, Read and Snapshot are common column names.

Fixed
- Keyword casing while typing no longer capitalises columns named Status: STATUS was being treated as a keyword because it is part of @@FETCH_STATUS.
- IntelliSense marked a column (PK) only if it was ascending in the primary key; a column the key sorts descending lost the flag.
- View Schema: a primary key column with a space in its name and a descending sort (`[Order Date] DESC`) was read as a column called Order, so the CREATE TABLE script treated it as a non-key column.
