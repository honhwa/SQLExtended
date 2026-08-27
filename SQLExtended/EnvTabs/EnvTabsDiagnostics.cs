using System;
using System.Collections.Generic;
using System.Linq;

namespace SQLExtended.EnvTabs;

/// <summary>
/// A small in-memory ring of the last things that went wrong in this subsystem, surfaced in the EnvTabs
/// settings dialog.
///
/// This exists for the reason the Agent jobs dashboard learned the hard way: <b>VS only writes
/// ActivityLog.xml when it was launched with /log</b>, so for a normal SSMS session it is not there when
/// the failure happens. Everything here fails soft by design — a missing interop type, a locked config
/// file, a shell preference that would not set — which means the whole feature can quietly do nothing and
/// look identical to "the user has no rules yet". These notes are how that is told apart.
///
/// Free-threaded: callers are on a poll thread as often as the UI thread.
/// </summary>
internal static class EnvTabsDiagnostics
{
    private const int Capacity = 25;

    private static readonly LinkedList<string> Entries = new();
    private static readonly object Gate = new();

    /// <summary>Records a note, timestamped, dropping the oldest once full.</summary>
    public static void Note(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        lock (Gate)
        {
            // Repeating the same message every poll would push everything else out of a 25-entry ring
            // within a couple of minutes, which is exactly when the earlier entries matter most.
            if (Entries.Last?.Value?.EndsWith(message, StringComparison.Ordinal) == true) return;

            Entries.AddLast($"{DateTime.Now:HH:mm:ss}  {message}");
            while (Entries.Count > Capacity) Entries.RemoveFirst();
        }
    }

    /// <summary>Most recent last. Safe to call from any thread.</summary>
    public static IReadOnlyList<string> Recent()
    {
        lock (Gate) return Entries.ToList();
    }

    public static void Clear()
    {
        lock (Gate) Entries.Clear();
    }
}
