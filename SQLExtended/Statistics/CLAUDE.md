# Statistics Subsystem

`Statistics/Core/` is **vendored verbatim** from Brent Ozar Unlimited's MIT-licensed
[StatisticsParserExtension](https://github.com/BrentOzarULTD/StatisticsParserExtension) (namespaces left as
`StatisticsParser.Core.*` so upstream syncs stay a file-for-file copy). Don't edit those files — re-copy from upstream
and update `THIRD-PARTY-NOTICES.md`. Both csprojs set `<Nullable>annotations</Nullable>` for them.

`Statistics/Capture/` reads the Messages pane via SSMS's **brokered services** (there is no public API for it):
`SVsBrokeredServiceContainer` → `ISqlEditorServiceBrokered.GetCurrentConnectionAsync` for the editor moniker →
`IQueryEditorTabDataServiceBrokered.GetMessagesTabSegmentAsync` paged in 64 KB segments. Every contract type/method is
resolved by name out of `Microsoft.SqlServer.Management.UI.VSIntegration.SqlEditor.BrokeredContracts.dll`, loaded from
the SSMS install at runtime (never shipped in the VSIX). `ContractTypes.ResolveMethod` prefix-matches parameters so an
SSMS minor version adding optional parameters doesn't break capture. The same interface also exposes
`GetQueryPlanXmlSegmentAsync`, `GetClientStatisticsAsync`, and `GetGridResultsSegmentAsync` if more is ever needed.
