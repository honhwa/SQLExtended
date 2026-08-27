# Schema Export

`Export/SchemaExportService.cs` scripts schema out of SQL Server via SMO's `Scripter` in three shapes: a
whole database to one .sql file and a single object to one .sql file (both reached from the Object Explorer
menu), and — `ExportToFolder` — a folder tree of **one file per object, in a subfolder per type**, reached
from the Schema Cache window (database context menu, server context menu, or the Export toolbar button).

The folder export exists to be pointed at a **folder-compare tool** (WinMerge), and that goal, not
runnability, decides every choice in it:

- **Nothing volatile is written.** No export timestamp, no row counts, no manifest file, and
  `IncludeHeaders` is off — SMO's object header carries a script date, which alone would make every file in
  the tree differ on every export. A file differs only when the object's definition differs.
- **The path shape differs by scope on purpose.** A single-database export puts the type folders directly
  under the chosen folder; only a whole-server export inserts a database level. Two exports of the same
  database from two servers therefore line up file-for-file — a server or database name in the path would
  misalign every file and defeat the point.
- **Statement order is left exactly as SMO produced it.** SMO's child collections (indexes, constraints,
  triggers) are keyed and enumerated by name, so the output is already stable across servers. Re-sorting to
  "canonicalise" it would risk splitting a module's leading `SET ANSI_NULLS`/`QUOTED_IDENTIFIER` batches
  from its body for no gain.
- **`NoCollation` is left off** (unlike the single-file `ExportDatabase`, which sets it). SMO then emits
  `COLLATE` only where a column departs from the database default — a real difference worth seeing, rather
  than the noise that scripting every collation would produce.
- Line endings are normalised to CRLF (a definition stored with bare LFs would otherwise read as a
  whole-file difference), and files are UTF-8 **with** a BOM so SSMS doesn't open non-ASCII identifiers as
  ANSI. Both sides of a compare get both, so neither costs diff noise.
- **A re-export offers to delete the previous scripts first.** Writing over the top leaves the scripts of
  dropped objects in place, and the next compare reports them as present on both servers — the diff would be
  wrong in the one direction the tool is trusted for. Deletion only ever touches `.sql` files inside folders
  named in `ExportFileNaming.TypeFolders`, and filters on the extension rather than trusting the `*.sql`
  wildcard (Windows matches three-character extension patterns against longer ones, so the pattern alone
  would also catch a `.sqlproj`).
- `db.PrefetchObjects` + `SetDefaultInitFields(type, true)` per scripted type: without them SMO lazy-loads
  per property and a few hundred objects becomes thousands of round trips.
- Each type group and each object is collected/scripted in its own try/catch, and each database in a server
  export too — one type the edition or permission set disallows costs one folder and a warning, not the
  export. Cancellation is reported on the result rather than thrown, so the caller can still say how much of
  the tree was written; the files on disk are real and the user has to be told about them.

`Export/ExportFileNaming.cs` is deliberately free of SMO and I/O so the test project can link it
(`SQLExtended.Tests/Export/ExportFileNamingTests.cs`). Its job is that no two objects collapse onto one
file name: a case-sensitive collation allows both `[dbo].[Foo]` and `[dbo].[foo]`, names sanitize into each
other (`A/B` and `A\B`), and Windows silently strips trailing dots. Any of those silently drops an object,
and in a folder compare that reads as the object missing from one server — the one failure mode of this
feature nobody would notice.

In the Schema Cache window, **selection lives on the node objects** (`IsSelected`, bound TwoWay from the
item style, the same as `IsExpanded`) rather than on the tree containers. The tree's `ItemsSource` is
replaced wholesale every five seconds, so a container-held selection would not survive the next tick and the
Export button would lose its target. Selection is captured on every rebuild, including the
`capture: false` ones Expand/Collapse all use — that flag is only about expansion state, and letting it skip
selection would re-apply a stale one, aiming Export at whatever was selected two clicks ago. Status messages
that must outlive a rebuild (export outcomes, "no connection available") are written through `SetStatus(...,
sticky: true)`, since `Rebuild` otherwise overwrites the line with its counts within five seconds.
