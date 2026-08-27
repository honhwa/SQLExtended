using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQLExtended.Cache;
using SQLExtended.Cache.Models;
using System;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.Settings;

/// <summary>
/// Command handler for SQLExtended Settings (opens unified settings dialog)
/// and the toolbar cache status indicator.
/// </summary>
internal sealed class SettingsCommand
{
    public static readonly Guid CommandSet = new("a1b2c3d4-e5f6-7890-abcd-123456789abc");

    private const int SettingsCommandId = 0x0500;
    private const int SnippetsCommandId = 0x0220;
    private const int ToolbarCacheStatusId = 0x0600;

    private readonly AsyncPackage _package;
    private static SettingsCommand _instance;
    private static OleMenuCommand _cacheStatusCommand;

    private SettingsCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;

        // Settings button
        commandService.AddCommand(
            new MenuCommand(OnOpenSettings, new CommandID(CommandSet, SettingsCommandId)));

        // Snippets button — opens Settings dialog on the Snippets tab
        commandService.AddCommand(
            new MenuCommand(OnOpenSnippets, new CommandID(CommandSet, SnippetsCommandId)));

        // Toolbar cache status indicator — OleMenuCommand supports dynamic text
        _cacheStatusCommand = new OleMenuCommand(OnCacheStatusClick, new CommandID(CommandSet, ToolbarCacheStatusId));
        _cacheStatusCommand.Text = "Schema: Not loaded";
        commandService.AddCommand(_cacheStatusCommand);

        // Subscribe to cache events to update toolbar text
        SchemaCache.Instance.CacheRefreshed += OnCacheRefreshed;
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (commandService != null)
            _instance = new SettingsCommand(package, commandService);
    }

    private void OnOpenSettings(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var settings = SQLExtendedSettings.Load();
            var dialog = new SQLExtendedSettingsDialog(settings);
            bool saved = dialog.ShowDialog() == true;

            // Grid Aggregates' auto-show is the one setting that arms a watcher outside any window, so it
            // has to be re-read here or turning it on would not take effect until the next SSMS start.
            if (saved)
                ResultsGrid.Aggregates.GridAggregatesWatcher.ArmAutoShow(_package);
        }
        catch (Exception ex)
        {
            VsShellUtilities.ShowMessageBox(
                _package, ex.Message, "SQLExtended Settings - Error",
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }

    private void OnOpenSnippets(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var settings = SQLExtendedSettings.Load();
            var dialog = new SQLExtendedSettingsDialog(settings, "Snippets");
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            VsShellUtilities.ShowMessageBox(
                _package, ex.Message, "SQLExtended Snippets - Error",
                OLEMSGICON.OLEMSGICON_CRITICAL,
                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }

    /// <summary>
    /// Clicking the cache status indicator refreshes the current database cache.
    /// </summary>
    private void OnCacheStatusClick(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            string connStr = ConnectionHelper.GetActiveConnectionString();
            string db = ConnectionHelper.GetCurrentDatabaseName();
            if (string.IsNullOrEmpty(connStr) || string.IsNullOrEmpty(db))
                return;

            CacheStatusBar.SetText($"Schema: Refreshing {db}...");
            UpdateToolbarText($"Schema: Refreshing {db}...");
            _ = Task.Run(async () =>
            {
                await SchemaCache.Instance.LoadDatabaseAsync(connStr, db, forceFullRefresh: true);
            });
        }
        catch
        {
            // Non-critical
        }
    }

    private void OnCacheRefreshed(object sender, CacheRefreshEventArgs args)
    {
        string text = args.NewState switch
        {
            CacheState.Ready => $"Schema: {args.DatabaseName} ({args.ObjectCount:N0})",
            CacheState.Loading => $"Schema: Loading {args.DatabaseName}...",
            CacheState.Error => $"Schema: Error",
            CacheState.Stale => $"Schema: {args.DatabaseName} (stale)",
            _ => "Schema: Not loaded"
        };

        UpdateToolbarText(text);
    }

    private static void UpdateToolbarText(string text)
    {
        try
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_cacheStatusCommand != null)
                {
                    _cacheStatusCommand.Text = text;
                }
            });
        }
        catch
        {
            // UI thread access failure is non-critical
        }
    }

    public static void Dispose()
    {
        if (_instance != null)
            SchemaCache.Instance.CacheRefreshed -= _instance.OnCacheRefreshed;
    }
}
