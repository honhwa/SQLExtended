using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Threading;

namespace SQLExtended.History;

/// <summary>
/// Polls all open DTE text documents every second, computes a hash of each, and once a
/// document's text has been stable for the configured debounce interval, hands it to
/// <see cref="HistoryService"/> for capture. Mirrors the polling pattern used by
/// <see cref="DatabaseChangeMonitor"/>.
/// </summary>
internal sealed class TabHistoryTracker : IDisposable
{
    private Timer _timer;
    private readonly Dictionary<string, DocState> _docs = new(StringComparer.OrdinalIgnoreCase);

    private const int TickIntervalMs = 1000;

    private sealed class DocState
    {
        public string LastHash;
        public DateTime LastChangedUtc;
        public string LastSnappedHash;
        public bool SeenInitial;
    }

    public void Start()
    {
        _timer = new Timer(Tick, null, TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(TickIntervalMs));
    }

    private void Tick(object state)
    {
        try
        {
            if (!HistoryService.Instance.IsInitialized) return;
            var settings = HistoryService.Instance.Settings;
            if (!settings.HistoryEnabled) return;

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ScanDocumentsOnUiThread(settings.HistoryDebounceMs);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SQLExtended] TabHistoryTracker tick failed: {ex.Message}");
        }
    }

    private void ScanDocumentsOnUiThread(int debounceMs)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        DTE2 dte;
        try { dte = (DTE2)Package.GetGlobalService(typeof(DTE)); }
        catch { return; }
        if (dte?.Documents == null) return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string connectionKey = null;
        string databaseName = null;
        try { connectionKey = ConnectionHelper.GetActiveConnectionString(); } catch { }
        try { databaseName = ConnectionHelper.GetCurrentDatabaseName(); } catch { }

        foreach (Document doc in dte.Documents)
        {
            string key;
            string title;
            string path;
            string text;

            try
            {
                if (!IsSqlDocument(doc)) continue;

                path = doc.FullName;
                title = doc.Name;
                key = !string.IsNullOrEmpty(path) ? "P:" + path : "T:" + title;

                var td = doc.Object("TextDocument") as TextDocument;
                if (td == null) continue;

                var start = td.StartPoint.CreateEditPoint();
                text = start.GetText(td.EndPoint);
            }
            catch
            {
                continue;
            }

            seen.Add(key);

            if (!_docs.TryGetValue(key, out var st))
            {
                st = new DocState();
                _docs[key] = st;
            }

            string hash = HistoryService.ComputeHash(text);

            if (!st.SeenInitial)
            {
                // Don't snapshot the initial content — only capture changes.
                st.LastHash = hash;
                st.LastSnappedHash = hash;
                st.LastChangedUtc = DateTime.UtcNow;
                st.SeenInitial = true;
                continue;
            }

            if (!string.Equals(hash, st.LastHash, StringComparison.Ordinal))
            {
                st.LastHash = hash;
                st.LastChangedUtc = DateTime.UtcNow;
                continue;
            }

            // Hash is stable. Capture if it's a new value we haven't snapshotted yet
            // and the debounce window has elapsed.
            if (!string.Equals(hash, st.LastSnappedHash, StringComparison.Ordinal)
                && (DateTime.UtcNow - st.LastChangedUtc).TotalMilliseconds >= debounceMs)
            {
                HistoryService.Instance.CaptureIfChanged(
                    documentPath: !string.IsNullOrEmpty(path) ? path : null,
                    documentTitle: title,
                    text: text,
                    connectionKey: connectionKey,
                    databaseName: databaseName);

                st.LastSnappedHash = hash;
            }
        }

        // Drop state for closed documents so the dictionary doesn't grow forever.
        if (_docs.Count > seen.Count)
        {
            var toRemove = new List<string>();
            foreach (var k in _docs.Keys)
                if (!seen.Contains(k)) toRemove.Add(k);
            foreach (var k in toRemove) _docs.Remove(k);
        }
    }

    private static bool IsSqlDocument(Document doc)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            string lang = doc.Language;
            if (!string.IsNullOrEmpty(lang) &&
                (lang.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
        }
        catch { }

        try
        {
            string name = doc.Name;
            if (!string.IsNullOrEmpty(name) &&
                name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                return true;

            // SSMS query windows are named "SQLQuery1.sql" etc., but in some cases
            // Language returns "T-SQL90". Both paths above catch the common cases.
        }
        catch { }

        return false;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
