using System;

namespace SQLExtended.History.Models;

/// <summary>
/// A single captured version of a SQL tab's text. Persisted in history.db.
/// </summary>
public sealed class HistorySnapshot
{
    public long Id { get; set; }
    public DateTime CapturedAtUtc { get; set; }

    /// <summary>Full path on disk if the tab is backed by a file; null for untitled query windows.</summary>
    public string DocumentPath { get; set; }

    /// <summary>Tab caption (e.g. "SQLQuery1.sql"). Always populated.</summary>
    public string DocumentTitle { get; set; }

    /// <summary>Server key at capture time (best-effort).</summary>
    public string ConnectionKey { get; set; }

    /// <summary>Database name at capture time (best-effort).</summary>
    public string DatabaseName { get; set; }

    /// <summary>SHA-256 of <see cref="Text"/>, used for dedupe.</summary>
    public string TextHash { get; set; }

    public string Text { get; set; }

    public int TextLength { get; set; }

    /// <summary>Reserved for a future on-execute capture path. Today we only set this on change.</summary>
    public bool WasExecuted { get; set; }

    /// <summary>First non-blank line of <see cref="Text"/>, truncated. Used by the list UI.</summary>
    public string Preview
    {
        get
        {
            if (string.IsNullOrEmpty(Text)) return "";
            int i = 0;
            while (i < Text.Length)
            {
                int nl = Text.IndexOf('\n', i);
                int end = nl < 0 ? Text.Length : nl;
                string line = Text.Substring(i, end - i).TrimEnd('\r').Trim();
                if (line.Length > 0)
                    return line.Length > 200 ? line.Substring(0, 200) : line;
                if (nl < 0) break;
                i = nl + 1;
            }
            return "";
        }
    }
}
