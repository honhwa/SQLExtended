using SQLExtended.History.Models;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace SQLExtended.History;

/// <summary>
/// SQLite persistence for tab history snapshots. Stored at
/// %APPDATA%\SQLExtended\SSMS\history.db so history survives SSMS restarts.
/// </summary>
internal sealed class HistoryStore : IDisposable
{
    private readonly string _dbPath;
    private SQLiteConnection _conn;
    private readonly object _writeLock = new();

    public HistoryStore()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "SQLExtended", "SSMS");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "history.db");
    }

    public string DatabasePath => _dbPath;

    public void Initialize()
    {
        _conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;Journal Mode=WAL;");
        _conn.Open();
        CreateSchema();
    }

    private void CreateSchema()
    {
        const string ddl = @"
            CREATE TABLE IF NOT EXISTS history (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                captured_at     TEXT    NOT NULL,
                document_path   TEXT,
                document_title  TEXT    NOT NULL,
                connection_key  TEXT,
                database_name   TEXT,
                text_hash       TEXT    NOT NULL,
                text            TEXT    NOT NULL,
                text_length     INTEGER NOT NULL,
                was_executed    INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_history_captured ON history(captured_at DESC);
            CREATE INDEX IF NOT EXISTS idx_history_doc      ON history(document_path, captured_at DESC);
            CREATE INDEX IF NOT EXISTS idx_history_hash     ON history(text_hash);";

        using var cmd = new SQLiteCommand(ddl, _conn);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Inserts a snapshot and returns the new id.
    /// </summary>
    public long Insert(HistorySnapshot snap)
    {
        lock (_writeLock)
        {
            using var cmd = new SQLiteCommand(
                @"INSERT INTO history (captured_at, document_path, document_title, connection_key, database_name, text_hash, text, text_length, was_executed)
                  VALUES (@ts, @path, @title, @ck, @db, @hash, @text, @len, @ex);
                  SELECT last_insert_rowid();", _conn);

            cmd.Parameters.AddWithValue("@ts", snap.CapturedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@path", (object)snap.DocumentPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@title", snap.DocumentTitle ?? "");
            cmd.Parameters.AddWithValue("@ck", (object)snap.ConnectionKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@db", (object)snap.DatabaseName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@hash", snap.TextHash);
            cmd.Parameters.AddWithValue("@text", snap.Text ?? "");
            cmd.Parameters.AddWithValue("@len", snap.TextLength);
            cmd.Parameters.AddWithValue("@ex", snap.WasExecuted ? 1 : 0);

            long id = (long)cmd.ExecuteScalar();
            snap.Id = id;
            return id;
        }
    }

    /// <summary>
    /// Returns the most recent text hash for the given document (by path, or by title for untitled).
    /// Used to dedupe consecutive identical snapshots.
    /// </summary>
    public string GetLatestHashForDocument(string documentPath, string documentTitle)
    {
        SQLiteCommand cmd;
        if (!string.IsNullOrEmpty(documentPath))
        {
            cmd = new SQLiteCommand(
                "SELECT text_hash FROM history WHERE document_path = @path ORDER BY id DESC LIMIT 1", _conn);
            cmd.Parameters.AddWithValue("@path", documentPath);
        }
        else
        {
            cmd = new SQLiteCommand(
                "SELECT text_hash FROM history WHERE document_path IS NULL AND document_title = @title ORDER BY id DESC LIMIT 1", _conn);
            cmd.Parameters.AddWithValue("@title", documentTitle ?? "");
        }

        using (cmd)
        {
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? null : result.ToString();
        }
    }

    /// <summary>
    /// Returns snapshots matching the optional search term and date filter, newest first.
    /// Search matches against title and text content (LIKE).
    /// </summary>
    public List<HistorySnapshot> Query(string searchTerm, DateTime? sinceUtc, int maxResults)
    {
        var results = new List<HistorySnapshot>();

        var sql = new System.Text.StringBuilder(
            @"SELECT id, captured_at, document_path, document_title, connection_key, database_name,
                     text_hash, text, text_length, was_executed
              FROM history WHERE 1=1");

        if (!string.IsNullOrWhiteSpace(searchTerm))
            sql.Append(" AND (document_title LIKE @pattern COLLATE NOCASE OR text LIKE @pattern COLLATE NOCASE)");

        if (sinceUtc.HasValue)
            sql.Append(" AND captured_at >= @since");

        sql.Append(" ORDER BY id DESC LIMIT @limit");

        using var cmd = new SQLiteCommand(sql.ToString(), _conn);
        if (!string.IsNullOrWhiteSpace(searchTerm))
            cmd.Parameters.AddWithValue("@pattern", $"%{searchTerm}%");
        if (sinceUtc.HasValue)
            cmd.Parameters.AddWithValue("@since", sinceUtc.Value.ToString("o"));
        cmd.Parameters.AddWithValue("@limit", maxResults <= 0 ? 500 : maxResults);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(ReadSnapshot(reader));

        return results;
    }

    public HistorySnapshot GetById(long id)
    {
        using var cmd = new SQLiteCommand(
            @"SELECT id, captured_at, document_path, document_title, connection_key, database_name,
                     text_hash, text, text_length, was_executed
              FROM history WHERE id = @id", _conn);
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadSnapshot(reader) : null;
    }

    public void DeleteById(long id)
    {
        lock (_writeLock)
        {
            using var cmd = new SQLiteCommand("DELETE FROM history WHERE id = @id", _conn);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    public void ClearAll()
    {
        lock (_writeLock)
        {
            using var cmd = new SQLiteCommand("DELETE FROM history; VACUUM;", _conn);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Removes rows older than <paramref name="retentionDays"/> and caps each document at
    /// <paramref name="maxPerDocument"/> rows (keeping the most recent).
    /// </summary>
    public int Purge(int retentionDays, int maxPerDocument)
    {
        lock (_writeLock)
        {
            int deleted = 0;

            if (retentionDays > 0)
            {
                string cutoff = DateTime.UtcNow.AddDays(-retentionDays).ToString("o");
                using var cmd = new SQLiteCommand("DELETE FROM history WHERE captured_at < @cutoff", _conn);
                cmd.Parameters.AddWithValue("@cutoff", cutoff);
                deleted += cmd.ExecuteNonQuery();
            }

            if (maxPerDocument > 0)
            {
                // Delete all but the most recent N rows per document_path (NULL grouped by title).
                const string sql = @"
                    DELETE FROM history WHERE id IN (
                        SELECT id FROM (
                            SELECT id,
                                   ROW_NUMBER() OVER (
                                       PARTITION BY COALESCE(document_path, '__null__' || document_title)
                                       ORDER BY id DESC
                                   ) AS rn
                            FROM history
                        ) WHERE rn > @cap
                    );";
                using var cmd = new SQLiteCommand(sql, _conn);
                cmd.Parameters.AddWithValue("@cap", maxPerDocument);
                deleted += cmd.ExecuteNonQuery();
            }

            return deleted;
        }
    }

    public long GetRowCount()
    {
        using var cmd = new SQLiteCommand("SELECT COUNT(*) FROM history", _conn);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static HistorySnapshot ReadSnapshot(SQLiteDataReader r)
    {
        return new HistorySnapshot
        {
            Id = r.GetInt64(0),
            CapturedAtUtc = DateTime.Parse(r.GetString(1)).ToUniversalTime(),
            DocumentPath = r.IsDBNull(2) ? null : r.GetString(2),
            DocumentTitle = r.IsDBNull(3) ? "" : r.GetString(3),
            ConnectionKey = r.IsDBNull(4) ? null : r.GetString(4),
            DatabaseName = r.IsDBNull(5) ? null : r.GetString(5),
            TextHash = r.IsDBNull(6) ? null : r.GetString(6),
            Text = r.IsDBNull(7) ? "" : r.GetString(7),
            TextLength = r.IsDBNull(8) ? 0 : r.GetInt32(8),
            WasExecuted = !r.IsDBNull(9) && r.GetInt32(9) != 0
        };
    }

    public void Dispose()
    {
        _conn?.Dispose();
        _conn = null;
    }
}
