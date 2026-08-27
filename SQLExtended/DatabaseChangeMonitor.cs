using Microsoft.VisualStudio.Shell;
using SQLExtended.Cache;
using SQLExtended.Cache.Models;
using SQLExtended.Settings;
using System;
using System.Threading;

namespace SQLExtended;

/// <summary>
/// Polls the active SSMS connection to detect database switches.
/// When the user changes the database dropdown, triggers a cache load for the new database.
/// </summary>
internal sealed class DatabaseChangeMonitor : IDisposable
{
    private Timer _timer;
    private string _lastConnectionString;
    private string _lastDatabase;

    /// <summary>
    /// Start polling for database changes at the configured interval
    /// (<see cref="SQLExtendedSettings.DatabaseChangePollSeconds"/>). A short fixed delay before the
    /// first check keeps startup responsive without racing package initialization.
    /// </summary>
    public void Start()
    {
        int intervalSeconds = Math.Max(1, SQLExtendedSettings.Current.DatabaseChangePollSeconds);
        var period = TimeSpan.FromSeconds(intervalSeconds);
        var firstCheck = TimeSpan.FromSeconds(Math.Min(5, intervalSeconds));
        _timer = new Timer(CheckForChange, null, firstCheck, period);
    }

    private void CheckForChange(object state)
    {
        try
        {
            string connStr = null;
            string database = null;

            // Connection extraction requires the UI thread
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                connStr = ConnectionHelper.GetActiveConnectionString();
                database = ConnectionHelper.GetCurrentDatabaseName();

                // Keep the snippet resolver's connection-derived placeholders ($dbname$, $server$)
                // current — it resolves off the UI thread and can't read SSMS state itself.
                Snippets.SnippetPlaceholderResolver.RefreshConnectionInfoFromSsms();
            });

            if (string.IsNullOrEmpty(connStr) || string.IsNullOrEmpty(database))
                return;

            // Detect a change
            if (connStr != _lastConnectionString || !string.Equals(database, _lastDatabase, StringComparison.OrdinalIgnoreCase))
            {
                _lastConnectionString = connStr;
                _lastDatabase = database;

                var cache = SchemaCache.Instance;
                string connKey = cache.GetConnectionKey(connStr);
                var cacheState = cache.GetState(connKey, database);

                if (cacheState == CacheState.NotLoaded || cacheState == CacheState.Error)
                {
                    CacheStatusBar.SetText($"Schema: Loading {database}...");
                    _ = cache.LoadDatabaseAsync(connStr, database);
                }
            }
        }
        catch
        {
            // Polling failure is non-critical
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
