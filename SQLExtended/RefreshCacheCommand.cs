using System;
using System.ComponentModel.Design;
using SQLExtended.Cache;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended;

/// <summary>
/// Command handlers for schema cache operations:
///   - Refresh current database (Ctrl+Shift+R)
///   - Refresh all cached databases
///   - Clear all cache
/// </summary>
internal sealed class RefreshCacheCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");

    private const int RefreshCurrentId = 0x0300;
    private const int RefreshAllId = 0x0310;
    private const int ClearCacheId = 0x0320;

    private readonly AsyncPackage _package;
    private static RefreshCacheCommand _instance;

    private RefreshCacheCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;

        commandService.AddCommand(new MenuCommand(OnRefreshCurrent, new CommandID(CommandSet, RefreshCurrentId)));
        commandService.AddCommand(new MenuCommand(OnRefreshAll, new CommandID(CommandSet, RefreshAllId)));
        commandService.AddCommand(new MenuCommand(OnClearCache, new CommandID(CommandSet, ClearCacheId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new RefreshCacheCommand(package, commandService);
    }

    private void OnRefreshCurrent(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        string connectionString = ConnectionHelper.GetActiveConnectionString();
        if (string.IsNullOrEmpty(connectionString))
        {
            ShowMessage("Refresh Cache", "No active database connection found.");
            return;
        }

        string database = ConnectionHelper.GetCurrentDatabaseName();
        if (string.IsNullOrEmpty(database))
        {
            ShowMessage("Refresh Cache", "Could not determine the current database.");
            return;
        }

        var cache = SchemaCache.Instance;
        CacheStatusBar.SetText($"Schema: Refreshing {database}...");
        _ = Task.Run(async () =>
        {
            await cache.LoadDatabaseAsync(connectionString, database, forceFullRefresh: true);
        });
    }

    private void OnRefreshAll(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var cache = SchemaCache.Instance;
        CacheStatusBar.SetText("Schema: Refreshing all databases...");
        cache.RefreshAllAsync();
    }

    private void OnClearCache(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        SchemaCache.Instance.ClearAll();
        SchemaQueryService.ClearCache();
        CacheStatusBar.SetText("Schema: Cache cleared");
    }

    private void ShowMessage(string title, string message)
    {
        VsShellUtilities.ShowMessageBox(
            _package, message, title,
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
