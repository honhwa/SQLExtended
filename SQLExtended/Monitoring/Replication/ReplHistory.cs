using System;
using System.Collections.Generic;

namespace SQLExtended.Monitoring.Replication;

/// <summary>
/// The in-memory rolling window behind the subscription latency sparklines. Live-only by design: nothing is
/// persisted, and the window resets when the tool window closes or the monitored server changes.
///
/// Latency is the one replication number that only means something as a trend. A subscription 90 seconds behind
/// is fine if it was 300 seconds behind a minute ago and catastrophic if it was 5 — and the distribution history
/// tables keep every past run but no cheap way to plot the recent shape of one subscription.
///
/// Keyed per subscription, capped at <see cref="Capacity"/> samples. At the default 15-second poll that is a
/// thirty-minute view.
/// </summary>
internal sealed class ReplHistory
{
    public const int Capacity = 120;

    private readonly Dictionary<string, RingBuffer> _series = new Dictionary<string, RingBuffer>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records this poll's latency and hands each row its own history buffer for binding.</summary>
    public void Record(IEnumerable<ReplSubscriptionRow> rows)
    {
        foreach (var row in rows)
        {
            if (!_series.TryGetValue(row.Key, out var series))
                _series[row.Key] = series = new RingBuffer(Capacity);

            series.Add(row.TotalLatencySeconds.GetValueOrDefault());

            // A fresh array per poll, not the live buffer: a DependencyProperty set to a reference-equal value is
            // a no-op, so handing out the same mutated list would leave the sparklines frozen.
            row.LatencyHistory = series.Snapshot();
        }
    }

    /// <summary>Drops series for subscriptions that are no longer reported, so a removed one stops holding memory.</summary>
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
    private sealed class RingBuffer
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
