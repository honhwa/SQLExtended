DataByte for SSMS 2026.10.23.1755

New
- Tab after SELECT * expands it. With the cursor straight after a `*` or `alias.*` in a SELECT list, press Tab and the star is replaced with the column list, each column prefixed with its table's alias; a table without an alias is given one. The columns are laid out using the formatter's column and comma settings. It needs the schema cache loaded for the database, and `COUNT(*)` is never touched. Switch it off under Settings → IntelliSense. Thanks to @walterstypula for the suggestion (#4).
- A SQLExtended submenu on the query editor's right-click menu, with View Schema, Format SQL, Qualify Columns, Parse Statistics, SQL Search, SQL History, Script Library, Validate Schema References, the schema cache commands and the settings. Parse Statistics is also on the Messages pane's right-click menu.
- The SQLExtended menu now shows each command's keyboard shortcut beside it.

Changed
- Qualify Columns is now Ctrl+K, Ctrl+A (it was Ctrl+K, Ctrl+Q). Ctrl+K, Ctrl+Q now runs Format SQL in a query window, the same chord SSMS's own formatter uses, and Ctrl+K, Ctrl+F still works too.
- Replication Monitor no longer has a shortcut: Ctrl+Alt+R belongs to Copilot. Open it from the SQLExtended menu or a server's right-click menu in Object Explorer.
- Settings → Keyboard Shortcuts now lists every command and reads its shortcut from SSMS, so a shortcut you reassign under Tools → Options → Keyboard shows there. Commands without a shortcut show "—".

Fixed
- Qualify Columns and Go to Column were missing from the SQLExtended menu and could only be reached by their shortcuts.
- In a query window, Ctrl+K, Ctrl+F ran SSMS's Format Selection instead of Format SQL, and Ctrl+Alt+F opened Copilot instead of SQL Search. Both now run SQLExtended's commands in a query window.
- SQLExtended's items on the query editor's right-click menu never appeared.
