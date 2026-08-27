using System;
using System.Collections.Generic;

namespace SQLExtended.Monitoring.AlwaysOn;

/// <summary>
/// The in-memory rolling window behind the queue sparklines. Live-only by design: nothing is persisted,
/// and the window resets when the tool window closes or the monitored server changes.
///
/// Keyed per database-per-replica, capped at <see cref="Capacity"/> samples. At the default 5-second poll
/// that is a ten-minute view — enough to tell a queue that is draining from one that is running away.
/// </summary>
internal sealed class AgHistory
{
    public const int Capacity = 120;

    private readonly Dictionary<string, Series> _series = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);

    private sealed class Series
    {
        public readonly RingBuffer Send = new RingBuffer(Capacity);
        public readonly RingBuffer Redo = new RingBuffer(Capacity);
    }

    /// <summary>Records this poll's queue sizes and hands each row its own history buffers for binding.</summary>
    public void Record(IEnumerable<AgDatabaseRow> rows)
    {
        foreach (var row in rows)
        {
            if (!_series.TryGetValue(row.Key, out var series))
                _series[row.Key] = series = new Series();

            series.Send.Add(row.LogSendQueueKb.GetValueOrDefault());
            series.Redo.Add(row.RedoQueueKb.GetValueOrDefault());

            // A fresh array per poll, not the live buffer: a DependencyProperty set to a reference-equal
            // value is a no-op, so handing out the same mutated list would leave the sparklines frozen.
            row.SendQueueHistory = series.Send.Snapshot();
            row.RedoQueueHistory = series.Redo.Snapshot();
        }
    }

    /// <summary>Drops series for rows that are no longer reported, so a removed database stops holding memory.</summary>
    public void Prune(ICollection<string> liveKeys)
    {
        if (liveKeys.Count == 0) { _series.Clear(); return; }

        List<string> dead = null;
        foreach (var key in _series.Keys)
        {
            if (liveKeys.Contains(key)) continue;
            (dead ?? (dead = new List<string>())).Add(key);
        }

        if (dead == null) return;
        foreach (var key in dead) _series.Remove(key);
    }

    public void Clear() => _series.Clear();

    /// <summary>Fixed-capacity sample buffer that drops the oldest sample once full.</summary>
    internal sealed class RingBuffer
    {
        private readonly List<double> _samples;
        private readonly int _capacity;

        public RingBuffer(int capacity)
        {
            _capacity = capacity;
            _samples = new List<double>(capacity);
        }

        public void Add(double value)
        {
            if (_samples.Count == _capacity)
                _samples.RemoveAt(0);
            _samples.Add(value);
        }

        public double[] Snapshot() => _samples.ToArray();
    }
}
