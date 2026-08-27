using System.Collections.Generic;

namespace SQLExtended.Monitoring.Performance;

/// <summary>
/// Holds the previous cumulative reading of every counter that only makes sense as a rate.
///
/// Wait stats, file I/O stalls and most performance counters are running totals since the instance started.
/// Read once, they describe the server's whole life — usually months, dominated by whatever happened during
/// one bad night in April. What you actually want when a server is slow *now* is the change over the last few
/// seconds, so every one of those sources is stored here and subtracted on the next poll.
///
/// The interval comes from the server's own <c>ms_ticks</c> rather than the client clock, so a laggy network
/// or a client clock adjustment cannot skew a rate.
/// </summary>
internal sealed class PerfDeltaTracker
{
    private Dictionary<string, WaitSample> _waits;
    private Dictionary<string, FileSample> _files;
    private Dictionary<string, long> _counters;
    private long? _msTicks;

    /// <summary>True until a baseline exists — the first poll can only seed, not report rates.</summary>
    public bool NeedsBaseline => _msTicks == null;

    internal struct WaitSample
    {
        public long WaitTimeMs;
        public long SignalWaitTimeMs;
        public long WaitingTasks;
    }

    internal struct FileSample
    {
        public long Reads;
        public long Writes;
        public long ReadStallMs;
        public long WriteStallMs;
        public long BytesRead;
        public long BytesWritten;
    }

    public void Clear()
    {
        _waits = null;
        _files = null;
        _counters = null;
        _msTicks = null;
    }

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

    public void SetTicks(long msTicks) => _msTicks = msTicks;

    // -----------------------------------------------------------------------------------------------------
    // Waits
    // -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the change since the previous poll, or null if this wait type has no baseline. A negative
    /// result means the counters were cleared (DBCC SQLPERF or a restart) and is reported as null rather than
    /// as a nonsense negative rate.
    /// </summary>
    public WaitSample? DeltaFor(string waitType, WaitSample current)
    {
        if (_waits == null || !_waits.TryGetValue(waitType, out var previous)) return null;

        var delta = new WaitSample
        {
            WaitTimeMs = current.WaitTimeMs - previous.WaitTimeMs,
            SignalWaitTimeMs = current.SignalWaitTimeMs - previous.SignalWaitTimeMs,
            WaitingTasks = current.WaitingTasks - previous.WaitingTasks
        };

        if (delta.WaitTimeMs < 0 || delta.WaitingTasks < 0) return null;
        return delta;
    }

    public void StoreWaits(Dictionary<string, WaitSample> current) => _waits = current;

    // -----------------------------------------------------------------------------------------------------
    // File I/O
    // -----------------------------------------------------------------------------------------------------

    public FileSample? DeltaFor(string fileKey, FileSample current)
    {
        if (_files == null || !_files.TryGetValue(fileKey, out var previous)) return null;

        var delta = new FileSample
        {
            Reads = current.Reads - previous.Reads,
            Writes = current.Writes - previous.Writes,
            ReadStallMs = current.ReadStallMs - previous.ReadStallMs,
            WriteStallMs = current.WriteStallMs - previous.WriteStallMs,
            BytesRead = current.BytesRead - previous.BytesRead,
            BytesWritten = current.BytesWritten - previous.BytesWritten
        };

        if (delta.Reads < 0 || delta.Writes < 0) return null;
        return delta;
    }

    public void StoreFiles(Dictionary<string, FileSample> current) => _files = current;

    // -----------------------------------------------------------------------------------------------------
    // Performance counters
    // -----------------------------------------------------------------------------------------------------

    /// <summary>Per-second rate for a cumulative (PERF_COUNTER_BULK_COUNT) counter.</summary>
    public double? RateFor(string counterName, long current, double? intervalSeconds)
    {
        if (_counters == null || intervalSeconds == null || intervalSeconds <= 0) return null;
        if (!_counters.TryGetValue(counterName, out long previous)) return null;

        long delta = current - previous;
        if (delta < 0) return null;

        return delta / intervalSeconds.Value;
    }

    public void StoreCounters(Dictionary<string, long> current) => _counters = current;
}
