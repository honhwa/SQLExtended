# Object Explorer context menu

`ObjectExplorer/ObjectExplorerMenuService.cs` appends a "SQLExtended" submenu to OE node menus through a runtime
reflection hook (`TreeView.ContextMenuStripChanged`), not the VSCT — SSMS builds those menus dynamically and there
is no stable `IDM_OBJEXPL_*` GUID to place against. Node kind and names come from `INodeInformation.UrnPath` and
`NavigationContext`, parsed in `ObjectExplorerHelper.GetNodeContext`.

For the monitoring dashboards this is the **only launch point that needs no query window**: the node carries its own
connection (`NodeContext.ConnectionString`), so any server connected in Object Explorer can be pinned. All four
commands take `Show(package, connectionString, serverLabel)`. The server node offers all four; the Always On and
replication subtrees offer their own dashboard plus Performance.

**`KindFromUrnPath` matches the Always On and replication subtrees by substring, not by exact path, deliberately.**
Hierarchy nodes have predictable paths (`Server/JobServer/Job`, from the node's `Xpath`), and a folder *with* a
`UniqueName` contributes that name plus "Folder" (`UniqueName=Jobs` → `Server/JobServer/JobsFolder`). But the
folders these two subtrees hang off declare **no `UniqueName` at all** — `<Object name='AvailabilityGroups'
base='Folder'>` in ObjectExplorer.dll's embedded `sqlexplorerhier.xml`, `<Object name='Replication' base='Folder'>`
in its `objectexplorerreplication.xml` — so the segment is whatever the node builder emits at runtime and only a
live SSMS will confirm it. Substring matching also means every node in the subtree (folder, group, publication,
subscription) offers the dashboard, and a renamed folder in a future SSMS does not silently drop the menu. The
substring rules run *after* the exact switch so they can never shadow a more specific kind. Guessing an exact path
here fails silently as "the menu never appears", which is why the guaranteed entry point is the server node.

To read those embedded resources: `Assembly.LoadFrom` the DLL and `GetManifestResourceStream` the `*.xml` names —
no decompiler needed, and it is how the folder question above was settled.
