using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQLExtended.Cache;
using SQLExtended.Export;
using SQLExtended.Search;
using SQLExtended.Validation;
using System;
using System.Windows.Forms;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.ObjectExplorer;

/// <summary>
/// Injects a "SQLExtended" submenu into the SSMS Object Explorer node context menu.
///
/// SSMS builds Object Explorer node menus dynamically, so there is no stable VSCT menu to place
/// commands into. Instead — following brink-daniel/ssms-object-explorer-menu and Nicholas Ross's
/// SSMS-Schema-Folders — we reflect the underlying WinForms <see cref="TreeView"/> out of
/// IObjectExplorerService (via <see cref="ObjectExplorerHelper"/>) and hook
/// <see cref="Control.ContextMenuStripChanged"/>, appending our items each time SSMS rebuilds the
/// strip. Everything is wrapped in try/catch so a failure here never crashes SSMS.
/// </summary>
internal static class ObjectExplorerMenuService
{
    // Sentinel placed on the items we add, so we can detect (and skip) a strip we've already extended.
    private const string OurItemTag = "SQLExtended.ObjectExplorerMenu";

    private static AsyncPackage _package;
    private static TreeView _treeView;
    private static bool _hooked;

    /// <summary>
    /// Resolves the Object Explorer tree and hooks its context-menu event. The tree may not exist
    /// when the package loads, so we poll for a short while before giving up.
    /// </summary>
    public static async Task InitializeAsync(AsyncPackage package)
    {
        _package = package;

        for (int attempt = 0; attempt < 20 && !_hooked; attempt++)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (TryHook())
                return;
            await Task.Delay(1000);
        }
    }

    public static void Dispose()
    {
        try
        {
            if (_treeView != null)
                _treeView.ContextMenuStripChanged -= OnContextMenuStripChanged;
        }
        catch { }
        _treeView = null;
        _hooked = false;
    }

    private static bool TryHook()
    {
        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!(ObjectExplorerHelper.GetObjectExplorerTree() is TreeView tree))
                return false;

            _treeView = tree;
            _treeView.ContextMenuStripChanged += OnContextMenuStripChanged;
            _hooked = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void OnContextMenuStripChanged(object sender, EventArgs e)
    {
        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var tree = _treeView;
            var strip = tree?.ContextMenuStrip;
            if (strip == null || tree.SelectedNode == null || strip.Items == null)
                return;

            // The event fires whenever SSMS swaps the strip in; bail if we've already extended this one.
            foreach (ToolStripItem existing in strip.Items)
                if (OurItemTag.Equals(existing.Tag))
                    return;

            var oeService = ObjectExplorerHelper.GetObjectExplorerService();
            if (oeService == null)
                return;

            oeService.GetSelectedNodes(out int count, out INodeInformation[] nodes);
            if (count == 0 || nodes == null || nodes.Length == 0)
                return;

            var ctx = ObjectExplorerHelper.GetNodeContext(nodes[0]);
            if (ctx == null)
                return;

            var submenu = BuildSubmenu(tree, ctx);
            if (submenu == null || submenu.DropDownItems.Count == 0)
                return;

            strip.Items.Add(new ToolStripSeparator { Tag = OurItemTag });
            strip.Items.Add(submenu);
        }
        catch
        {
            // Never let a menu-building failure crash the SSMS shell.
        }
    }

    private static ToolStripMenuItem BuildSubmenu(TreeView tree, ObjectExplorerHelper.NodeContext ctx)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var menu = new ToolStripMenuItem("SQLExtended")
        {
            Tag = OurItemTag,
            ForeColor = tree.ForeColor,
            BackColor = tree.BackColor
        };

        switch (ctx.Kind)
        {
            case ObjectExplorerHelper.NodeKind.Server:
                AddItem(menu, tree, "Validate Schema…", () => OpenValidation(ctx));
                AddItem(menu, tree, "Search in Database…", () => OpenSearch(ctx));
                AddItem(menu, tree, "Refresh All Cached Databases", RefreshAll);
                menu.DropDownItems.Add(new ToolStripSeparator { Tag = OurItemTag });

                // All four dashboards from the server node: each is pinned to the connection it is opened with, and
                // this node is the only place that offers every server connected in Object Explorer without needing
                // a query window on it first.
                AddItem(menu, tree, "Performance Monitor…", () => OpenPerfDashboard(ctx));
                AddItem(menu, tree, "Always On Monitor…", () => OpenAgDashboard(ctx));
                AddItem(menu, tree, "Replication Monitor…", () => OpenReplDashboard(ctx));
                AddItem(menu, tree, "Agent Jobs Dashboard…", () => OpenJobsDashboard(ctx));
                break;

            case ObjectExplorerHelper.NodeKind.Database:
                AddItem(menu, tree, "Validate Schema…", () => OpenValidation(ctx));
                AddItem(menu, tree, "Search in Database…", () => OpenSearch(ctx));
                AddItem(menu, tree, "Refresh Schema Cache", () => RefreshDatabase(ctx));
                AddItem(menu, tree, "Export Schema…", () => ExportDatabase(ctx));
                break;

            case ObjectExplorerHelper.NodeKind.Table:
            case ObjectExplorerHelper.NodeKind.View:
                AddItem(menu, tree, "View Schema", () => ViewSchema(ctx));
                AddItem(menu, tree, "Export Object…", () => ExportObject(ctx));
                AddItem(menu, tree, "Refresh Schema Cache", () => RefreshDatabase(ctx));
                break;

            case ObjectExplorerHelper.NodeKind.JobsFolder:
            case ObjectExplorerHelper.NodeKind.Job:
                AddItem(menu, tree, "Agent Jobs Dashboard…", () => OpenJobsDashboard(ctx));
                break;

            // Anywhere in the Always On or replication subtrees, offer the dashboard for that subject — and the
            // Performance one too, since "the AG is behind" and "replication is behind" both turn into a question
            // about the server underneath more often than not.
            case ObjectExplorerHelper.NodeKind.AlwaysOn:
                AddItem(menu, tree, "Always On Monitor…", () => OpenAgDashboard(ctx));
                AddItem(menu, tree, "Performance Monitor…", () => OpenPerfDashboard(ctx));
                break;

            case ObjectExplorerHelper.NodeKind.Replication:
                AddItem(menu, tree, "Replication Monitor…", () => OpenReplDashboard(ctx));
                AddItem(menu, tree, "Performance Monitor…", () => OpenPerfDashboard(ctx));
                break;

            default:
                return null;
        }

        return menu;
    }

    private static void AddItem(ToolStripMenuItem parent, TreeView tree, string text, Action action)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = tree.ForeColor,
            BackColor = tree.BackColor
        };
        item.Click += (s, e) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { action(); }
            catch (Exception ex) { ShowError(ex.Message); }
        };
        parent.DropDownItems.Add(item);
    }

    // --- Actions (all reuse existing SQLExtended features) ---

    private static void OpenValidation(ObjectExplorerHelper.NodeContext ctx)
    {
        SchemaValidationControl.PendingTarget = (KeyFor(ctx), ctx.Database);
        ShowToolWindow(typeof(SchemaValidationToolWindow));
    }

    private static void OpenSearch(ObjectExplorerHelper.NodeContext ctx)
    {
        SqlSearchControl.PendingTarget = (KeyFor(ctx), ctx.Database);
        ShowToolWindow(typeof(SqlSearchToolWindow));
    }

    /// <summary>
    /// Opens the Agent Jobs dashboard pinned to the clicked node's server. This is the one launch point that does
    /// not need a query window at all: the node carries its own connection, so any server connected in Object
    /// Explorer can be monitored, and the dashboard keeps that connection for the window's lifetime.
    /// </summary>
    private static void OpenJobsDashboard(ObjectExplorerHelper.NodeContext ctx)
    {
        Monitoring.Jobs.JobsCommand.Show(_package, ctx?.ConnectionString, ServerLabel(ctx));
    }

    /// <summary>
    /// Opens the live performance dashboard pinned to the clicked node's server. Same contract as the other three:
    /// the node's own connection is what the window keeps, so no query window has to be open on that instance.
    /// </summary>
    private static void OpenPerfDashboard(ObjectExplorerHelper.NodeContext ctx)
    {
        Monitoring.Performance.PerfMonitorCommand.Show(_package, ctx?.ConnectionString, ServerLabel(ctx));
    }

    /// <summary>Opens the Always On monitor pinned to the clicked node's replica.</summary>
    private static void OpenAgDashboard(ObjectExplorerHelper.NodeContext ctx)
    {
        Monitoring.AlwaysOn.AgMonitorCommand.Show(_package, ctx?.ConnectionString, ServerLabel(ctx));
    }

    /// <summary>Opens the replication monitor pinned to the clicked node's instance.</summary>
    private static void OpenReplDashboard(ObjectExplorerHelper.NodeContext ctx)
    {
        Monitoring.Replication.ReplMonitorCommand.Show(_package, ctx?.ConnectionString, ServerLabel(ctx));
    }

    /// <summary>The node's server name for the window caption, or null to let it fall back to the Data Source.</summary>
    private static string ServerLabel(ObjectExplorerHelper.NodeContext ctx) =>
        string.IsNullOrEmpty(ctx?.Server) ? null : ctx.Server;

    private static void RefreshDatabase(ObjectExplorerHelper.NodeContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.ConnectionString) || string.IsNullOrEmpty(ctx.Database))
            return;

        CacheStatusBar.SetText($"Schema: Refreshing {ctx.Database}...");
        _ = Task.Run(async () =>
            await SchemaCache.Instance.LoadDatabaseAsync(ctx.ConnectionString, ctx.Database, forceFullRefresh: true));
    }

    private static void RefreshAll()
    {
        CacheStatusBar.SetText("Schema: Refreshing all databases...");
        SchemaCache.Instance.RefreshAllAsync();
    }

    private static void ViewSchema(ObjectExplorerHelper.NodeContext ctx)
    {
        string connStr = ctx.ConnectionString;
        string qualified = ctx.QualifiedObjectName;

        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            string script = await Task.Run(() => SchemaQueryService.GetSchemaScript(connStr, qualified));
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            new SchemaDialog(qualified, script, connStr).ShowDialog();
        });
    }

    private static void ExportDatabase(ObjectExplorerHelper.NodeContext ctx)
    {
        string path = PromptForSqlFile($"{ctx.Database}_schema.sql", "Export Database Schema");
        if (path == null)
            return;

        string connStr = ctx.ConnectionString;
        string db = ctx.Database;
        CacheStatusBar.SetText($"Schema: Exporting {db}...");
        _ = Task.Run(() =>
        {
            try
            {
                SchemaExportService.ExportDatabase(connStr, db, path);
                CacheStatusBar.SetText($"Schema: Exported {db}");
            }
            catch (Exception ex)
            {
                CacheStatusBar.SetText("Schema: Export failed");
                _ = ShowErrorAsync($"Export failed: {ex.Message}");
            }
        });
    }

    private static void ExportObject(ObjectExplorerHelper.NodeContext ctx)
    {
        string path = PromptForSqlFile($"{ctx.QualifiedObjectName}.sql", "Export Object Schema");
        if (path == null)
            return;

        string connStr = ctx.ConnectionString;
        string schema = ctx.Schema;
        string name = ctx.ObjectName;
        CacheStatusBar.SetText($"Schema: Exporting {ctx.QualifiedObjectName}...");
        _ = Task.Run(() =>
        {
            try
            {
                SchemaExportService.ExportObject(connStr, schema, name, path);
                CacheStatusBar.SetText("Schema: Export complete");
            }
            catch (Exception ex)
            {
                CacheStatusBar.SetText("Schema: Export failed");
                _ = ShowErrorAsync($"Export failed: {ex.Message}");
            }
        });
    }

    // --- Helpers ---

    private static string KeyFor(ObjectExplorerHelper.NodeContext ctx)
    {
        try { return SchemaCache.Instance.GetConnectionKey(ctx.ConnectionString); }
        catch { return ctx.Server; }
    }

    private static void ShowToolWindow(Type toolWindowType)
    {
        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var window = await _package.ShowToolWindowAsync(toolWindowType, 0, create: true, _package.DisposalToken);
            if (window?.Frame is IVsWindowFrame frame)
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());
        });
    }

    /// <summary>Shows a Save dialog for a .sql file. Returns the chosen path, or null if cancelled.
    /// Must be called on the UI thread.</summary>
    private static string PromptForSqlFile(string defaultFileName, string title)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            Filter = "SQL script (*.sql)|*.sql|All files (*.*)|*.*",
            DefaultExt = ".sql",
            AddExtension = true,
            FileName = SanitizeFileName(defaultFileName)
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static void ShowError(string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        VsShellUtilities.ShowMessageBox(_package, message, "SQLExtended",
            OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }

    private static async Task ShowErrorAsync(string message)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
        ShowError(message);
    }
}
