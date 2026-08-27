using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SQLExtended.Diagnostics;

internal enum DiagnosticLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// One line of the session log. Held by <see cref="DiagnosticLogBuffer"/> and bound directly by the
/// Diagnostics tab, which is why it raises <see cref="PropertyChanged"/>: a repeat updates an entry that
/// is already on screen rather than adding another.
///
/// <para><b>Repeats are counted, not appended.</b> The things that log here are on timers — the schema
/// cache refreshes every few minutes, the monitoring dashboards poll every five seconds, completion runs
/// per keystroke — so a server that is refusing connections produces the same line indefinitely. Left
/// uncollapsed it pushes everything that came before it out of the ring within a minute or two, which is
/// exactly the part worth reading. Both ends of the run are kept (<see cref="FirstSeen"/> /
/// <see cref="LastSeen"/>) so the collapse never hides when it started.</para>
///
/// <para>WPF marshals <see cref="PropertyChanged"/> for a single property onto the dispatcher itself, so
/// <see cref="Repeat"/> is safe to call from the poll thread that logged. Collection changes are not —
/// see the dispatcher hop in the Diagnostics tab.</para>
/// </summary>
internal sealed class DiagnosticLogEntry : INotifyPropertyChanged
{
    private DateTime _lastSeen;
    private int _repeats;

    public DiagnosticLogEntry(DateTime seen, DiagnosticLevel level, string source, string message, string detail)
    {
        FirstSeen = seen;
        _lastSeen = seen;
        _repeats = 1;
        Level = level;
        Source = source ?? "";
        Message = message ?? "";
        Detail = detail ?? "";
    }

    public DateTime FirstSeen { get; }
    public DiagnosticLevel Level { get; }
    public string Source { get; }
    public string Message { get; }

    /// <summary>The exception chain and stack, or empty. Shown in the detail pane rather than the grid row.</summary>
    public string Detail { get; }

    public DateTime LastSeen => _lastSeen;
    public int Repeats => _repeats;

    /// <summary>Grid text: the time, plus the end of the run once there is one.</summary>
    public string TimeText => _repeats > 1
        ? $"{FirstSeen:HH:mm:ss}-{_lastSeen:HH:mm:ss}"
        : FirstSeen.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public string CountText => _repeats > 1 ? "x" + _repeats.ToString(CultureInfo.InvariantCulture) : "";

    public string LevelText => Level switch
    {
        DiagnosticLevel.Error => "ERROR",
        DiagnosticLevel.Warning => "WARN",
        _ => "INFO"
    };

    public bool HasDetail => Detail.Length > 0;

    /// <summary>Records another occurrence of the same line. Called under the buffer's lock.</summary>
    internal void Repeat(DateTime seen)
    {
        _lastSeen = seen;
        _repeats++;
        Raise(nameof(LastSeen));
        Raise(nameof(Repeats));
        Raise(nameof(TimeText));
        Raise(nameof(CountText));
    }

    /// <summary>One line for the clipboard and the file sink. Detail is indented under it when present.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append(FirstSeen.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        if (_repeats > 1)
            sb.Append(" (to ").Append(_lastSeen.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append(", x").Append(_repeats.ToString(CultureInfo.InvariantCulture)).Append(')');
        sb.Append("  ").Append(LevelText.PadRight(5));
        sb.Append("  [").Append(Source).Append("]  ").Append(Message);

        if (Detail.Length > 0)
        {
            foreach (string line in Detail.Replace("\r\n", "\n").Split('\n'))
                sb.AppendLine().Append("        ").Append(line);
        }

        return sb.ToString();
    }

    public event PropertyChangedEventHandler PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class DiagnosticLogEventArgs : EventArgs
{
    public DiagnosticLogEventArgs(DiagnosticLogEntry entry, bool isNew, DiagnosticLogEntry evicted)
    {
        Entry = entry;
        IsNew = isNew;
        Evicted = evicted;
    }

    public DiagnosticLogEntry Entry { get; }

    /// <summary>False when the entry was already in the buffer and only its repeat count moved.</summary>
    public bool IsNew { get; }

    /// <summary>The entry pushed off the end to make room, or null. The view removes it rather than rebuilding.</summary>
    public DiagnosticLogEntry Evicted { get; }
}

/// <summary>
/// The session log itself: a bounded ring of entries, oldest first.
///
/// <para>Kept as an instance class free of VS, WPF and SqlClient so the test project can link it
/// (<c>SQLExtended.Tests/Diagnostics/DiagnosticLogBufferTests.cs</c>) — the same split
/// <c>ExportFileNaming</c> and <c>MonitorCollection</c> exist for, and for the same reason: everything
/// this holds is itself a report of something that already failed silently, so a ring that drops the
/// wrong entry or collapses two different errors into one is indistinguishable from the failure not
/// having happened. Its clock is a parameter for the same reason.</para>
///
/// <para>Free-threaded. Callers are on a poll thread, a completion thread or the UI thread with equal
/// likelihood, and the point of the thing is to still work when everything else is going wrong.</para>
/// </summary>
internal sealed class DiagnosticLogBuffer
{
    public const int DefaultCapacity = 500;

    private readonly LinkedList<DiagnosticLogEntry> _entries = new();
    private readonly object _gate = new();
    private readonly int _capacity;

    public DiagnosticLogBuffer(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>Raised for every occurrence, new or repeated. Fired outside the lock.</summary>
    public event EventHandler<DiagnosticLogEventArgs> Changed;

    public int Capacity => _capacity;

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <summary>
    /// Records an occurrence. When it repeats the entry currently at the end of the ring — same level,
    /// source, message and detail — the count on that entry moves instead of a line being added.
    /// </summary>
    public DiagnosticLogEntry Add(DiagnosticLevel level, string source, string message, string detail, DateTime seen)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;

        DiagnosticLogEntry entry;
        DiagnosticLogEntry evicted = null;
        bool isNew;

        lock (_gate)
        {
            var last = _entries.Last?.Value;
            if (last != null && Matches(last, level, source, message, detail))
            {
                last.Repeat(seen);
                entry = last;
                isNew = false;
            }
            else
            {
                entry = new DiagnosticLogEntry(seen, level, source, message, detail);
                _entries.AddLast(entry);
                isNew = true;

                if (_entries.Count > _capacity)
                {
                    evicted = _entries.First.Value;
                    _entries.RemoveFirst();
                }
            }
        }

        Changed?.Invoke(this, new DiagnosticLogEventArgs(entry, isNew, evicted));
        return entry;
    }

    private static bool Matches(DiagnosticLogEntry entry, DiagnosticLevel level, string source, string message, string detail)
    {
        return entry.Level == level
            && string.Equals(entry.Source, source ?? "", StringComparison.Ordinal)
            && string.Equals(entry.Message, message ?? "", StringComparison.Ordinal)
            && string.Equals(entry.Detail, detail ?? "", StringComparison.Ordinal);
    }

    /// <summary>Oldest first. A snapshot — safe to enumerate on any thread.</summary>
    public IReadOnlyList<DiagnosticLogEntry> Snapshot()
    {
        lock (_gate) return _entries.ToList();
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
    }

    /// <summary>The whole log as text, for the clipboard.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var entry in Snapshot())
            sb.AppendLine(entry.ToText());
        return sb.ToString();
    }
}
