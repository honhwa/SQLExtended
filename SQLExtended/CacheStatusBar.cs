using System;
using SQLExtended.Cache;
using SQLExtended.Cache.Models;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SQLExtended;

/// <summary>
/// Shows schema cache state in the VS/SSMS status bar.
/// Displays messages like "Schema: Loading AdventureWorks..." or "Schema: Ready (423 objects)".
/// </summary>
internal static class CacheStatusBar
{
    private static IVsStatusbar _statusBar;

    public static void Initialize(IServiceProvider serviceProvider)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _statusBar = serviceProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;

        // Subscribe to cache events
        SchemaCache.Instance.CacheRefreshed += OnCacheRefreshed;
    }

    public static void SetText(string text)
    {
        try
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _statusBar?.SetText(text);
            });
        }
        catch
        {
            // Status bar update failure is non-critical
        }
    }

    private static void OnCacheRefreshed(object sender, CacheRefreshEventArgs args)
    {
        string message = args.NewState switch
        {
            CacheState.Ready => $"Schema: Ready \u2014 {args.DatabaseName} ({args.ObjectCount:N0} objects)",
            CacheState.Loading => $"Schema: Loading {args.DatabaseName}...",
            CacheState.Error => $"Schema: Error loading {args.DatabaseName}",
            CacheState.Stale => $"Schema: {args.DatabaseName} (stale)",
            _ => "Schema: Not loaded"
        };

        SetText(message);
    }

    public static void Dispose()
    {
        SchemaCache.Instance.CacheRefreshed -= OnCacheRefreshed;
    }
}
