using SQLExtended.History.Models;
using SQLExtended.Settings;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SQLExtended.History;

/// <summary>
/// Process-wide history service. Owns the SQLite store, coordinates dedupe + retention,
/// and exposes the API the tracker and the tool window use.
/// </summary>
internal sealed class HistoryService : IDisposable
{
    private static readonly Lazy<HistoryService> _instance = new(() => new HistoryService());
    public static HistoryService Instance => _instance.Value;

    private HistoryStore _store;
    private SQLExtendedSettings _settings;
    private DateTime _lastPurgeUtc = DateTime.MinValue;

    public event EventHandler<HistorySnapshot> SnapshotAdded;

    private HistoryService() { }

    public bool IsInitialized => _store != null;

    public string DatabasePath => _store?.DatabasePath;

    public SQLExtendedSettings Settings => _settings ??= SQLExtendedSettings.Load();

    public void ReloadSettings()
    {
        _settings = SQLExtendedSettings.Load();
    }

    public void Initialize()
    {
        if (_store != null) return;
        _settings = SQLExtendedSettings.Load();

        try
        {
            _store = new HistoryStore();
            _store.Initialize();

            // Best-effort startup purge.
            try { _store.Purge(Settings.HistoryRetentionDays, Settings.HistoryMaxPerDocument); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SQLExtended] history purge failed: {ex.Message}"); }

            _lastPurgeUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SQLExtended] HistoryStore init failed: {ex}");
            _store = null;
        }
    }

    /// <summary>
    /// Captures a snapshot if its content differs from the most recent row for the same document.
    /// Returns the inserted snapshot, or null if it was deduped, disabled, or too large.
    /// </summary>
    public HistorySnapshot CaptureIfChanged(string documentPath, string documentTitle, string text,
        string connectionKey, string databaseName, bool wasExecuted = false)
    {
        if (_store == null) return null;
        if (!Settings.HistoryEnabled) return null;
        if (string.IsNullOrEmpty(text)) return null;

        int byteCount = Encoding.UTF8.GetByteCount(text);
        if (byteCount > Settings.HistoryMaxTextBytes) return null;

        string hash = ComputeHash(text);

        try
        {
            string latest = _store.GetLatestHashForDocument(documentPath, documentTitle);
            if (string.Equals(latest, hash, StringComparison.Ordinal))
                return null;

            var snap = new HistorySnapshot
            {
                CapturedAtUtc = DateTime.UtcNow,
                DocumentPath = documentPath,
                DocumentTitle = documentTitle ?? "(untitled)",
                ConnectionKey = connectionKey,
                DatabaseName = databaseName,
                TextHash = hash,
                Text = text,
                TextLength = text.Length,
                WasExecuted = wasExecuted
            };

            _store.Insert(snap);

            // Opportunistic daily purge.
            if ((DateTime.UtcNow - _lastPurgeUtc).TotalHours >= 24)
            {
                try { _store.Purge(Settings.HistoryRetentionDays, Settings.HistoryMaxPerDocument); }
                catch { }
                _lastPurgeUtc = DateTime.UtcNow;
            }

            SnapshotAdded?.Invoke(this, snap);
            return snap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SQLExtended] CaptureIfChanged failed: {ex.Message}");
            return null;
        }
    }

    public List<HistorySnapshot> Query(string searchTerm, DateTime? sinceUtc, int maxResults)
    {
        return _store == null
            ? new List<HistorySnapshot>()
            : _store.Query(searchTerm, sinceUtc, maxResults);
    }

    public HistorySnapshot GetById(long id) => _store?.GetById(id);

    public void DeleteById(long id) => _store?.DeleteById(id);

    public void ClearAll() => _store?.ClearAll();

    public long RowCount => _store?.GetRowCount() ?? 0;

    public static string ComputeHash(string text)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
        var sb = new StringBuilder(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    public void Dispose()
    {
        _store?.Dispose();
        _store = null;
    }
}
