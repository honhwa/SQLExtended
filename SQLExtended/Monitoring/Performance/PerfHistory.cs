using System.Collections.Generic;

namespace SQLExtended.Monitoring.Performance;

/// <summary>
/// The in-memory rolling window behind the Live tab's sparklines. Live-only: nothing is persisted, and the
/// window resets when the tool window closes or the monitored server changes.
///
/// CPU is the exception and is not tracked here — the scheduler-monitor ring buffer already carries an hour of
/// per-minute samples server-side, so that chart is populated on the very first poll instead of drawing itself
/// in over the following ten minutes.
/// </summary>
internal sealed class PerfHistory
{
    public const int Capacity = 120;

    private readonly List<double> _batchRequests = new List<double>(Capacity);
    private readonly List<double> _pageLifeExpectancy = new List<double>(Capacity);
    private readonly List<double> _blocked = new List<double>(Capacity);
    private readonly List<double> _active = new List<double>(Capacity);
    private readonly List<double> _tempdbUsedPercent = new List<double>(Capacity);

    /// <summary>
    /// Appends this poll's values and hands the vitals fresh arrays. Fresh arrays, not the live lists: a
    /// binding set to a reference-equal value is a no-op, so reusing the buffers would freeze the sparklines.
    /// </summary>
    public void Record(PerfVitals vitals)
    {
        Append(_batchRequests, vitals.BatchRequestsPerSec ?? 0);
        Append(_pageLifeExpectancy, vitals.PageLifeExpectancy ?? 0);
        Append(_blocked, vitals.BlockedRequests);
        Append(_active, vitals.ActiveRequests);
        Append(_tempdbUsedPercent, vitals.TempdbUsedPercent);

        vitals.BatchHistory = _batchRequests.ToArray();
        vitals.PleHistory = _pageLifeExpectancy.ToArray();
        vitals.BlockedHistory = _blocked.ToArray();
        vitals.ActiveHistory = _active.ToArray();
        vitals.TempdbHistory = _tempdbUsedPercent.ToArray();
    }

    public void Clear()
    {
        _batchRequests.Clear();
        _pageLifeExpectancy.Clear();
        _blocked.Clear();
        _active.Clear();
        _tempdbUsedPercent.Clear();
    }

    private static void Append(List<double> series, double value)
    {
        if (series.Count == Capacity) series.RemoveAt(0);
        series.Add(value);
    }
}
