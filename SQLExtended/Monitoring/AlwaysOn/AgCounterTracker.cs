using System;
using System.Collections.Generic;

namespace SQLExtended.Monitoring.AlwaysOn;

/// <summary>
/// Holds the previous reading of every AG performance counter that is cumulative, so the Throughput tab can
/// report rates rather than totals since instance start.
///
/// Same reasoning as the Performance dashboard's <c>PerfDeltaTracker</c>: read once, "Log Bytes Received/sec"
/// is a running total describing the server's whole life. What matters when a secondary is falling behind is
/// the change over the last few seconds. Counters whose <c>cntr_type</c> says they are already a level
/// (PERF_COUNTER_LARGE_RAWCOUNT) are passed straight through instead — subtracting two queue depths would be
/// meaningless.
///
/// The interval comes from the server's own <c>ms_ticks</c>, never the client clock, so neither a laggy network
/// nor a clock adjustment on this machine can skew a rate.
/// </summary>
internal sealed class AgCounterTracker
{
    /// <summary>PERF_COUNTER_BULK_COUNT and PERF_COUNTER_COUNTER — cumulative, must be differenced.</summary>
    private const int PerfCounterBulkCount = 272696576;
    private const int PerfCounterCounter = 272696320;

    private Dictionary<string, long> _previous;
    private long? _msTicks;

    /// <summary>True until a baseline exists — the first reading can only seed, not report rates.</summary>
    public bool NeedsBaseline => _msTicks == null;

    public void Clear()
    {
        _previous = null;
        _msTicks = null;
    }

    /// <summary>True for the counter types that accumulate and therefore need differencing.</summary>
    public static bool IsCumulative(int counterType) => counterType == PerfCounterBulkCount || counterType == PerfCounterCounter;

    /// <summary>
    /// Seconds since the previous sample, from the server's tick counter. Null when there is no baseline, or
    /// when the counter has gone backwards — which means the host restarted and every total reset with it.
    /// </summary>
    public double? IntervalSecondsFrom(long msTicks)
    {
        if (_msTicks == null) return null;
        long elapsed = msTicks - _msTicks.Value;
        return elapsed > 0 ? elapsed / 1000d : (double?)null;
    }

    /// <summary>Per-second rate for one cumulative counter, or null when it has no usable baseline.</summary>
    public double? RateFor(string key, long current, double? intervalSeconds)
    {
        if (_previous == null || intervalSeconds == null || intervalSeconds <= 0) return null;
        if (!_previous.TryGetValue(key, out long previous)) return null;

        long delta = current - previous;

        // Negative means the counters were reset (a restart, or the database was removed and re-added). Report
        // nothing rather than a negative rate that reads as a real measurement.
        if (delta < 0) return null;

        return delta / intervalSeconds.Value;
    }

    public void Store(Dictionary<string, long> current, long msTicks)
    {
        _previous = current;
        _msTicks = msTicks;
    }

    /// <summary>The dictionary key for one counter reading. Object and instance both matter — the same counter
    /// name appears under every database and every replica.</summary>
    public static string KeyFor(string objectName, string instanceName, string counterName) =>
        string.Concat(objectName ?? "", "|", instanceName ?? "", "|", counterName ?? "");

    /// <summary>Trims the padding SQL Server puts in <c>object_name</c> / <c>counter_name</c> (they are char columns).</summary>
    public static string Trim(string value) => value?.Trim() ?? "";

    /// <summary>Case-insensitive counter-name comparison, so a release that changes casing does not blank a column.</summary>
    public static bool Is(string counterName, string expected) => string.Equals(counterName, expected, StringComparison.OrdinalIgnoreCase);
}
