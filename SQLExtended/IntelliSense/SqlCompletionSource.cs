using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Threading;
using SQLExtended.Cache;
using SQLExtended.Cache.Models;
using SQLExtended.Formatting;
using SQLExtended.Snippets;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Provides table/view completion and column completion from the shared schema cache.
/// Table completion triggers after FROM, JOIN, INTO, UPDATE, DELETE keywords.
/// Column completion triggers after alias/table dot or in SELECT/WHERE/ON contexts.
/// </summary>
internal sealed class SqlCompletionSource : IAsyncCompletionSource
{
    private static readonly ImmutableArray<CompletionFilter> TableFilter =
        ImmutableArray.Create(new CompletionFilter("Tables", "T", new ImageElement(CompletionIcons.Table.ToImageId())));
    private static readonly ImmutableArray<CompletionFilter> ViewFilter =
        ImmutableArray.Create(new CompletionFilter("Views", "V", new ImageElement(CompletionIcons.View.ToImageId())));
    private static readonly ImmutableArray<CompletionFilter> ColumnFilter =
        ImmutableArray.Create(new CompletionFilter("Columns", "C", new ImageElement(CompletionIcons.Column.ToImageId())));
    private static readonly ImmutableArray<CompletionFilter> ProcFilter =
        ImmutableArray.Create(new CompletionFilter("Procedures", "P", new ImageElement(CompletionIcons.StoredProcedure.ToImageId())));
    private static readonly ImmutableArray<CompletionFilter> FunctionFilter =
        ImmutableArray.Create(new CompletionFilter("Functions", "F", new ImageElement(CompletionIcons.ScalarFunction.ToImageId())));
    private static readonly ImmutableArray<CompletionFilter> KeywordFilter =
        ImmutableArray.Create(new CompletionFilter("Keywords", "K", new ImageElement(CompletionIcons.Keyword.ToImageId())));
    private static readonly ImmutableArray<CompletionFilter> SnippetFilter =
        ImmutableArray.Create(new CompletionFilter("Snippets", "S", new ImageElement(CompletionIcons.Snippet.ToImageId())));
    private static readonly ImmutableArray<CompletionFilter> DatabaseFilter =
        ImmutableArray.Create(new CompletionFilter("Databases", "D", new ImageElement(CompletionIcons.Database.ToImageId())));
    private static readonly ImmutableArray<CompletionFilter> SchemaFilter =
        ImmutableArray.Create(new CompletionFilter("Schemas", "H", new ImageElement(CompletionIcons.Schema.ToImageId())));
    private static readonly ImmutableArray<CompletionFilter> EmptyFilters = ImmutableArray<CompletionFilter>.Empty;

    // --- Current-window local tables (#temp, ##global, @tableVar) ---
    // Derived entirely from this window's buffer, memoized on the snapshot version so a
    // burst of keystrokes reuses one parse. Per-view because the source is created per
    // ITextView (see SqlCompletionSourceProvider), giving each window its own local scope.
    private int _localTablesVersion = -1;
    private IReadOnlyList<LocalTableScanner.LocalTable> _localTables;

    // Snapshot text is needed by both InitializeCompletion (UI thread) and
    // GetCompletionContextAsync for the same trigger. GetText() copies the whole buffer —
    // a large allocation that, repeated per keystroke on a big script, drives GC pauses felt
    // as scroll/typing jank. Cache it by snapshot version so one keystroke copies at most once.
    private int _snapshotTextVersion = -1;
    private string _snapshotText;

    private string GetSnapshotText(ITextSnapshot snapshot)
    {
        int version = snapshot.Version.VersionNumber;
        if (_snapshotText != null && _snapshotTextVersion == version)
            return _snapshotText;

        _snapshotText = snapshot.GetText();
        _snapshotTextVersion = version;
        return _snapshotText;
    }

    private IReadOnlyList<LocalTableScanner.LocalTable> GetLocalTables(ITextSnapshot snapshot)
    {
        int version = snapshot.Version.VersionNumber;
        if (_localTables != null && _localTablesVersion == version)
            return _localTables;

        _localTables = LocalTableScanner.Scan(snapshot.GetText());
        _localTablesVersion = version;
        return _localTables;
    }

    /// <summary>
    /// Returns the columns of a local table (with empty PK/FK flags) when <paramref name="tableName"/>
    /// names one in the current window; otherwise null so the caller falls back to the shared cache.
    /// </summary>
    private static IReadOnlyList<(CachedColumn Column, bool IsPrimaryKey, bool IsForeignKey)> TryGetLocalColumns(
        IReadOnlyList<LocalTableScanner.LocalTable> localTables, string tableName)
    {
        var local = LocalTableScanner.Find(localTables, tableName);
        if (local == null)
            return null;
        return local.Columns.Select(c => (c, false, false)).ToList();
    }

    public CompletionStartData InitializeCompletion(
        CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
    {
        DebugLog($"[Source] InitializeCompletion reason={trigger.Reason} char='{trigger.Character}' pos={triggerLocation.Position}");

        // Don't participate if triggered by deletion
        if (trigger.Reason == CompletionTriggerReason.Backspace ||
            trigger.Reason == CompletionTriggerReason.Deletion)
            return default;

        var snapshot = triggerLocation.Snapshot;
        int position = triggerLocation.Position;

        // Get the full text and analyze context
        string fullText = GetSnapshotText(snapshot);
        var analysis = SqlContextAnalyzer.Analyze(fullText, position);

        DebugLog($"[Source] Context type={analysis.Type}");

        switch (analysis.Type)
        {
            case SqlContextAnalyzer.CompletionType.TableName:
            case SqlContextAnalyzer.CompletionType.ColumnAfterDot:
            case SqlContextAnalyzer.CompletionType.ColumnInContext:
            case SqlContextAnalyzer.CompletionType.JoinOnCondition:
            case SqlContextAnalyzer.CompletionType.ProcedureName:
            case SqlContextAnalyzer.CompletionType.InsertColumnTemplate:
            case SqlContextAnalyzer.CompletionType.DatabaseName:
            case SqlContextAnalyzer.CompletionType.CollationName:
                {
                    var span = FindApplicableSpan(triggerLocation, analysis.Type);
                    return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
                }

            case SqlContextAnalyzer.CompletionType.FunctionArgument:
            case SqlContextAnalyzer.CompletionType.DbccCommand:
            case SqlContextAnalyzer.CompletionType.AlterTarget:
            case SqlContextAnalyzer.CompletionType.AlterTableAction:
            case SqlContextAnalyzer.CompletionType.AlterIndexAction:
            case SqlContextAnalyzer.CompletionType.AlterIndexName:
                {
                    // Argument value suggestions (data types, dateparts), DBCC command
                    // names, and ALTER clauses need no DB and should appear immediately —
                    // including right after the triggering '(' or trailing space.
                    var span = FindApplicableSpan(triggerLocation, analysis.Type);
                    return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
                }

            case SqlContextAnalyzer.CompletionType.StarExpansion:
                {
                    // Only on explicit invoke (Ctrl+Space at the star) — popping up while '*'
                    // is typed would risk a later commit char swallowing the star mid-flow.
                    if (trigger.Reason != CompletionTriggerReason.Invoke &&
                        trigger.Reason != CompletionTriggerReason.InvokeAndCommitIfUnique)
                        return default;
                    int starLen = Math.Min(analysis.StarReplaceLength, position);
                    var starSpan = new SnapshotSpan(snapshot, position - starLen, starLen);
                    return new CompletionStartData(CompletionParticipation.ProvidesItems, starSpan);
                }

            case SqlContextAnalyzer.CompletionType.Keyword:
                {
                    // For keyword/snippet completion, only participate if user is typing
                    // an identifier (at least 1 char) or explicitly invoked (Ctrl+Space)
                    var span = FindApplicableSpan(triggerLocation, analysis.Type);
                    bool isExplicitInvoke = trigger.Reason == CompletionTriggerReason.Invoke ||
                                            trigger.Reason == CompletionTriggerReason.InvokeAndCommitIfUnique;
                    if (span.Length > 0 || isExplicitInvoke)
                        return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
                    return default;
                }

            default:
                DebugLog($"[Source] No participation for context {analysis.Type}");
                return default;
        }
    }

    public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
    {
        DebugLog($"[Source] GetCompletionContextAsync reason={trigger.Reason} span=[{applicableToSpan.Start.Position},{applicableToSpan.End.Position}]");
        try
        {
            // Analyze context first — keywords/snippets don't need a DB connection
            string fullText = GetSnapshotText(triggerLocation.Snapshot);
            int position = triggerLocation.Position;
            var analysis = SqlContextAnalyzer.Analyze(fullText, position);
            DebugLog($"[Source] Async context type={analysis.Type}");

            // Keywords and snippets work without a connection
            if (analysis.Type == SqlContextAnalyzer.CompletionType.Keyword)
                return BuildKeywordAndSnippetCompletion(fullText, position);

            // Function argument suggestions (data types, dateparts) need no connection
            if (analysis.Type == SqlContextAnalyzer.CompletionType.FunctionArgument)
                return BuildFunctionArgumentCompletion(analysis.ArgumentKind);

            // DBCC command names need no connection
            if (analysis.Type == SqlContextAnalyzer.CompletionType.DbccCommand)
                return BuildDbccCommandCompletion();

            // ALTER clauses need no connection
            if (analysis.Type == SqlContextAnalyzer.CompletionType.AlterTarget)
                return BuildAlterClauseCompletion(SqlAlterCommands.Targets, "ALTER target");
            if (analysis.Type == SqlContextAnalyzer.CompletionType.AlterTableAction)
                return BuildAlterClauseCompletion(SqlAlterCommands.TableActions, "ALTER TABLE action");
            if (analysis.Type == SqlContextAnalyzer.CompletionType.AlterIndexAction)
                return BuildAlterClauseCompletion(SqlAlterCommands.IndexActions, "ALTER INDEX action");
            if (analysis.Type == SqlContextAnalyzer.CompletionType.AlterIndexName)
                return BuildAlterClauseCompletion(SqlAlterCommands.IndexNameHints, "index");

            // All other completions need a connection + cache
            string connectionString = null;
            string currentDb = null;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            connectionString = ConnectionHelper.GetActiveConnectionString();
            currentDb = ConnectionHelper.GetCurrentDatabaseName();
            // Back to a background thread for the rest. The local-table scan (a full ScriptDom
            // parse of the whole buffer) and the database-list query below must NOT run on the UI
            // thread — on a large script the parse alone freezes SSMS. (Task.Yield would have kept
            // the continuation on the UI thread; awaiting the default scheduler leaves it.)
            await TaskScheduler.Default;

            // Database list (USE …) needs only a connection, not a loaded schema cache.
            if (analysis.Type == SqlContextAnalyzer.CompletionType.DatabaseName)
            {
                if (string.IsNullOrEmpty(connectionString))
                    return CompletionContext.Empty;
                return BuildDatabaseCompletion(connectionString);
            }

            // Collation names (COLLATE …) also need only a connection.
            if (analysis.Type == SqlContextAnalyzer.CompletionType.CollationName)
            {
                if (string.IsNullOrEmpty(connectionString))
                    return CompletionContext.Empty;
                return BuildCollationCompletion(connectionString, currentDb);
            }

            if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(currentDb))
                return CompletionContext.Empty;

            var cache = SchemaCache.Instance;
            string connKey = cache.GetConnectionKey(connectionString);
            var state = cache.GetState(connKey, currentDb);

            // Warm the server's system catalog on any completion rather than waiting for the first
            // "sys." — it is one query per server per session, and starting it here means the list
            // is already in memory the first time someone types the dot instead of the first
            // attempt coming back empty.
            SystemCatalogCache.Instance.EnsureLoaded(connectionString, connKey);

            // Trigger cache load if not loaded
            if (state == CacheState.NotLoaded)
            {
                _ = cache.LoadDatabaseAsync(connectionString, currentDb);
                return CompletionContext.Empty;
            }

            var localTables = GetLocalTables(triggerLocation.Snapshot);

            switch (analysis.Type)
            {
                case SqlContextAnalyzer.CompletionType.TableName:
                    return BuildTableCompletion(cache, connectionString, connKey, currentDb, triggerLocation, localTables);

                case SqlContextAnalyzer.CompletionType.ColumnAfterDot:
                    return BuildColumnCompletionForDot(cache, connectionString, connKey, currentDb, analysis, localTables);

                case SqlContextAnalyzer.CompletionType.ColumnInContext:
                    return BuildColumnCompletionForContext(cache, connectionString, connKey, currentDb, analysis, localTables);

                case SqlContextAnalyzer.CompletionType.JoinOnCondition:
                    return BuildJoinConditionCompletion(cache, connectionString, connKey, currentDb, analysis, localTables);

                case SqlContextAnalyzer.CompletionType.ProcedureName:
                    return BuildProcedureCompletion(cache, connKey, currentDb, triggerLocation);

                case SqlContextAnalyzer.CompletionType.InsertColumnTemplate:
                    return BuildInsertTemplateCompletion(cache, connectionString, connKey, currentDb, analysis);

                case SqlContextAnalyzer.CompletionType.StarExpansion:
                    return BuildStarExpansionCompletion(cache, connectionString, connKey, currentDb, analysis, localTables);

                default:
                    return CompletionContext.Empty;
            }
        }
        catch (OperationCanceledException)
        {
            return CompletionContext.Empty;
        }
        catch
        {
            // Never crash SSMS
            return CompletionContext.Empty;
        }
    }

    public Task<object> GetDescriptionAsync(
        IAsyncCompletionSession session, CompletionItem item, CancellationToken token)
    {
        // Show the carried description for ALTER clause items
        if (item.Properties.TryGetProperty<string>(ClauseDescriptionKey, out var clauseDesc) &&
            !string.IsNullOrEmpty(clauseDesc))
            return Task.FromResult<object>(clauseDesc);

        // Show signature + description for built-in function items
        if (item.Properties.TryGetProperty<string>(BuiltInFunctionKey, out var fnName))
        {
            var fn = SqlBuiltInFunctions.Find(fnName);
            if (fn != null)
                return Task.FromResult<object>($"{fn.Signature}\n{fn.Category} function, returns {fn.ReturnType}.\n\n{fn.Description}");
        }

        // Show syntax + description for DBCC command items
        if (item.Properties.TryGetProperty<string>(DbccCommandKey, out var dbccName))
        {
            var cmd = SqlDbccCommands.Find(dbccName);
            if (cmd != null)
            {
                string syntax = string.IsNullOrEmpty(cmd.Syntax)
                    ? $"DBCC {cmd.Name}"
                    : $"DBCC {cmd.Name} {cmd.Syntax}";
                return Task.FromResult<object>($"{syntax}\n{cmd.Category} command.\n\n{cmd.Description}");
            }
        }

        // Show snippet description and resolved body preview for snippet items
        if (item.Filters.Length > 0 && item.Filters[0].DisplayText == "Snippets")
        {
            var snippet = SnippetManager.Instance.FindByCode(item.DisplayText);
            if (snippet != null)
            {
                string desc = !string.IsNullOrEmpty(snippet.Description)
                    ? snippet.Description + "\n\n"
                    : "";

                // For tooltip, fully resolve all placeholders (system + defaults for custom)
                string preview = SnippetPlaceholderResolver.Resolve(snippet.Body, snippet.Defaults);
                return Task.FromResult<object>($"{desc}{preview}");
            }
        }

        return Task.FromResult<object>(item.DisplayText);
    }

    // --- Table completion (Phase 2 logic) ---

    private CompletionContext BuildTableCompletion(
        ISchemaCache cache, string connectionString, string connKey, string currentDb, SnapshotPoint triggerLocation,
        IReadOnlyList<LocalTableScanner.LocalTable> localTables)
    {
        // Determine the already-typed qualifier before the cursor:
        //   []                  \u2192 current-db tables/views + server databases
        //   [X]                 \u2192 schema X tables (current db) and/or, if X is a database, its schemas
        //   [Db, Schema]        \u2192 tables/views in Db.Schema
        int pos = triggerLocation.Position;
        int lookBack = Math.Min(pos, 500);
        string textBefore = triggerLocation.Snapshot.GetText(pos - lookBack, lookBack);
        var parts = SqlCompletionContext.GetQualifierParts(textBefore);

        var items = new List<CompletionItem>();

        if (parts.Count == 0)
        {
            // Bare object position: this window's local temp tables/variables first (they're
            // never schema-qualified), then current-database tables/views with schema in the
            // inserted text, then the databases available on the server.
            AppendLocalTableItems(items, localTables);
            AppendObjectItems(items, cache, connKey, currentDb, schemaFilter: null, qualifyWithSchema: true);
            AppendDatabaseItems(items, connectionString, currentDb);
        }
        else if (parts.Count == 1)
        {
            string qualifier = parts[0];

            // Interpretation A: a schema in the current database \u2192 its tables/views.
            AppendObjectItems(items, cache, connKey, currentDb, schemaFilter: qualifier, qualifyWithSchema: false);

            // Interpretation A': "sys." / "INFORMATION_SCHEMA." \u2014 the catalog views and DMVs, which
            // the schema cache does not hold (it loads is_ms_shipped = 0 only).
            AppendSystemObjectItems(items, connectionString, connKey, qualifier);

            // Interpretation B: a database on the server \u2192 its schemas (three-part naming).
            if (IsKnownDatabase(connectionString, qualifier))
                AppendSchemaItems(items, cache, connectionString, connKey, qualifier);
        }
        else
        {
            // database.schema. \u2192 tables/views in that database+schema.
            string db = parts[0];
            string schema = parts[1];
            if (IsKnownDatabase(connectionString, db))
            {
                AppendObjectItems(items, cache, connKey, db, schemaFilter: schema, qualifyWithSchema: false,
                    ensureConnectionString: connectionString);

                // "MyDb.sys." resolves to the same catalog: it is a property of the instance, and the
                // cache is keyed per server for that reason. No per-database load is needed here.
                AppendSystemObjectItems(items, connectionString, connKey, schema);
            }
        }

        return items.Count > 0
            ? new CompletionContext(items.ToImmutableArray())
            : CompletionContext.Empty;
    }

    /// <summary>
    /// Appends table/view completion items for a database. When <paramref name="qualifyWithSchema"/>
    /// the inserted text is "schema.object" (no qualifier typed yet); otherwise just the object name
    /// (a schema/database qualifier was already typed and stays put). If the target database is not
    /// yet cached and <paramref name="ensureConnectionString"/> is supplied, a background load is
    /// kicked off and no items are added this pass.
    /// </summary>
    private void AppendObjectItems(
        List<CompletionItem> items, ISchemaCache cache, string connKey, string database,
        string schemaFilter, bool qualifyWithSchema, string ensureConnectionString = null)
    {
        if (ensureConnectionString != null && !EnsureDatabaseLoaded(cache, ensureConnectionString, connKey, database))
            return;

        var objects = cache.GetObjects(connKey, database);
        if (objects == null || objects.Count == 0)
            return;

        foreach (var obj in objects)
        {
            string type = obj.ObjectType?.Trim();
            if (type != "U" && type != "V")
                continue;

            if (schemaFilter != null &&
                !string.Equals(obj.SchemaName, schemaFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            string quotedName = SqlIdentifierQuoting.QuoteObjectIfNeeded(obj.ObjectName);
            string insertText = qualifyWithSchema
                ? $"{SqlIdentifierQuoting.QuoteIfNeeded(obj.SchemaName)}.{quotedName}"
                : quotedName;
            string displayText = $"{obj.SchemaName}.{obj.ObjectName}";

            string suffix = type == "U"
                ? $"Table \u00B7 {obj.RowCount:N0} rows"
                : "View";

            var icon = new ImageElement(CompletionIcons.ForObjectType(type).ToImageId());
            var filters = type == "U" ? TableFilter : ViewFilter;

            items.Add(new CompletionItem(
                displayText: displayText,
                source: this,
                icon: icon,
                filters: filters,
                suffix: suffix,
                insertText: insertText,
                sortText: displayText,
                // The quoted form is in the filter text too, so a user who starts with "[" � how you reach
                // a name with a space in it � still matches the item.
                filterText: quotedName == obj.ObjectName
                    ? $"{obj.SchemaName}.{obj.ObjectName} {obj.ObjectName}"
                    : $"{obj.SchemaName}.{obj.ObjectName} {obj.ObjectName} {quotedName}",
                attributeIcons: ImmutableArray<ImageElement>.Empty));
        }
    }

    /// <summary>
    /// Appends the catalog views, DMVs and table-valued DMVs of <c>sys</c> / <c>INFORMATION_SCHEMA</c>
    /// when that is the schema the user typed. These live in <see cref="SystemCatalogCache"/> rather
    /// than the schema cache — see that class for why they are held per server rather than per
    /// database — so this is a second pass over a different store, not a filter change on the first.
    ///
    /// Only ever called with a schema qualifier already typed. Folding these into the bare object
    /// list would bury a database's own tables under ~1,100 system objects, which is the opposite of
    /// useful; the user asks for them by typing "sys.".
    /// </summary>
    private void AppendSystemObjectItems(List<CompletionItem> items, string connectionString, string connKey, string schema)
    {
        if (!SystemCatalogCache.IsSystemSchema(schema))
            return;
        if (!SystemCatalogCache.Instance.EnsureLoaded(connectionString, connKey))
            return;

        foreach (var obj in SystemCatalogCache.Instance.GetObjects(connKey))
        {
            if (!string.Equals(obj.SchemaName, schema, StringComparison.OrdinalIgnoreCase))
                continue;

            string type = obj.ObjectType;

            // Scalar functions have no place in a FROM clause; they are offered by
            // BuildFunctionCompletion for the "SELECT sys.…" position instead.
            if (type == "FN")
                continue;

            string typeLabel = type switch
            {
                "U" => "System table",
                "V" => obj.ObjectName.StartsWith("dm_", StringComparison.OrdinalIgnoreCase)
                    ? "Dynamic management view"
                    : "System view",
                "IF" or "TF" => "Table-valued function",
                _ => "System object"
            };

            var filters = type == "U" ? TableFilter
                        : type == "V" ? ViewFilter
                        : FunctionFilter;

            string displayText = $"{obj.SchemaName}.{obj.ObjectName}";

            items.Add(new CompletionItem(
                displayText: displayText,
                source: this,
                icon: new ImageElement(CompletionIcons.ForObjectType(type).ToImageId()),
                filters: filters,
                suffix: typeLabel,
                // The schema is already typed and stays put, so only the object name is inserted.
                insertText: SqlIdentifierQuoting.QuoteObjectIfNeeded(obj.ObjectName),
                sortText: displayText,
                filterText: $"{obj.SchemaName}.{obj.ObjectName} {obj.ObjectName}",
                attributeIcons: ImmutableArray<ImageElement>.Empty));
        }
    }

    /// <summary>
    /// Appends one item per local table defined in the current window (#temp, ##global, @tableVar).
    /// These sort ahead of everything else (a leading space sortText) since the user just declared
    /// them and is most likely to want them.
    /// </summary>
    private void AppendLocalTableItems(List<CompletionItem> items, IReadOnlyList<LocalTableScanner.LocalTable> localTables)
    {
        if (localTables == null || localTables.Count == 0)
            return;

        var icon = new ImageElement(CompletionIcons.Table.ToImageId());
        foreach (var lt in localTables)
        {
            string kind = lt.IsTableVariable ? "Table variable"
                        : lt.IsGlobal ? "Global temp table"
                        : "Local temp table";
            string suffix = lt.Columns.Count > 0 ? $"{kind} · {lt.Columns.Count} cols" : kind;

            items.Add(new CompletionItem(
                displayText: lt.Name,
                source: this,
                icon: icon,
                filters: TableFilter,
                suffix: suffix,
                insertText: SqlIdentifierQuoting.QuoteObjectIfNeeded(lt.Name),
                sortText: $" 0_{lt.Name}",   // leading space sorts ahead of all alnum sortTexts
                filterText: SqlIdentifierQuoting.QuoteObjectIfNeeded(lt.Name) == lt.Name
                    ? lt.Name
                    : $"{lt.Name} {SqlIdentifierQuoting.QuoteObjectIfNeeded(lt.Name)}",
                attributeIcons: ImmutableArray<ImageElement>.Empty));
        }
    }

    /// <summary>
    /// Appends one item per database on the server. Picking one inserts the database name;
    /// the user then types '.' to drill into its schemas (three-part naming). Databases sort
    /// after tables/views (they share the bare FROM list).
    /// </summary>
    private void AppendDatabaseItems(List<CompletionItem> items, string connectionString, string currentDb)
    {
        if (string.IsNullOrEmpty(connectionString))
            return;

        var names = GetDatabaseNames(connectionString);
        if (names == null || names.Count == 0)
            return;

        var icon = new ImageElement(CompletionIcons.Database.ToImageId());
        foreach (var name in names)
        {
            string insertText = SqlIdentifierQuoting.QuoteIfNeeded(name);
            string suffix = string.Equals(name, currentDb, StringComparison.OrdinalIgnoreCase)
                ? "Database (current)"
                : "Database";

            items.Add(new CompletionItem(
                displayText: name,
                source: this,
                icon: icon,
                filters: DatabaseFilter,
                suffix: suffix,
                insertText: insertText,
                sortText: $"~db_{name}",   // '~' sorts databases after the schema-qualified objects
                filterText: name,
                attributeIcons: ImmutableArray<ImageElement>.Empty));
        }
    }

    /// <summary>
    /// Appends one item per schema in the given database. Picking one inserts the schema name;
    /// the user types '.' to reach its tables. Loads the database in the background if needed.
    /// </summary>
    private void AppendSchemaItems(
        List<CompletionItem> items, ISchemaCache cache, string connectionString, string connKey, string database)
    {
        if (!EnsureDatabaseLoaded(cache, connectionString, connKey, database))
            return;

        var objects = cache.GetObjects(connKey, database);
        if (objects == null || objects.Count == 0)
            return;

        var schemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in objects)
            if (!string.IsNullOrEmpty(obj.SchemaName))
                schemas.Add(obj.SchemaName);

        // sys and INFORMATION_SCHEMA exist in every database but own no cached object, so they would
        // never appear from the loop above — and "MyDb." offering every schema except the two the
        // user is most likely to want next reads as them not existing.
        foreach (var systemSchema in SystemCatalogCache.SystemSchemas)
            schemas.Add(systemSchema);

        var icon = new ImageElement(CompletionIcons.Schema.ToImageId());
        foreach (var schema in schemas)
        {
            string insertText = SqlIdentifierQuoting.QuoteIfNeeded(schema);
            items.Add(new CompletionItem(
                displayText: schema,
                source: this,
                icon: icon,
                filters: SchemaFilter,
                suffix: $"Schema \u00B7 {database}",
                insertText: insertText,
                sortText: $"schema_{schema}",
                filterText: schema,
                attributeIcons: ImmutableArray<ImageElement>.Empty));
        }
    }

    /// <summary>
    /// Ensures the given database's schema is cached. Returns true if it is ready; otherwise
    /// kicks off a background load (when not already loading) and returns false so the caller
    /// adds no items this pass \u2014 they appear once the user re-triggers after the load completes.
    /// </summary>
    private static bool EnsureDatabaseLoaded(ISchemaCache cache, string connectionString, string connKey, string database)
    {
        var state = cache.GetState(connKey, database);
        if (state == CacheState.Ready || state == CacheState.Stale)
            return true;
        if (state == CacheState.NotLoaded)
            _ = cache.LoadDatabaseAsync(connectionString, database);
        return false;
    }

    /// <summary>Case-insensitive check that <paramref name="name"/> is a database on the server.</summary>
    private static bool IsKnownDatabase(string connectionString, string name)
    {
        if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(name))
            return false;
        var names = GetDatabaseNames(connectionString);
        return names != null && names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    // --- Database completion (USE …) ---

    /// <summary>
    /// Cached database lists per connection key, with a short TTL. The set of
    /// databases on a server changes rarely; refetching on every keystroke would
    /// stall IntelliSense.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime FetchedAt, List<string> Names)>
        _databaseListCache = new();

    private static readonly TimeSpan DatabaseListTtl = TimeSpan.FromSeconds(60);

    private CompletionContext BuildDatabaseCompletion(string connectionString)
    {
        var names = GetDatabaseNames(connectionString);
        if (names == null || names.Count == 0)
            return CompletionContext.Empty;

        var icon = new ImageElement(CompletionIcons.Database.ToImageId());
        var items = new List<CompletionItem>(names.Count);

        foreach (var name in names)
        {
            // Always insert the database name bracketed so it survives reserved words
            // and names containing spaces or hyphens (e.g. "USE [My DB]").
            string insertText = $"[{name}]";

            items.Add(new CompletionItem(
                displayText: name,
                source: this,
                icon: icon,
                filters: EmptyFilters,
                suffix: "Database",
                insertText: insertText,
                sortText: name,
                filterText: name,
                attributeIcons: ImmutableArray<ImageElement>.Empty));
        }

        return new CompletionContext(items.ToImmutableArray());
    }

    private static List<string> GetDatabaseNames(string connectionString)
    {
        string key = SchemaCache.Instance.GetConnectionKey(connectionString);

        if (_databaseListCache.TryGetValue(key, out var entry) &&
            DateTime.UtcNow - entry.FetchedAt < DatabaseListTtl)
        {
            return entry.Names;
        }

        var names = new List<string>();
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "master",
                ConnectTimeout = 5
            };
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT name FROM sys.databases
                                WHERE state_desc = 'ONLINE'
                                  AND HAS_DBACCESS(name) = 1
                                ORDER BY name";
            cmd.CommandTimeout = 5;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                names.Add(reader.GetString(0));
        }
        catch (Exception ex)
        {
            // Reaching master is its own connect and its own permission, separate from every other read the
            // extension makes - on Azure SQL Database a contained user cannot get there at all. The fallback
            // means completion still offers something, which is exactly why the failure has to be recorded.
            Diagnostics.SQLExtendedLog.Warning("Completion", $"Could not list databases on {key} (connecting to master)", ex);

            // Fall back to whatever the schema cache already knows.
            var cached = SchemaCache.Instance.GetDatabases(key);
            if (cached != null)
                foreach (var d in cached)
                    names.Add(d.Name);
        }

        _databaseListCache[key] = (DateTime.UtcNow, names);
        return names;
    }

    // --- Collation completion (COLLATE …) ---

    /// <summary>
    /// Server collation list per connection key. The set of collations a server supports is
    /// fixed for its version, so it's fetched once and kept for the session (~5,500 rows).
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<(string Name, string Description)>>
        _collationListCache = new();

    /// <summary>Default collation per "connKey|database", with a short TTL (it can be ALTERed, rarely).</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime FetchedAt, string Name)>
        _defaultCollationCache = new();

    private static readonly TimeSpan DefaultCollationTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Builds the COLLATE argument list: the current database's default collation first (so it's
    /// the preselected suggestion), then the DATABASE_DEFAULT keyword, then every collation the
    /// server supports in alphabetical order.
    /// </summary>
    private CompletionContext BuildCollationCompletion(string connectionString, string currentDb)
    {
        var collations = GetCollations(connectionString);
        if (collations == null || collations.Count == 0)
            return CompletionContext.Empty;

        string defaultCollation = GetDefaultCollation(connectionString, currentDb);

        var icon = new ImageElement(CompletionIcons.Collation.ToImageId());
        var items = new List<CompletionItem>(collations.Count + 2);

        // Database default first — a leading-space sortText sorts (and thus soft-selects) it
        // ahead of everything else when nothing has been typed yet.
        if (!string.IsNullOrEmpty(defaultCollation))
        {
            string desc = collations.FirstOrDefault(c => string.Equals(c.Name, defaultCollation, StringComparison.OrdinalIgnoreCase)).Description;
            items.Add(new CompletionItem(
                displayText: defaultCollation,
                source: this,
                icon: icon,
                filters: KeywordFilter,
                suffix: string.IsNullOrEmpty(desc) ? "Database default" : $"Database default · {desc}",
                insertText: defaultCollation,
                sortText: " 0_default",
                filterText: defaultCollation,
                attributeIcons: ImmutableArray<ImageElement>.Empty));
        }

        // DATABASE_DEFAULT keyword — collate to whatever the current database uses (tempdb-safe).
        string dbDefaultKw = CaseKeyword("DATABASE_DEFAULT");
        items.Add(new CompletionItem(
            displayText: dbDefaultKw,
            source: this,
            icon: icon,
            filters: KeywordFilter,
            suffix: "Use the collation of the current database",
            insertText: dbDefaultKw,
            sortText: " 1_database_default",
            filterText: "DATABASE_DEFAULT",
            attributeIcons: ImmutableArray<ImageElement>.Empty));

        foreach (var (name, description) in collations)
        {
            if (string.Equals(name, defaultCollation, StringComparison.OrdinalIgnoreCase))
                continue; // already listed at the top

            items.Add(new CompletionItem(
                displayText: name,
                source: this,
                icon: icon,
                filters: KeywordFilter,
                suffix: description ?? "Collation",
                insertText: name,
                sortText: name,
                filterText: name,
                attributeIcons: ImmutableArray<ImageElement>.Empty));
        }

        return new CompletionContext(items.ToImmutableArray());
    }

    private static List<(string Name, string Description)> GetCollations(string connectionString)
    {
        string key = SchemaCache.Instance.GetConnectionKey(connectionString);
        if (_collationListCache.TryGetValue(key, out var cached))
            return cached;

        var collations = new List<(string, string)>();
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 5 };
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, description FROM sys.fn_helpcollations() ORDER BY name";
            cmd.CommandTimeout = 10;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                collations.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }
        catch
        {
            return null; // don't cache a failed fetch — retry on the next trigger
        }

        _collationListCache[key] = collations;
        return collations;
    }

    private static string GetDefaultCollation(string connectionString, string database)
    {
        if (string.IsNullOrEmpty(database))
            return null;

        string key = SchemaCache.Instance.GetConnectionKey(connectionString) + "|" + database;
        if (_defaultCollationCache.TryGetValue(key, out var entry) &&
            DateTime.UtcNow - entry.FetchedAt < DefaultCollationTtl)
        {
            return entry.Name;
        }

        string name = null;
        try
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 5 };
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(builder.ConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CONVERT(sysname, DATABASEPROPERTYEX(@db, 'Collation'))";
            cmd.Parameters.AddWithValue("@db", database);
            cmd.CommandTimeout = 5;
            name = cmd.ExecuteScalar() as string;
        }
        catch
        {
            return null; // don't cache a failed fetch
        }

        _defaultCollationCache[key] = (DateTime.UtcNow, name);
        return name;
    }

    // --- Stored procedure completion (Phase 4 logic) ---

    private CompletionContext BuildProcedureCompletion(
        ISchemaCache cache, string connKey, string currentDb, SnapshotPoint triggerLocation)
    {
        var objects = cache.GetObjects(connKey, currentDb);
        if (objects == null || objects.Count == 0)
            return CompletionContext.Empty;

        // Check for schema prefix (e.g., "dbo.")
        int pos = triggerLocation.Position;
        int lookBack = Math.Min(pos, 500);
        string textBefore = triggerLocation.Snapshot.GetText(pos - lookBack, lookBack);
        string schemaPrefix = SqlCompletionContext.GetSchemaPrefix(textBefore);

        var items = new List<CompletionItem>();

        foreach (var obj in objects)
        {
            string type = obj.ObjectType?.Trim();
            if (type != "P")
                continue;

            if (schemaPrefix != null &&
                !string.Equals(obj.SchemaName, schemaPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string insertText = schemaPrefix != null
                ? SqlIdentifierQuoting.QuoteObjectIfNeeded(obj.ObjectName)
                : $"{SqlIdentifierQuoting.QuoteIfNeeded(obj.SchemaName)}.{SqlIdentifierQuoting.QuoteObjectIfNeeded(obj.ObjectName)}";

            string displayText = $"{obj.SchemaName}.{obj.ObjectName}";

            // Build parameter summary for suffix
            var parameters = cache.GetParameters(connKey, currentDb, obj.SchemaName, obj.ObjectName);
            string suffix = BuildParameterSummary(parameters);

            var icon = new ImageElement(CompletionIcons.StoredProcedure.ToImageId());

            var item = new CompletionItem(
                displayText: displayText,
                source: this,
                icon: icon,
                filters: ProcFilter,
                suffix: suffix,
                insertText: insertText,
                sortText: displayText,
                filterText: $"{obj.SchemaName}.{obj.ObjectName} {obj.ObjectName}",
                attributeIcons: ImmutableArray<ImageElement>.Empty);

            // When enabled, accepting the item expands to the full "@param = value" call as an
            // interactive tab-stop snippet (see ProcParameterExpansion / the commit manager). Tag it
            // as a snippet so Space never triggers the expansion — only Tab/Enter do.
            if (Settings.SQLExtendedSettings.Current.ExpandProcedureParameters && parameters != null && parameters.Count > 0)
            {
                item.Properties.AddProperty(ProcParameterExpansion.InfoKey,
                    new ProcParameterExpansion.Info(insertText, parameters));
                item.Properties.AddProperty(SqlCompletionCommitManager.IsSnippetKey, true);
            }

            items.Add(item);
        }

        return new CompletionContext(items.ToImmutableArray());
    }

    // --- Function completion (Phase 4 logic) ---

    /// <summary>
    /// Builds completion items for functions in a given schema.
    /// Called when the dot prefix is not found as an alias (likely a schema name).
    /// </summary>
    private CompletionContext BuildFunctionCompletion(
        ISchemaCache cache, string connKey, string currentDb, string schemaPrefix)
    {
        var items = new List<CompletionItem>();

        // "SELECT sys.fn_…" — the system functions and table-valued DMVs, which the schema cache
        // does not hold. Appended before the early return below so this still works in a database
        // whose own object list is empty.
        AppendSystemFunctionItems(items, connKey, schemaPrefix);

        var objects = cache.GetObjects(connKey, currentDb);
        if (objects == null || objects.Count == 0)
            return items.Count > 0 ? new CompletionContext(items.ToImmutableArray()) : CompletionContext.Empty;

        foreach (var obj in objects)
        {
            string type = obj.ObjectType?.Trim();
            if (type != "FN" && type != "IF" && type != "TF")
                continue;

            if (schemaPrefix != null &&
                !string.Equals(obj.SchemaName, schemaPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Schema is already typed before the dot, so just insert the name
            string insertText = SqlIdentifierQuoting.QuoteObjectIfNeeded(obj.ObjectName);
            string displayText = $"{obj.SchemaName}.{obj.ObjectName}";

            string typeLabel = type switch
            {
                "FN" => "Scalar Function",
                "IF" => "Inline Table Function",
                "TF" => "Table Function",
                _ => "Function"
            };

            var parameters = cache.GetParameters(connKey, currentDb, obj.SchemaName, obj.ObjectName);
            string paramSummary = BuildParameterSummary(parameters);
            string suffix = string.IsNullOrEmpty(paramSummary)
                ? typeLabel
                : $"{typeLabel} \u00B7 {paramSummary}";

            var icon = new ImageElement(CompletionIcons.ForObjectType(type).ToImageId());

            var item = new CompletionItem(
                displayText: displayText,
                source: this,
                icon: icon,
                filters: FunctionFilter,
                suffix: suffix,
                insertText: insertText,
                sortText: displayText,
                filterText: $"{obj.SchemaName}.{obj.ObjectName} {obj.ObjectName}",
                attributeIcons: ImmutableArray<ImageElement>.Empty);

            items.Add(item);
        }

        return new CompletionContext(items.ToImmutableArray());
    }

    /// <summary>
    /// Builds a short parameter summary like "(3 params)" or "(@CustomerID int, @Name nvarchar)".
    /// </summary>
    private static string BuildParameterSummary(IReadOnlyList<CachedParameter> parameters)
    {
        if (parameters == null || parameters.Count == 0)
            return "(no params)";

        if (parameters.Count > 3)
            return $"({parameters.Count} params)";

        var parts = new List<string>();
        foreach (var p in parameters)
        {
            string name = p.ParameterName;
            string type = p.DataType ?? "unknown";
            parts.Add($"{name} {type}");
        }
        return $"({string.Join(", ", parts)})";
    }

    /// <summary>
    /// Appends the scalar and table-valued functions of <c>sys</c> for the expression position
    /// ("SELECT sys.fn_…", "CROSS APPLY sys.dm_exec_sql_text(…)"). No connection string is taken:
    /// the catalog is either already loaded or the caller gets nothing this pass — every path that
    /// reaches here has already been through <see cref="AppendSystemObjectItems"/>'s warm-up or the
    /// one in the completion entry point.
    /// </summary>
    private void AppendSystemFunctionItems(List<CompletionItem> items, string connKey, string schemaPrefix)
    {
        if (!SystemCatalogCache.IsSystemSchema(schemaPrefix))
            return;

        foreach (var obj in SystemCatalogCache.Instance.GetObjects(connKey))
        {
            string type = obj.ObjectType;
            if (type != "FN" && type != "IF" && type != "TF")
                continue;
            if (!string.Equals(obj.SchemaName, schemaPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string typeLabel = type == "FN" ? "System scalar function" : "System table-valued function";
            string displayText = $"{obj.SchemaName}.{obj.ObjectName}";

            items.Add(new CompletionItem(
                displayText: displayText,
                source: this,
                icon: new ImageElement(CompletionIcons.ForObjectType(type).ToImageId()),
                filters: FunctionFilter,
                suffix: typeLabel,
                insertText: SqlIdentifierQuoting.QuoteObjectIfNeeded(obj.ObjectName),
                sortText: displayText,
                filterText: $"{obj.SchemaName}.{obj.ObjectName} {obj.ObjectName}",
                attributeIcons: ImmutableArray<ImageElement>.Empty));
        }
    }

    // --- Column completion after dot (alias. or table.) ---

    private CompletionContext BuildColumnCompletionForDot(
        ISchemaCache cache, string connectionString, string connKey, string currentDb, SqlContextAnalyzer.AnalysisResult analysis,
        IReadOnlyList<LocalTableScanner.LocalTable> localTables)
    {
        // A local temp table / table variable referenced directly by name ("#tmp.").
        if (LocalTableScanner.IsLocalName(analysis.DotPrefix))
        {
            var localCols = TryGetLocalColumns(localTables, analysis.DotPrefix);
            if (localCols != null)
                return BuildColumnItemsFrom(localCols);
        }

        // Resolve the dot prefix to a table
        var tables = AliasResolver.Resolve(analysis.StatementText);
        var tableRef = AliasResolver.FindByIdentifier(tables, analysis.DotPrefix);

        if (tableRef != null)
        {
            string database = tableRef.Database ?? currentDb;
            string schema = tableRef.Schema ?? "dbo";
            string tableName = tableRef.Table;
            var columns = GetColumnsWithFlags(cache, connectionString, connKey, database, schema, tableName, localTables);
            return BuildColumnItemsFrom(columns);
        }

        // Prefix is not an alias — it might be a schema name.
        // Show functions from that schema (e.g., "SELECT dbo.fn_GetName")
        return BuildFunctionCompletion(cache, connKey, currentDb, analysis.DotPrefix);
    }

    // --- Column completion in context (SELECT, WHERE, etc.) ---

    private CompletionContext BuildColumnCompletionForContext(
        ISchemaCache cache, string connectionString, string connKey, string currentDb, SqlContextAnalyzer.AnalysisResult analysis,
        IReadOnlyList<LocalTableScanner.LocalTable> localTables)
    {
        // Get all tables referenced in the current statement
        var tables = AliasResolver.Resolve(analysis.StatementText);
        if (tables.Count == 0)
            return CompletionContext.Empty;

        var items = new List<CompletionItem>();
        AppendColumnItems(items, cache, connectionString, connKey, currentDb, tables, localTables);
        return new CompletionContext(items.ToImmutableArray());
    }

    /// <summary>
    /// Appends one completion item per column across all the given tables. When more than
    /// one table is in scope the columns are prefixed with the alias/table reference so the
    /// produced text is unambiguous (e.g. "o.CustomerID").
    /// </summary>
    private void AppendColumnItems(
        List<CompletionItem> items, ISchemaCache cache, string connectionString, string connKey, string currentDb,
        IReadOnlyList<AliasResolver.TableReference> tables,
        IReadOnlyList<LocalTableScanner.LocalTable> localTables)
    {
        bool multipleTables = tables.Count > 1;

        foreach (var tableRef in tables)
        {
            string database = tableRef.Database ?? currentDb;
            string schema = tableRef.Schema ?? "dbo";
            string tableName = tableRef.Table;
            string prefix = multipleTables ? tableRef.ReferenceName : null;

            var columns = GetColumnsWithFlags(cache, connectionString, connKey, database, schema, tableName, localTables);
            foreach (var (col, isPk, isFk) in columns)
            {
                string quoted = SqlIdentifierQuoting.QuoteIfNeeded(col.ColumnName);
                string insertText = prefix != null ? $"{prefix}.{quoted}" : quoted;
                string displayText = prefix != null ? $"{prefix}.{col.ColumnName}" : col.ColumnName;
                string suffix = BuildColumnSuffix(col, isPk, isFk);

                var icon = new ImageElement(
                    CompletionIcons.ForColumn(isPk, isFk, col.IsIdentity, col.IsComputed).ToImageId());

                var item = new CompletionItem(
                    displayText: displayText,
                    source: this,
                    icon: icon,
                    filters: ColumnFilter,
                    suffix: suffix,
                    insertText: insertText,
                    sortText: prefix != null ? $"{prefix}.{col.Ordinal:D4}" : $"{col.Ordinal:D4}",
                    // The quoted form is in the filter text too, so a user who starts with "[" — which is
                    // how you reach a column with a space in its name — still matches the item.
                    filterText: quoted == col.ColumnName ? $"{col.ColumnName} {displayText}" : $"{col.ColumnName} {displayText} {quoted}",
                    attributeIcons: ImmutableArray<ImageElement>.Empty);

                items.Add(item);
            }
        }
    }

    // --- JOIN ... ON condition completion (foreign-key-aware) ---

    private static readonly ImmutableArray<CompletionFilter> JoinFilter =
        ImmutableArray.Create(new CompletionFilter("Joins", "J", new ImageElement(CompletionIcons.ForeignKey.ToImageId())));

    /// <summary>
    /// Builds completion for a JOIN's ON clause. Foreign-key relationships between the
    /// just-joined table and the other tables already in scope are offered first as
    /// complete predicates (e.g. "o.CustomerID = c.CustomerID"), followed by the plain
    /// column list so the user can still author the condition by hand.
    /// </summary>
    private CompletionContext BuildJoinConditionCompletion(
        ISchemaCache cache, string connectionString, string connKey, string currentDb, SqlContextAnalyzer.AnalysisResult analysis,
        IReadOnlyList<LocalTableScanner.LocalTable> localTables)
    {
        var tables = AliasResolver.Resolve(analysis.StatementText);
        if (tables.Count == 0)
            return CompletionContext.Empty;

        // Identify the just-joined (right) table; fall back to the last one parsed.
        var rightTable = AliasResolver.FindByIdentifier(tables, analysis.JoinedTableReference)
                         ?? tables[tables.Count - 1];

        var items = new List<CompletionItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int order = 0;

        foreach (var leftTable in tables)
        {
            if (ReferenceEquals(leftTable, rightTable))
                continue;

            foreach (var predicate in BuildForeignKeyPredicates(cache, connKey, currentDb, rightTable, leftTable))
            {
                if (!seen.Add(predicate))
                    continue;

                var icon = new ImageElement(CompletionIcons.ForeignKey.ToImageId());
                items.Add(new CompletionItem(
                    displayText: predicate,
                    source: this,
                    icon: icon,
                    filters: JoinFilter,
                    suffix: "Foreign key",
                    insertText: predicate,
                    sortText: $"!0_{order++:D3}",   // '!' sorts ahead of the alnum column sortTexts
                    filterText: predicate,
                    attributeIcons: ImmutableArray<ImageElement>.Empty));
            }
        }

        // Always also offer plain columns so the user can build the condition manually.
        AppendColumnItems(items, cache, connectionString, connKey, currentDb, tables, localTables);

        return items.Count > 0
            ? new CompletionContext(items.ToImmutableArray())
            : CompletionContext.Empty;
    }

    /// <summary>
    /// Produces join predicates from the foreign keys declared between two tables, in both
    /// directions. Each predicate pairs the FK columns with the referenced columns, joined
    /// by AND for composite keys, e.g. "o.CustomerID = c.CustomerID".
    /// </summary>
    private static IEnumerable<string> BuildForeignKeyPredicates(
        ISchemaCache cache, string connKey, string currentDb,
        AliasResolver.TableReference right, AliasResolver.TableReference left)
    {
        var results = new List<string>();

        // FKs declared on the right table that reference the left table.
        foreach (var fk in cache.GetForeignKeys(connKey, right.Database ?? currentDb, right.Schema ?? "dbo", right.Table))
        {
            if (ReferencesTable(fk, left))
                results.Add(FormatPredicate(right.ReferenceName, fk.Columns, left.ReferenceName, fk.ReferencedColumns));
        }

        // FKs declared on the left table that reference the right table.
        foreach (var fk in cache.GetForeignKeys(connKey, left.Database ?? currentDb, left.Schema ?? "dbo", left.Table))
        {
            if (ReferencesTable(fk, right))
                results.Add(FormatPredicate(left.ReferenceName, fk.Columns, right.ReferenceName, fk.ReferencedColumns));
        }

        return results;
    }

    private static bool ReferencesTable(CachedForeignKey fk, AliasResolver.TableReference table)
    {
        if (!string.Equals(fk.ReferencedTable, table.Table, StringComparison.OrdinalIgnoreCase))
            return false;

        // Only enforce schema equality when both sides specify one (the parsed reference
        // often omits it, in which case the table-name match is sufficient).
        if (!string.IsNullOrEmpty(table.Schema) && !string.IsNullOrEmpty(fk.ReferencedSchema))
            return string.Equals(fk.ReferencedSchema, table.Schema, StringComparison.OrdinalIgnoreCase);

        return true;
    }

    /// <summary>
    /// Formats a join predicate from comma-separated column lists, pairing them positionally
    /// and combining composite keys with AND.
    /// </summary>
    private static string FormatPredicate(string leftRef, string leftCols, string rightRef, string rightCols)
    {
        var lc = (leftCols ?? string.Empty).Split(',');
        var rc = (rightCols ?? string.Empty).Split(',');
        int n = Math.Min(lc.Length, rc.Length);

        var parts = new List<string>(n);
        for (int i = 0; i < n; i++)
            parts.Add($"{leftRef}.{SqlIdentifierQuoting.QuoteIfNeeded(lc[i].Trim())} = {rightRef}.{SqlIdentifierQuoting.QuoteIfNeeded(rc[i].Trim())}");

        return string.Join(" AND ", parts);
    }

    // --- INSERT all-columns template ---

    private static readonly ImmutableArray<CompletionFilter> SnippetFilterArr =
        ImmutableArray.Create(new CompletionFilter("Snippets", "S", new ImageElement(CompletionIcons.Snippet.ToImageId())));

    /// <summary>
    /// Builds two completion items offering an "all columns" INSERT template
    /// when the cursor sits after "INSERT INTO [schema.]table ". Identity and
    /// computed columns are skipped. The default style (from FormatterOptions)
    /// is preselected; the other is offered as an alternative.
    /// </summary>
    private CompletionContext BuildInsertTemplateCompletion(
        ISchemaCache cache, string connectionString, string connKey, string currentDb, SqlContextAnalyzer.AnalysisResult analysis)
    {
        string table = analysis.TargetTable;
        if (string.IsNullOrEmpty(table))
            return CompletionContext.Empty;

        string database = analysis.TargetDatabase ?? currentDb;
        if (connectionString != null)
            EnsureDatabaseLoaded(cache, connectionString, connKey, database);

        // Resolve schema: if not given, find it in the cache (default to dbo).
        string schema = analysis.TargetSchema;
        if (string.IsNullOrEmpty(schema))
        {
            var objects = cache.GetObjects(connKey, database);
            var match = objects?.FirstOrDefault(o =>
                string.Equals(o.ObjectName, table, StringComparison.OrdinalIgnoreCase) &&
                (o.ObjectType?.Trim() == "U" || o.ObjectType?.Trim() == "V"));
            schema = match?.SchemaName ?? "dbo";
        }

        var columns = cache.GetColumns(connKey, database, schema, table);
        if (columns == null || columns.Count == 0)
            return CompletionContext.Empty;

        // Filter out identity and computed columns — they cannot be inserted.
        var insertable = columns.Where(c => !c.IsIdentity && !c.IsComputed).ToList();
        if (insertable.Count == 0)
            return CompletionContext.Empty;

        FormatterOptions opts;
        try { opts = FormatterOptions.Load(); }
        catch { opts = new FormatterOptions(); }

        bool selectIsDefault = opts.InsertTemplateDefaultStyle == InsertTemplateStyleOption.SelectAssign;

        string valuesBody = BuildInsertTemplateBody(insertable, opts, useSelectAssign: false);
        string selectBody = BuildInsertTemplateBody(insertable, opts, useSelectAssign: true);

        // Lower sortText wins; default style sorts first.
        var valuesItem = MakeItem(
            display: "(all columns) VALUES",
            suffix: $"INSERT template for {schema}.{table} · {insertable.Count} columns",
            body: valuesBody,
            sortText: selectIsDefault ? "~1_insert_values" : "~0_insert_values",
            filterText: "all columns insert template values");

        var selectItem = MakeItem(
            display: "(all columns) SELECT col = val",
            suffix: $"INSERT … SELECT template for {schema}.{table} · {insertable.Count} columns",
            body: selectBody,
            sortText: selectIsDefault ? "~0_insert_select" : "~1_insert_select",
            filterText: "all columns insert template select assign");

        return new CompletionContext(ImmutableArray.Create(valuesItem, selectItem));
    }

    // --- SELECT * expansion ---

    /// <summary>
    /// Builds items that expand a SELECT-list "*" (or "alias.*") into the explicit column
    /// list of the table(s) in scope. The applicable span covers the star token, so a
    /// commit replaces it. With a bare "*" over multiple tables, every table's columns are
    /// alias-prefixed; per-table items are also offered so one source can be picked.
    /// </summary>
    private CompletionContext BuildStarExpansionCompletion(
        ISchemaCache cache, string connectionString, string connKey, string currentDb, SqlContextAnalyzer.AnalysisResult analysis,
        IReadOnlyList<LocalTableScanner.LocalTable> localTables)
    {
        var tables = AliasResolver.Resolve(analysis.StatementText);
        if (tables.Count == 0)
            return CompletionContext.Empty;

        var items = new List<CompletionItem>();

        if (!string.IsNullOrEmpty(analysis.DotPrefix))
        {
            // alias.* — expand only that table's columns, keeping the alias prefix.
            var tableRef = AliasResolver.FindByIdentifier(tables, analysis.DotPrefix);
            if (tableRef == null && LocalTableScanner.IsLocalName(analysis.DotPrefix))
                tableRef = new AliasResolver.TableReference { Table = analysis.DotPrefix };
            if (tableRef != null)
                AddStarExpansionItem(items, cache, connectionString, connKey, currentDb, new[] { tableRef }, localTables,
                    prefixOverride: analysis.DotPrefix, sortText: "!*0");
        }
        else
        {
            AddStarExpansionItem(items, cache, connectionString, connKey, currentDb, tables, localTables,
                prefixOverride: null, sortText: "!*0");

            if (tables.Count > 1)
            {
                int order = 1;
                foreach (var t in tables)
                    AddStarExpansionItem(items, cache, connectionString, connKey, currentDb, new[] { t }, localTables,
                        prefixOverride: t.ReferenceName, sortText: $"!*{order++}");
            }
        }

        return items.Count > 0
            ? new CompletionContext(items.ToImmutableArray())
            : CompletionContext.Empty;
    }

    /// <summary>
    /// Appends one expansion item covering the given tables. Columns are prefixed with
    /// <paramref name="prefixOverride"/> when set (the alias.* case), or with each table's
    /// reference name when more than one table contributes columns.
    /// </summary>
    private void AddStarExpansionItem(
        List<CompletionItem> items, ISchemaCache cache, string connectionString, string connKey, string currentDb,
        IReadOnlyList<AliasResolver.TableReference> tables,
        IReadOnlyList<LocalTableScanner.LocalTable> localTables, string prefixOverride, string sortText)
    {
        bool prefixEach = prefixOverride != null || tables.Count > 1;
        var parts = new List<string>();
        foreach (var t in tables)
        {
            string prefix = prefixOverride ?? (prefixEach ? t.ReferenceName : null);
            foreach (var (col, _, _) in GetColumnsWithFlags(cache, connectionString, connKey, t.Database ?? currentDb, t.Schema ?? "dbo", t.Table, localTables))
            {
                string quoted = SqlIdentifierQuoting.QuoteIfNeeded(col.ColumnName);
                parts.Add(prefix != null ? $"{prefix}.{quoted}" : quoted);
            }
        }

        if (parts.Count == 0)
            return;

        string expansion = string.Join(", ", parts);
        string label = prefixOverride != null ? $"{prefixOverride}.*" : "*";
        string preview = expansion.Length > 80 ? expansion.Substring(0, 77) + "…" : expansion;

        var item = new CompletionItem(
            displayText: $"{label} → {preview}",
            source: this,
            icon: new ImageElement(CompletionIcons.Column.ToImageId()),
            filters: ColumnFilter,
            suffix: $"Expand to {parts.Count} columns",
            insertText: expansion,
            sortText: sortText,
            filterText: label,
            attributeIcons: ImmutableArray<ImageElement>.Empty);
        // Tab/Enter only — Space must type through and never swallow the star.
        item.Properties.AddProperty(SqlCompletionCommitManager.IsSnippetKey, true);
        items.Add(item);
    }

    private CompletionItem MakeItem(string display, string suffix, string body, string sortText, string filterText)
    {
        var icon = new ImageElement(CompletionIcons.Snippet.ToImageId());
        var item = new CompletionItem(
            displayText: display,
            source: this,
            icon: icon,
            filters: SnippetFilterArr,
            suffix: suffix,
            insertText: body,
            sortText: sortText,
            filterText: filterText,
            attributeIcons: ImmutableArray<ImageElement>.Empty);
        item.Properties.AddProperty(SqlCompletionCommitManager.IsSnippetKey, true);
        return item;
    }

    /// <summary>
    /// Builds the INSERT template body. Two flavors:
    ///   VALUES form:        (cols) VALUES (vals)
    ///   SELECT-assign form: (cols) SELECT col1 = val1, col2 = val2
    /// Respects keyword casing, indent style/size, comma position, columns/values-per-line,
    /// and InsertParenthesesOnSameLine from FormatterOptions.
    /// </summary>
    private static string BuildInsertTemplateBody(
        IReadOnlyList<CachedColumn> columns, FormatterOptions opts, bool useSelectAssign)
    {
        string indent = opts.IndentStyle == IndentStyleOption.Tabs
            ? "\t"
            : new string(' ', Math.Max(1, opts.IndentSize));
        bool leadingComma = opts.CommaPosition == CommaPositionOption.LeadingComma;
        bool parensSameLine = opts.InsertParenthesesOnSameLine;

        string valuesKw = ApplyCase("VALUES", opts.KeywordCase);
        string selectKw = ApplyCase("SELECT", opts.KeywordCase);

        int colsPerLine = Math.Max(1, opts.InsertColumnsPerLine);
        int valsPerLine = Math.Max(1, opts.InsertValuesPerLine);

        var sb = new System.Text.StringBuilder();

        // Column list — same for both forms.
        AppendList(sb, columns.Select(c => SqlIdentifierQuoting.QuoteIfNeeded(c.ColumnName)).ToList(),
            indent, leadingComma, colsPerLine, parensSameLine);

        if (useSelectAssign)
        {
            sb.AppendLine();
            sb.AppendLine(selectKw);
            // assignments: col = value
            var assigns = new List<string>(columns.Count);
            foreach (var c in columns)
                assigns.Add($"{SqlIdentifierQuoting.QuoteIfNeeded(c.ColumnName)} = {DefaultValueFor(c)}");
            AppendStackedItems(sb, assigns, indent, leadingComma, valsPerLine);
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine(valuesKw);
            var values = columns.Select(DefaultValueFor).ToList();
            AppendList(sb, values, indent, leadingComma, valsPerLine, parensSameLine);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends a parenthesized comma-separated list (used for column list and VALUES block).
    /// Wraps every <paramref name="perLine"/> items.
    /// </summary>
    private static void AppendList(
        System.Text.StringBuilder sb, IReadOnlyList<string> items,
        string indent, bool leadingComma, int perLine, bool parensSameLine)
    {
        if (parensSameLine)
        {
            sb.Append('(');
            AppendItems(sb, items, indent, leadingComma, perLine, firstLineHasOpenParen: true);
            sb.Append(')');
        }
        else
        {
            sb.AppendLine("(");
            AppendItems(sb, items, indent, leadingComma, perLine, firstLineHasOpenParen: false);
            sb.AppendLine();
            sb.Append(')');
        }
    }

    private static void AppendItems(
        System.Text.StringBuilder sb, IReadOnlyList<string> items,
        string indent, bool leadingComma, int perLine, bool firstLineHasOpenParen)
    {
        for (int i = 0; i < items.Count; i++)
        {
            bool isFirstOnLine = (i % perLine) == 0;
            bool isLastItem = i == items.Count - 1;

            if (isFirstOnLine)
            {
                if (i > 0 || !firstLineHasOpenParen)
                {
                    if (i > 0) sb.AppendLine();
                    sb.Append(indent);
                }
                else
                {
                    // first item on the same line as '(' — no indent
                }
            }

            if (leadingComma && i > 0 && isFirstOnLine)
                sb.Append(", ");

            sb.Append(items[i]);

            if (!isLastItem && !leadingComma)
                sb.Append(", ");
            else if (!isLastItem && leadingComma && !isFirstOnLineNext(i, perLine))
                sb.Append(", ");
        }
    }

    private static bool isFirstOnLineNext(int i, int perLine) => ((i + 1) % perLine) == 0;

    /// <summary>
    /// Appends a stacked list (one assignment per "slot") without parentheses, used for
    /// the SELECT-assign form. Indented by <paramref name="indent"/>.
    /// </summary>
    private static void AppendStackedItems(
        System.Text.StringBuilder sb, IReadOnlyList<string> items,
        string indent, bool leadingComma, int perLine)
    {
        for (int i = 0; i < items.Count; i++)
        {
            bool isFirstOnLine = (i % perLine) == 0;
            bool isLastItem = i == items.Count - 1;

            if (isFirstOnLine)
            {
                if (i > 0) sb.AppendLine();
                sb.Append(indent);
            }

            if (leadingComma && i > 0 && isFirstOnLine)
                sb.Append(", ");

            sb.Append(items[i]);

            if (!isLastItem && !leadingComma)
                sb.Append(", ");
            else if (!isLastItem && leadingComma && ((i + 1) % perLine) != 0)
                sb.Append(", ");
        }
    }

    private static string ApplyCase(string keyword, CasingOption casing)
    {
        return casing switch
        {
            CasingOption.Upper => keyword.ToUpperInvariant(),
            CasingOption.Lower => keyword.ToLowerInvariant(),
            _ => keyword
        };
    }

    /// <summary>
    /// Active keyword/identifier casing from the formatter's current profile. Read per completion
    /// build so casing changes apply without an SSMS restart; falls back to Unchanged on any error.
    /// </summary>
    private static (CasingOption Keyword, CasingOption Identifier) GetActiveCasing()
    {
        try { return FormatterProfileManager.Instance.GetActiveCasing(); }
        catch { return (CasingOption.Unchanged, CasingOption.Unchanged); }
    }

    /// <summary>Applies the formatter's keyword casing to a completion's SQL keyword text.</summary>
    private static string CaseKeyword(string text) => ApplyCase(text, GetActiveCasing().Keyword);

    /// <summary>
    /// Returns a sensible placeholder literal for the given column's type.
    /// Nullable columns without a known default get NULL; otherwise we pick a
    /// type-appropriate zero/empty literal.
    /// </summary>
    private static string DefaultValueFor(CachedColumn col)
        => DefaultValueForType(col.DataType, col.IsNullable);

    /// <summary>
    /// Returns a sensible placeholder literal for a SQL data type (used for both INSERT-template
    /// values and stored-procedure parameter expansion). Known types get a type-appropriate
    /// zero/empty literal; anything else falls back to NULL.
    /// </summary>
    internal static string DefaultValueForType(string dataType, bool isNullable)
    {
        string type = (dataType ?? string.Empty).Trim().ToLowerInvariant();

        switch (type)
        {
            case "bit":
                return "0";
            case "tinyint":
            case "smallint":
            case "int":
            case "bigint":
            case "decimal":
            case "numeric":
            case "money":
            case "smallmoney":
            case "float":
            case "real":
                return "0";
            case "char":
            case "varchar":
            case "text":
                return "''";
            case "nchar":
            case "nvarchar":
            case "ntext":
                return "N''";
            case "date":
            case "datetime":
            case "datetime2":
            case "smalldatetime":
                return "GETDATE()";
            case "datetimeoffset":
                return "SYSDATETIMEOFFSET()";
            case "time":
                return "CAST(GETDATE() AS time)";
            case "uniqueidentifier":
                return "NEWID()";
            case "binary":
            case "varbinary":
            case "image":
                return "0x";
            case "xml":
                return "''";
            default:
                return "NULL";
        }
    }

    // --- Column item building helpers ---

    /// <summary>Builds a single-table column completion list (no alias prefixing) from resolved columns.</summary>
    private CompletionContext BuildColumnItemsFrom(
        IReadOnlyList<(CachedColumn Column, bool IsPrimaryKey, bool IsForeignKey)> columns)
    {
        if (columns == null || columns.Count == 0)
            return CompletionContext.Empty;

        var items = new List<CompletionItem>();

        foreach (var (col, isPk, isFk) in columns)
        {
            string displayText = col.ColumnName;
            string insertText = SqlIdentifierQuoting.QuoteIfNeeded(col.ColumnName);
            string suffix = BuildColumnSuffix(col, isPk, isFk);

            var icon = new ImageElement(
                CompletionIcons.ForColumn(isPk, isFk, col.IsIdentity, col.IsComputed).ToImageId());

            var item = new CompletionItem(
                displayText: displayText,
                source: this,
                icon: icon,
                filters: ColumnFilter,
                suffix: suffix,
                insertText: insertText,
                sortText: $"{col.Ordinal:D4}",
                filterText: insertText == col.ColumnName ? col.ColumnName : $"{col.ColumnName} {insertText}",
                attributeIcons: ImmutableArray<ImageElement>.Empty);

            items.Add(item);
        }

        return new CompletionContext(items.ToImmutableArray());
    }

    /// <summary>
    /// Gets columns for a table along with PK/FK flags from the cache. <paramref name="database"/>
    /// is the table's resolved database — the current one, or the qualifier of a cross-database
    /// reference (whose cache load is kicked off in the background if not yet available).
    /// </summary>
    private IReadOnlyList<(CachedColumn Column, bool IsPrimaryKey, bool IsForeignKey)> GetColumnsWithFlags(
        ISchemaCache cache, string connectionString, string connKey, string database, string schema, string tableName,
        IReadOnlyList<LocalTableScanner.LocalTable> localTables = null)
    {
        // Local temp tables / table variables come from the current window, not the shared cache.
        if (LocalTableScanner.IsLocalName(tableName))
        {
            var local = TryGetLocalColumns(localTables, tableName);
            if (local != null)
                return local;
        }

        // Kick off a background load for a not-yet-cached database (cross-database references);
        // whatever is already in memory still serves this pass.
        if (connectionString != null)
            EnsureDatabaseLoaded(cache, connectionString, connKey, database);

        var columns = cache.GetColumns(connKey, database, schema, tableName);

        // "FROM sys.dm_exec_requests r WHERE r." — a system object resolves to nothing in the schema
        // cache, so fall back to the server's catalog. Checked after the miss rather than before it,
        // so a user table in a schema literally named "sys" still wins. System objects have no
        // primary or foreign keys to report, hence the flags below are all false.
        if ((columns == null || columns.Count == 0) && SystemCatalogCache.IsSystemSchema(schema))
        {
            var systemColumns = SystemCatalogCache.Instance.GetColumns(connKey, schema, tableName);
            return systemColumns.Select(c => (Column: c, IsPrimaryKey: false, IsForeignKey: false)).ToList();
        }

        if (columns == null || columns.Count == 0)
            return Array.Empty<(CachedColumn, bool, bool)>();

        // Build PK column set from indexes
        var pkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indexes = cache.GetIndexes(connKey, database, schema, tableName);
        foreach (var idx in indexes)
        {
            if (idx.IsPrimaryKey && !string.IsNullOrEmpty(idx.KeyColumns))
            {
                foreach (var col in idx.KeyColumns.Split(','))
                    pkColumns.Add(col.Trim());
            }
        }

        // Build FK column set from foreign keys
        var fkColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var foreignKeys = cache.GetForeignKeys(connKey, database, schema, tableName);
        foreach (var fk in foreignKeys)
        {
            if (!string.IsNullOrEmpty(fk.Columns))
            {
                foreach (var col in fk.Columns.Split(','))
                    fkColumns.Add(col.Trim());
            }
        }

        return columns.Select(c => (
            Column: c,
            IsPrimaryKey: pkColumns.Contains(c.ColumnName),
            IsForeignKey: fkColumns.Contains(c.ColumnName)
        )).ToList();
    }

    /// <summary>
    /// Builds the suffix text shown to the right of a column completion item.
    /// Example: "int NOT NULL (PK)" or "nvarchar(50) NULL"
    /// </summary>
    private static string BuildColumnSuffix(CachedColumn col, bool isPk, bool isFk)
    {
        string typeStr = col.DataType ?? "unknown";

        // Add flags
        var flags = new List<string>();
        if (isPk) flags.Add("PK");
        if (isFk) flags.Add("FK");
        if (col.IsIdentity) flags.Add("Identity");

        string nullability = col.IsNullable ? "NULL" : "NOT NULL";
        string flagStr = flags.Count > 0 ? $" ({string.Join(", ", flags)})" : "";

        // Include default if present
        string defaultStr = !string.IsNullOrEmpty(col.DefaultDefinition)
            ? $" = {col.DefaultDefinition}"
            : "";

        return $"{typeStr} {nullability}{flagStr}{defaultStr}";
    }

    /// <summary>
    /// Finds the span of the identifier being typed at the trigger location.
    /// For dot-triggered completion, only includes the part after the dot.
    /// </summary>
    private static SnapshotSpan FindApplicableSpan(
        SnapshotPoint triggerLocation, SqlContextAnalyzer.CompletionType contextType)
    {
        var snapshot = triggerLocation.Snapshot;
        int end = triggerLocation.Position;
        int start = end;

        // For dot-qualified contexts only the segment after the final dot is replaced;
        // the already-typed qualifier (alias., schema., database.schema.) stays put.
        // This covers column-after-dot and the (possibly multi-part) table/procedure name.
        if (contextType == SqlContextAnalyzer.CompletionType.ColumnAfterDot ||
            contextType == SqlContextAnalyzer.CompletionType.TableName ||
            contextType == SqlContextAnalyzer.CompletionType.ProcedureName)
        {
            while (start > 0)
            {
                char c = snapshot[start - 1];
                if (c == '.')
                    break; // stop at the dot
                // Brackets are part of the segment being replaced. Typing the opening bracket is how anyone
                // reaches a column with a space in its name, and since the inserted text brings its own
                // brackets, leaving a typed "[" outside the span produces "t.[[Ongoing Qty]".
                if (char.IsLetterOrDigit(c) || c == '_' || c == '#' || c == '@' || c == '[' || c == ']')
                    start--;
                else
                    break;
            }
        }
        else
        {
            // Walk backward over identifier chars (letters, digits, _, ., [, ], #, @)
            while (start > 0)
            {
                char c = snapshot[start - 1];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '[' || c == ']' || c == '#' || c == '@')
                    start--;
                else
                    break;
            }
        }

        return new SnapshotSpan(snapshot, start, end - start);
    }

    // --- Keyword and snippet completion (Phase 5) ---

    private CompletionContext BuildKeywordAndSnippetCompletion(string fullText, int position)
    {
        // Determine which text is before cursor for context detection
        int lookBack = Math.Min(position, 500);
        string textBeforeCursor = fullText.Substring(position - lookBack, lookBack);

        var items = new List<CompletionItem>();

        // Add context-aware keywords
        var context = SqlKeywords.DetectContext(textBeforeCursor);
        var keywords = SqlKeywords.GetKeywordsForContext(context);
        var keywordIcon = new ImageElement(CompletionIcons.Keyword.ToImageId());

        foreach (var kw in keywords)
        {
            string text = CaseKeyword(kw.Text);
            var item = new CompletionItem(
                displayText: text,
                source: this,
                icon: keywordIcon,
                filters: KeywordFilter,
                suffix: "Keyword",
                insertText: text,
                sortText: $"~1_{kw.Text}",  // Sort after schema objects but before snippets
                filterText: kw.Text,
                attributeIcons: ImmutableArray<ImageElement>.Empty);

            items.Add(item);
        }

        // Add built-in T-SQL functions (GETDATE, STRING_SPLIT, DATEADD, …) in any
        // expression-capable context. They intermix alphabetically with keywords
        // and surface their signature/description in the tooltip.
        if ((context & FunctionCompletionContexts) != 0)
            AddBuiltInFunctionItems(items);

        // Add snippets (resolve $placeholder$ tokens at insertion time)
        var snippetIcon = new ImageElement(CompletionIcons.Snippet.ToImageId());
        var snippets = SnippetManager.Instance.Snippets;

        DebugLog($"[Snippets] Building {snippets.Count} snippets");

        foreach (var snippet in snippets)
        {
            var customPlaceholders = SnippetPlaceholderResolver.GetCustomPlaceholderNames(snippet.Body);
            bool hasCursor = snippet.Body != null &&
                snippet.Body.IndexOf("$cursor$", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasExpansion = customPlaceholders.Count > 0 || hasCursor;

            if (hasExpansion)
                DebugLog($"[Snippets] '{snippet.Code}' has {customPlaceholders.Count} custom placeholders{(hasCursor ? " + $cursor$" : "")}: {string.Join(", ", customPlaceholders)}");

            // For expansion snippets, resolve only system placeholders (custom ones become tab stops).
            // For plain snippets, resolve everything.
            var resolvedBody = hasExpansion
                ? SnippetPlaceholderResolver.ResolveSystemOnly(snippet.Body)
                : SnippetPlaceholderResolver.Resolve(snippet.Body);

            var item = new CompletionItem(
                displayText: snippet.Code,
                source: this,
                icon: snippetIcon,
                filters: SnippetFilter,
                suffix: snippet.Title,
                insertText: resolvedBody,
                sortText: $"~0_{snippet.Code}",  // Sort snippets first (they match short prefixes)
                filterText: snippet.Code,
                attributeIcons: ImmutableArray<ImageElement>.Empty);

            // Tag all snippet items so space never commits them
            item.Properties.AddProperty(SqlCompletionCommitManager.IsSnippetKey, true);

            // Tag expansion snippets so the commit manager can start a tab-stop session
            if (hasExpansion)
                item.Properties.AddProperty(SqlCompletionCommitManager.SnippetExpansionKey, true);

            items.Add(item);
        }

        return items.Count > 0
            ? new CompletionContext(items.ToImmutableArray())
            : CompletionContext.Empty;
    }

    // --- Built-in T-SQL function completion ---

    /// <summary>Property key tagging a completion item as a built-in function (value = function name).</summary>
    internal const string BuiltInFunctionKey = "BuiltInFunction";

    /// <summary>
    /// Contexts where built-in functions are syntactically reasonable. Excludes
    /// pure statement-start / DDL positions where a function call makes no sense.
    /// </summary>
    private const KeywordContext FunctionCompletionContexts =
        KeywordContext.AfterSelect | KeywordContext.AfterWhere | KeywordContext.AfterGroupBy |
        KeywordContext.AfterOrderBy | KeywordContext.Expression | KeywordContext.AfterSet |
        KeywordContext.Block | KeywordContext.AfterJoin;

    private static readonly ImageElement ScalarFunctionIcon = new(CompletionIcons.ScalarFunction.ToImageId());
    private static readonly ImageElement TableFunctionIcon = new(CompletionIcons.TableFunction.ToImageId());

    private void AddBuiltInFunctionItems(List<CompletionItem> items)
    {
        foreach (var fn in SqlBuiltInFunctions.All)
        {
            bool isTableFn = string.Equals(fn.ReturnType, "table", StringComparison.OrdinalIgnoreCase);
            var icon = isTableFn ? TableFunctionIcon : ScalarFunctionIcon;

            // Suffix shows the parameter list and return type, e.g. "(datepart, number, date) : date".
            string paramPart = fn.RequiresParentheses
                ? $"({string.Join(", ", fn.Parameters.Select(p => p.Display))})"
                : string.Empty;
            string suffix = $"{paramPart} : {fn.ReturnType} · {fn.Category}";

            string name = CaseKeyword(fn.Name);
            var item = new CompletionItem(
                displayText: name,
                source: this,
                icon: icon,
                filters: FunctionFilter,
                suffix: suffix,
                insertText: name,
                sortText: $"~1_{fn.Name}",  // intermix with keywords, alphabetically
                filterText: fn.Name,
                attributeIcons: ImmutableArray<ImageElement>.Empty);

            item.Properties.AddProperty(BuiltInFunctionKey, fn.Name);
            items.Add(item);
        }
    }

    // --- Function argument value completion (data types, dateparts) ---

    private static readonly ImageElement DataTypeIcon = new(CompletionIcons.DataType.ToImageId());
    private static readonly ImageElement DatePartIcon = new(CompletionIcons.DatePart.ToImageId());

    private CompletionContext BuildFunctionArgumentCompletion(SqlArgKind kind)
    {
        switch (kind)
        {
            case SqlArgKind.DataType:
                // Data types (INT, NVARCHAR, …) are keywords — follow the formatter's keyword casing.
                // Dateparts (yyyy, dd, …) are left as-is since they aren't cased as keywords.
                return BuildArgSuggestionItems(SqlBuiltInFunctions.DataTypes, DataTypeIcon, applyKeywordCase: true);
            case SqlArgKind.DatePart:
                return BuildArgSuggestionItems(SqlBuiltInFunctions.DateParts, DatePartIcon, applyKeywordCase: false);
            default:
                return CompletionContext.Empty;
        }
    }

    private CompletionContext BuildArgSuggestionItems(IReadOnlyList<SqlArgSuggestion> suggestions, ImageElement icon, bool applyKeywordCase)
    {
        var items = new List<CompletionItem>(suggestions.Count);
        foreach (var s in suggestions)
        {
            string name = applyKeywordCase ? CaseKeyword(s.Name) : s.Name;
            items.Add(new CompletionItem(
                displayText: name,
                source: this,
                icon: icon,
                filters: EmptyFilters,
                suffix: s.Detail,
                insertText: name,
                sortText: s.SortKey,
                filterText: s.Name,
                attributeIcons: ImmutableArray<ImageElement>.Empty));
        }

        return items.Count > 0
            ? new CompletionContext(items.ToImmutableArray())
            : CompletionContext.Empty;
    }

    // --- DBCC command completion ---

    /// <summary>Property key tagging a completion item as a DBCC command (value = command name).</summary>
    internal const string DbccCommandKey = "DbccCommand";

    private static readonly ImageElement DbccIcon = new(CompletionIcons.DbccCommand.ToImageId());

    private CompletionContext BuildDbccCommandCompletion()
    {
        var commands = SqlDbccCommands.All;
        var items = new List<CompletionItem>(commands.Count);

        foreach (var cmd in commands)
        {
            string name = CaseKeyword(cmd.Name);
            var item = new CompletionItem(
                displayText: name,
                source: this,
                icon: DbccIcon,
                filters: KeywordFilter,
                suffix: $"DBCC · {cmd.Category}",
                insertText: name,
                sortText: cmd.Name,
                filterText: cmd.Name,
                attributeIcons: ImmutableArray<ImageElement>.Empty);

            item.Properties.AddProperty(DbccCommandKey, cmd.Name);
            items.Add(item);
        }

        return items.Count > 0
            ? new CompletionContext(items.ToImmutableArray())
            : CompletionContext.Empty;
    }

    // --- ALTER clause completion (object kinds, table sub-actions) ---

    /// <summary>Property key carrying a completion item's tooltip description directly.</summary>
    internal const string ClauseDescriptionKey = "ClauseDescription";

    private static readonly ImageElement AlterClauseIcon = new(CompletionIcons.Keyword.ToImageId());

    private CompletionContext BuildAlterClauseCompletion(IReadOnlyList<AlterClause> clauses, string suffix)
    {
        var items = new List<CompletionItem>(clauses.Count);
        foreach (var c in clauses)
        {
            string keyword = CaseKeyword(c.Keyword);
            var item = new CompletionItem(
                displayText: keyword,
                source: this,
                icon: AlterClauseIcon,
                filters: KeywordFilter,
                suffix: suffix,
                insertText: keyword,
                sortText: c.Keyword,
                filterText: c.Keyword,
                attributeIcons: ImmutableArray<ImageElement>.Empty);

            item.Properties.AddProperty(ClauseDescriptionKey, c.Description);
            items.Add(item);
        }

        return items.Count > 0
            ? new CompletionContext(items.ToImmutableArray())
            : CompletionContext.Empty;
    }

    // [Conditional("DEBUG")] removes every call site (and its string-interpolation argument)
    // from Release builds. This logger does synchronous file I/O and was being invoked from
    // InitializeCompletion on the UI thread for every keystroke — a per-character disk write
    // that stalled typing and scrolling in shipped builds.
    [System.Diagnostics.Conditional("DEBUG")]
    internal static void DebugLog(string message)
    {
        try
        {
            var path = Path.Combine(@"C:\TEMP\sqlextended-ssms-debug.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] [Completion] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
