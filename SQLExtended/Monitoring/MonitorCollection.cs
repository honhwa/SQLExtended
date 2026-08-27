using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SQLExtended.Monitoring;

/// <summary>
/// Shared collection plumbing for the four dashboards: the ordered list of sections one poll intends to read, the
/// running commentary it reports to the status line while it works, and the point part-way through at which the
/// tab that is actually on screen has enough to be drawn.
///
/// It lives here beside <see cref="RowMerge"/> and <see cref="MonitorPin"/> for the reason those do — four copies
/// would drift, and the difference would be felt as one dashboard behaving unlike the others.
///
/// <para>Three things it exists to fix, all of them the same complaint from the other side of the screen: a window
/// that says "Collecting…" and nothing else. It cannot be told apart from a window that has hung, it gives no clue
/// which server-side read is the slow one when a poll takes ten seconds, and it withholds the numbers that were
/// ready in the first 200 ms until the last section — often the least interesting one — has finished.</para>
/// </summary>
internal readonly struct MonitorStep
{
    public MonitorStep(int number, int total, string label)
    {
        Number = number;
        Total = total;
        Label = label;
    }

    /// <summary>1-based position of the section now starting.</summary>
    public int Number { get; }

    /// <summary>How many sections this poll plans to read in total. Known up front — see <see cref="MonitorPlan"/>.</summary>
    public int Total { get; }

    /// <summary>What is being read, in the words the warning banner would use for it if it failed.</summary>
    public string Label { get; }

    /// <summary>
    /// The status line's text. Both halves earn their place: the label says which read is in flight (which is the
    /// question when one poll is slow), and the count says whether it is nearly done (which is the question when
    /// all of them are).
    /// </summary>
    public string Text => Total > 1 ? $"Reading {Label}…  ({Number} of {Total})" : $"Reading {Label}…";
}

/// <summary>
/// The sections one poll will read, in the order it will read them, with the ones backing the tab that is on
/// screen marked so they run first.
///
/// <para>Building the list before running it — rather than <c>await</c>ing a series of section calls in line — is
/// what makes the count in "(3 of 9)" exact and self-maintaining: a section added or made conditional on a
/// capability changes the denominator by construction, where a hand-kept total would drift the first time someone
/// added one.</para>
///
/// <para><b>Primary sections run first and their results are shown before the rest are read.</b> The gain is real
/// on a slow instance — the Overview no longer waits on a seeding DMV or a top-queries scan it does not display —
/// but it costs the one invariant that has to be held: while the primary results are being merged into the grids
/// on the UI thread, the collection must not still be writing to the snapshot. So the hook is <c>await</c>ed
/// rather than fired off. There is no window in which both threads touch it, which is worth more here than the
/// few milliseconds the round trip to the UI thread costs.</para>
/// </summary>
internal sealed class MonitorPlan
{
    private readonly struct Entry
    {
        public Entry(string label, Func<Task> read, bool primary) { Label = label; Read = read; Primary = primary; }
        public string Label { get; }
        public Func<Task> Read { get; }
        public bool Primary { get; }
    }

    private readonly List<Entry> _entries = new List<Entry>();
    private readonly IProgress<MonitorStep> _progress;
    private readonly Action<string> _warn;

    /// <param name="progress">Where to report each section as it starts. Null on the timer polls, which leave the
    /// status line showing the last poll's summary rather than flickering through the steps every few seconds.</param>
    /// <param name="warn">Records a section that threw. Same contract as the per-section try/catch this replaces:
    /// one unavailable view costs one tab and a named warning, never the poll.</param>
    public MonitorPlan(IProgress<MonitorStep> progress, Action<string> warn)
    {
        _progress = progress;
        _warn = warn;
    }

    /// <summary>How many sections ran, and how many of those failed. Reported alongside the timing.</summary>
    public int Ran { get; private set; }

    public int Failed { get; private set; }

    /// <param name="primary">
    /// True for the sections backing the tab the window opens on. They run first and are shown before the rest are
    /// read. Order within each group is the order added, so a section another one depends on stays ahead of it as
    /// long as both carry the same flag.
    /// </param>
    public MonitorPlan Add(string label, Func<Task> read, bool primary = false)
    {
        _entries.Add(new Entry(label, read, primary));
        return this;
    }

    /// <summary>Adds a section only when the server has what it reads — the capability probes decide this.</summary>
    public MonitorPlan AddIf(bool condition, string label, Func<Task> read, bool primary = false)
        => condition ? Add(label, read, primary) : this;

    /// <param name="onPrimaryComplete">
    /// Run once every primary section has finished, before any of the rest starts, and <b>awaited</b> — see the
    /// class remarks. Null skips the early paint and runs everything straight through.
    /// </param>
    public async Task RunAsync(Func<Task> onPrimaryComplete = null)
    {
        var ordered = _entries.Where(e => e.Primary).Concat(_entries.Where(e => !e.Primary)).ToList();
        int total = ordered.Count;
        int primaryCount = _entries.Count(e => e.Primary);
        int number = 0;

        foreach (var entry in ordered)
        {
            number++;
            _progress?.Report(new MonitorStep(number, total, entry.Label));

            try
            {
                await entry.Read().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Failed++;
                _warn?.Invoke($"{entry.Label}: {ex.Message}");
            }

            Ran++;

            if (number == primaryCount && onPrimaryComplete != null)
                await onPrimaryComplete().ConfigureAwait(false);
        }
    }
}
