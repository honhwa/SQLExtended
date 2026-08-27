using System;
using System.Linq;
using SQLExtended.Diagnostics;
using Xunit;

namespace SQLExtended.Tests.Diagnostics;

/// <summary>
/// The session log's ring. Every one of these pins something whose failure mode is a log that looks
/// complete and is not — an entry silently dropped, two different errors shown as one, or a run of
/// repeats whose start time has been overwritten. On screen none of those is distinguishable from the
/// failure not having been recorded at all, which is the whole thing this subsystem exists to fix.
/// </summary>
public class DiagnosticLogBufferTests
{
    private static DateTime At(int second) => new DateTime(2026, 8, 21, 14, 0, 0).AddSeconds(second);

    [Fact]
    public void EntriesAreKeptOldestFirst()
    {
        var buffer = new DiagnosticLogBuffer();

        buffer.Add(DiagnosticLevel.Error, "A", "first", "", At(0));
        buffer.Add(DiagnosticLevel.Error, "B", "second", "", At(1));
        buffer.Add(DiagnosticLevel.Error, "C", "third", "", At(2));

        Assert.Equal(new[] { "first", "second", "third" }, buffer.Snapshot().Select(e => e.Message));
    }

    [Fact]
    public void TheRingDropsTheOldestOnceFull()
    {
        var buffer = new DiagnosticLogBuffer(capacity: 3);

        for (int i = 0; i < 5; i++)
            buffer.Add(DiagnosticLevel.Error, "S", "message " + i, "", At(i));

        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { "message 2", "message 3", "message 4" }, buffer.Snapshot().Select(e => e.Message));
    }

    [Fact]
    public void AnEvictedEntryIsReportedSoTheViewCanRemoveIt()
    {
        var buffer = new DiagnosticLogBuffer(capacity: 2);
        DiagnosticLogEntry evicted = null;
        buffer.Changed += (_, e) => { if (e.Evicted != null) evicted = e.Evicted; };

        buffer.Add(DiagnosticLevel.Info, "S", "one", "", At(0));
        buffer.Add(DiagnosticLevel.Info, "S", "two", "", At(1));
        Assert.Null(evicted);

        buffer.Add(DiagnosticLevel.Info, "S", "three", "", At(2));

        // Without this the bound collection grows without bound while the ring stays capped, and the two
        // disagree for the rest of the session.
        Assert.NotNull(evicted);
        Assert.Equal("one", evicted.Message);
    }

    /// <summary>
    /// The reason repeats are collapsed at all: the schema cache refreshes on a timer and the dashboards
    /// poll every five seconds, so one unreachable server would otherwise fill the whole ring with one
    /// line and push out everything that came before it.
    /// </summary>
    [Fact]
    public void RepeatsOfTheSameLineAreCountedRatherThanAdded()
    {
        var buffer = new DiagnosticLogBuffer();

        for (int i = 0; i < 4; i++)
            buffer.Add(DiagnosticLevel.Error, "SchemaCache", "Full load failed", "detail", At(i * 5));

        var only = Assert.Single(buffer.Snapshot());
        Assert.Equal(4, only.Repeats);

        // Both ends of the run survive: collapsing must not hide when the trouble started.
        Assert.Equal(At(0), only.FirstSeen);
        Assert.Equal(At(15), only.LastSeen);
        Assert.Contains("x4", only.CountText);
    }

    [Fact]
    public void ARepeatIsReportedAsNotNew()
    {
        var buffer = new DiagnosticLogBuffer();
        int added = 0, repeated = 0;
        buffer.Changed += (_, e) => { if (e.IsNew) added++; else repeated++; };

        buffer.Add(DiagnosticLevel.Warning, "S", "same", "", At(0));
        buffer.Add(DiagnosticLevel.Warning, "S", "same", "", At(1));

        Assert.Equal(1, added);
        Assert.Equal(1, repeated);
    }

    [Fact]
    public void OnlyTheLastEntryCollapses()
    {
        var buffer = new DiagnosticLogBuffer();

        buffer.Add(DiagnosticLevel.Error, "S", "alpha", "", At(0));
        buffer.Add(DiagnosticLevel.Error, "S", "beta", "", At(1));
        buffer.Add(DiagnosticLevel.Error, "S", "alpha", "", At(2));

        // Collapsing a non-adjacent match would reorder the log, which is the one thing a timeline cannot
        // survive: "alpha" would appear to have happened before "beta" and then not again.
        Assert.Equal(new[] { "alpha", "beta", "alpha" }, buffer.Snapshot().Select(e => e.Message));
    }

    // The differing field is named rather than passed as a DiagnosticLevel: the enum is internal, and an
    // internal type cannot be a parameter of the public method xUnit needs for a [Theory].
    [Theory]
    [InlineData("level")]
    [InlineData("source")]
    [InlineData("message")]
    [InlineData("detail")]
    public void AnythingDifferentIsItsOwnEntry(string field)
    {
        var buffer = new DiagnosticLogBuffer();

        buffer.Add(DiagnosticLevel.Error, "S", "same", "same detail", At(0));
        buffer.Add(
            field == "level" ? DiagnosticLevel.Warning : DiagnosticLevel.Error,
            field == "source" ? "T" : "S",
            field == "message" ? "other" : "same",
            field == "detail" ? "other detail" : "same detail",
            At(1));

        // Two failures shown as one repeat of the first is a log that has lost an error while looking
        // healthy. The detail case is the one that matters most: the same message from two different
        // servers carries two different exceptions.
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public void ABlankMessageIsNotRecorded()
    {
        var buffer = new DiagnosticLogBuffer();

        Assert.Null(buffer.Add(DiagnosticLevel.Error, "S", null, "", At(0)));
        Assert.Null(buffer.Add(DiagnosticLevel.Error, "S", "   ", "", At(1)));
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void TextCarriesTheLevelSourceMessageAndDetail()
    {
        var buffer = new DiagnosticLogBuffer();
        buffer.Add(DiagnosticLevel.Error, "SchemaCache", "Full load failed for prod/Sales",
            "Microsoft.Data.SqlClient.SqlException: Login failed.", At(0));

        string text = buffer.ToText();

        // This text is what gets pasted into a bug report, so the exception has to survive the trip.
        Assert.Contains("ERROR", text);
        Assert.Contains("[SchemaCache]", text);
        Assert.Contains("Full load failed for prod/Sales", text);
        Assert.Contains("Login failed.", text);
    }

    [Fact]
    public void TextRecordsTheEndAndSizeOfARun()
    {
        var buffer = new DiagnosticLogBuffer();
        buffer.Add(DiagnosticLevel.Warning, "S", "still failing", "", At(0));
        buffer.Add(DiagnosticLevel.Warning, "S", "still failing", "", At(30));

        string text = buffer.ToText();

        Assert.Contains("x2", text);
        Assert.Contains("14:00:30", text);
    }

    [Fact]
    public void ClearEmptiesTheRing()
    {
        var buffer = new DiagnosticLogBuffer();
        buffer.Add(DiagnosticLevel.Info, "S", "something", "", At(0));

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.Snapshot());
    }

    [Fact]
    public void CapacityIsNeverZero()
    {
        // A zero or negative capacity would make Add drop everything it was handed, which is a log that is
        // on, reports no errors, and is wrong.
        var buffer = new DiagnosticLogBuffer(capacity: 0);

        buffer.Add(DiagnosticLevel.Error, "S", "kept", "", At(0));

        Assert.Equal(1, buffer.Capacity);
        Assert.Single(buffer.Snapshot());
    }
}
