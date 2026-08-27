using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SQLExtended.Monitoring;
using Xunit;

namespace SQLExtended.Tests.Monitoring;

/// <summary>
/// The shared section plan behind all four dashboards' collections. Every rule in it fails quietly on screen — a
/// section run in the wrong order enriches rows that have not been read yet and simply leaves columns blank, a
/// hook fired at the wrong point paints a half-collected tab as though it were finished, and a swallowed
/// exception is indistinguishable from a server that had nothing to report.
/// </summary>
public class MonitorPlanTests
{
    private sealed class Recorder : IProgress<MonitorStep>
    {
        public List<MonitorStep> Steps { get; } = new List<MonitorStep>();
        public void Report(MonitorStep step) => Steps.Add(step);
    }

    private static MonitorPlan Plan(out List<string> log, out List<string> warnings, out Recorder progress)
    {
        log = new List<string>();
        warnings = new List<string>();
        progress = new Recorder();
        return new MonitorPlan(progress, warnings.Add);
    }

    // --- ordering: primary first, insertion order preserved within each group ---

    [Fact]
    public async Task Primary_sections_run_before_the_rest()
    {
        var plan = Plan(out var log, out _, out _);

        plan.Add("late", () => Record(log, "late"))
            .Add("early", () => Record(log, "early"), primary: true)
            .Add("later", () => Record(log, "later"))
            .Add("earlier", () => Record(log, "earlier"), primary: true);

        await plan.RunAsync();

        Assert.Equal(new[] { "early", "earlier", "late", "later" }, log);
    }

    /// <summary>
    /// Within a group the order added is the order run. Sections are not all independent — the Performance
    /// dashboard's baseline sample seeds the tracker every later read subtracts against, and replication's
    /// sp_replcounters enriches the publisher rows read before it — so this is a guarantee, not an accident.
    /// </summary>
    [Fact]
    public async Task Order_within_a_group_is_the_order_added()
    {
        var plan = Plan(out var log, out _, out _);

        plan.Add("a", () => Record(log, "a"), primary: true)
            .Add("b", () => Record(log, "b"), primary: true)
            .Add("c", () => Record(log, "c"), primary: true);

        await plan.RunAsync();

        Assert.Equal(new[] { "a", "b", "c" }, log);
    }

    // --- the early-paint hook ---

    [Fact]
    public async Task The_hook_runs_after_the_last_primary_section_and_before_the_first_of_the_rest()
    {
        var plan = Plan(out var log, out _, out _);

        plan.Add("rest", () => Record(log, "rest"))
            .Add("primary one", () => Record(log, "primary one"), primary: true)
            .Add("primary two", () => Record(log, "primary two"), primary: true);

        await plan.RunAsync(() => Record(log, "PAINT"));

        Assert.Equal(new[] { "primary one", "primary two", "PAINT", "rest" }, log);
    }

    /// <summary>
    /// The hook is awaited rather than fired off, which is the whole reason the early paint is safe: the UI merges
    /// the snapshot's rows while the collection is stopped, so the two threads never touch it at once.
    /// </summary>
    [Fact]
    public async Task The_hook_is_awaited_before_the_remaining_sections_start()
    {
        var plan = Plan(out var log, out _, out _);
        var hookReleased = new TaskCompletionSource<bool>();

        plan.Add("primary", () => Record(log, "primary"), primary: true)
            .Add("rest", () => Record(log, "rest"));

        var run = plan.RunAsync(async () =>
        {
            log.Add("hook entered");
            await hookReleased.Task.ConfigureAwait(false);
            log.Add("hook left");
        });

        // The plan cannot have moved past the hook while it is still suspended inside it.
        await Task.Yield();
        Assert.Equal(new[] { "primary", "hook entered" }, log);

        hookReleased.SetResult(true);
        await run;

        Assert.Equal(new[] { "primary", "hook entered", "hook left", "rest" }, log);
    }

    [Fact]
    public async Task A_plan_with_no_hook_runs_everything_straight_through()
    {
        var plan = Plan(out var log, out _, out _);

        plan.Add("one", () => Record(log, "one"), primary: true)
            .Add("two", () => Record(log, "two"));

        await plan.RunAsync();

        Assert.Equal(new[] { "one", "two" }, log);
    }

    // --- failure isolation: one unavailable view costs one tab and a named warning, never the poll ---

    [Fact]
    public async Task A_section_that_throws_is_recorded_and_the_rest_still_run()
    {
        var plan = Plan(out var log, out var warnings, out _);

        plan.Add("good", () => Record(log, "good"), primary: true)
            .Add("bad", () => throw new InvalidOperationException("Invalid column name 'comments'."))
            .Add("also good", () => Record(log, "also good"));

        await plan.RunAsync();

        Assert.Equal(new[] { "good", "also good" }, log);
        Assert.Equal(new[] { "bad: Invalid column name 'comments'." }, warnings);
        Assert.Equal(3, plan.Ran);
        Assert.Equal(1, plan.Failed);
    }

    /// <summary>
    /// Cancellation is the one exception that must not be turned into a warning: the window is closing or SSMS is
    /// shutting down, and reporting it as "this DMV was unavailable" would be a lie left on screen.
    /// </summary>
    [Fact]
    public async Task Cancellation_propagates_rather_than_becoming_a_warning()
    {
        var plan = Plan(out var log, out var warnings, out _);

        plan.Add("first", () => Record(log, "first"), primary: true)
            .Add("cancelled", () => throw new OperationCanceledException())
            .Add("never", () => Record(log, "never"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plan.RunAsync());

        Assert.Equal(new[] { "first" }, log);
        Assert.Empty(warnings);
    }

    // --- progress ---

    [Fact]
    public async Task Every_section_is_reported_before_it_runs_with_a_running_count()
    {
        var plan = Plan(out var log, out _, out var progress);

        plan.Add("groups", () => Record(log, "groups"), primary: true)
            .Add("listeners", () => Record(log, "listeners"))
            .Add("seeding", () => Record(log, "seeding"));

        await plan.RunAsync();

        Assert.Equal(3, progress.Steps.Count);
        Assert.Equal(new[] { "groups", "listeners", "seeding" }, progress.Steps.ConvertAll(s => s.Label));
        Assert.Equal(new[] { 1, 2, 3 }, progress.Steps.ConvertAll(s => s.Number));
        Assert.All(progress.Steps, s => Assert.Equal(3, s.Total));

        Assert.Equal("Reading groups…  (1 of 3)", progress.Steps[0].Text);
    }

    /// <summary>
    /// The denominator has to come from the plan rather than a hand-kept constant, or a section made conditional
    /// on a capability probe silently makes "(3 of 9)" wrong on every server that lacks it.
    /// </summary>
    [Fact]
    public async Task A_section_the_server_does_not_support_leaves_the_plan_and_the_total()
    {
        var plan = Plan(out var log, out _, out var progress);

        plan.Add("always", () => Record(log, "always"), primary: true)
            .AddIf(false, "absent DMV", () => Record(log, "absent DMV"))
            .AddIf(true, "present DMV", () => Record(log, "present DMV"));

        await plan.RunAsync();

        Assert.Equal(new[] { "always", "present DMV" }, log);
        Assert.All(progress.Steps, s => Assert.Equal(2, s.Total));
        Assert.Equal(2, plan.Ran);
    }

    [Fact]
    public void A_single_section_is_reported_without_a_count()
    {
        Assert.Equal("Reading jobs…", new MonitorStep(1, 1, "jobs").Text);
    }

    [Fact]
    public async Task A_plan_with_no_progress_sink_still_runs()
    {
        var log = new List<string>();
        var plan = new MonitorPlan(null, null);

        plan.Add("one", () => Record(log, "one"), primary: true)
            .Add("throws", () => throw new InvalidOperationException("boom"));

        await plan.RunAsync();

        Assert.Equal(new[] { "one" }, log);
        Assert.Equal(1, plan.Failed);
    }

    private static Task Record(List<string> log, string entry)
    {
        log.Add(entry);
        return Task.CompletedTask;
    }
}
