using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SQLExtended.Monitoring.AlwaysOn;
using Xunit;

namespace SQLExtended.Tests.Monitoring;

/// <summary>
/// Parses the Always On monitor's T-SQL with ScriptDom, in both of the shapes it is generated in, for the reasons
/// <see cref="PerfSqlTests"/> gives: these batches are assembled from string fragments chosen by a capability
/// probe, and a syntax error in one is caught by the section's try/catch and shown as a warning banner over an
/// empty tab — which reads as "this DMV is unavailable here" rather than as a bug.
///
/// <para>The queries now name either the HADR views or the per-poll temp copies of them
/// (<see cref="AgCatalog"/>), so every batch has two shapes, and the copy shape has an invariant no parser can
/// see: a batch that joins <c>#ag_something</c> is broken unless the copy prologue creates it. That is what
/// <see cref="EveryCopyReferencedIsCreatedByThePrologue"/> pins — the failure it guards against is a section
/// added later against a copy nobody made, which on the dashboard is one more empty tab with a warning over it.</para>
/// </summary>
public class AgSqlTests
{
    /// <summary>Every capability on, then every capability off: the two ends of the substitution the probe drives.</summary>
    private static AgCapabilities Caps(bool all)
    {
        var caps = new AgCapabilities { IsHadrEnabled = true, ServerName = "PRIMARY01" };

        // Reflection rather than a list of twenty assignments, so a capability added later is covered by
        // construction — an unlisted one would otherwise ship with its substitution untested.
        foreach (var property in typeof(AgCapabilities).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.PropertyType == typeof(bool) && property.CanWrite && property.Name != nameof(AgCapabilities.IsHadrEnabled))
                property.SetValue(caps, all);

        return caps;
    }

    /// <summary>Every batch a poll or the "Open as query" button can produce, labelled for the assertion message.</summary>
    private static IEnumerable<KeyValuePair<string, string>> AllBatches(AgCapabilities caps, AgCatalog catalog)
    {
        yield return Batch(nameof(AgQueryService.GroupsSql), AgQueryService.GroupsSql(caps, catalog));
        yield return Batch(nameof(AgQueryService.ReplicasSql), AgQueryService.ReplicasSql(caps, catalog));
        yield return Batch(nameof(AgQueryService.DatabasesSql), AgQueryService.DatabasesSql(caps, catalog));
        yield return Batch(nameof(AgQueryService.ClusterSql), AgQueryService.ClusterSql(caps));
        yield return Batch(nameof(AgQueryService.ClusterNodesSql), AgQueryService.ClusterNodesSql(caps, catalog));
        yield return Batch(nameof(AgQueryService.ListenersSql), AgQueryService.ListenersSql(caps, catalog));
        yield return Batch(nameof(AgQueryService.RoutingSql), AgQueryService.RoutingSql(caps, catalog));
        yield return Batch(nameof(AgQueryService.CountersSql), AgQueryService.CountersSql);
        yield return Batch(nameof(AgQueryService.PhysicalSeedingSql), AgQueryService.PhysicalSeedingSql);
        yield return Batch(nameof(AgQueryService.AutoSeedingSql), AgQueryService.AutoSeedingSql(catalog));
        yield return Batch(nameof(AgQueryService.HealthEventsSql), AgQueryService.HealthEventsSql(200));
    }

    private static KeyValuePair<string, string> Batch(string label, string sql) => new KeyValuePair<string, string>(label, sql);

    private static void AssertParses(string label, string sql)
    {
        Assert.False(string.IsNullOrWhiteSpace(sql), label + " produced no SQL");

        // The same parser the formatter uses, so "valid" means the same thing in both places.
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);

        IList<ParseError> errors;
        using (var reader = new StringReader(sql)) parser.Parse(reader, out errors);

        Assert.True(errors.Count == 0, label + " does not parse: " + string.Join("; ", errors.Select(e => $"line {e.Line}: {e.Message}")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryQueryParses_AgainstTheViews(bool allCapabilities)
    {
        var caps = Caps(allCapabilities);
        foreach (var batch in AllBatches(caps, new AgCatalog())) AssertParses($"{batch.Key} (views, caps: {allCapabilities})", batch.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryQueryParses_AgainstTheCopies(bool allCapabilities)
    {
        var caps = Caps(allCapabilities);
        foreach (var batch in AllBatches(caps, AgCatalog.Copies)) AssertParses($"{batch.Key} (copies, caps: {allCapabilities})", batch.Value);
    }

    [Fact]
    public void ThePrologueParses() => AssertParses(nameof(AgCatalog.PrologueSql), AgCatalog.PrologueSql(Caps(true)));

    /// <summary>
    /// The one thing the parser cannot see: a batch joining a copy the prologue does not create parses perfectly
    /// and fails on the server with "invalid object name".
    /// </summary>
    [Fact]
    public void EveryCopyReferencedIsCreatedByThePrologue()
    {
        var caps = Caps(true);
        var created = new HashSet<string>(Regex.Matches(AgCatalog.PrologueSql(caps), @"INTO\s+(#ag_\w+)").Cast<Match>().Select(m => m.Groups[1].Value),
                                          StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(created);

        foreach (var batch in AllBatches(caps, AgCatalog.Copies))
            foreach (Match match in Regex.Matches(batch.Value, @"#ag_\w+"))
                Assert.True(created.Contains(match.Value), $"{batch.Key} reads {match.Value}, which the prologue does not create");
    }

    /// <summary>
    /// The copy shape has to actually stop naming the views, or the poll pays for the copies and joins the views
    /// anyway — the slow plan with an extra batch in front of it, which is the one outcome worse than before.
    /// </summary>
    [Fact]
    public void TheCopyShapeDoesNotStillNameTheViewsItCopied()
    {
        var caps = Caps(true);

        // Read off the prologue rather than listed here: it names each view exactly once already.
        var copied = Regex.Matches(AgCatalog.PrologueSql(caps), @"FROM\s+(sys\.\w+)").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.NotEmpty(copied);

        foreach (var batch in AllBatches(caps, AgCatalog.Copies))
            foreach (var view in copied)
                Assert.DoesNotContain(view, batch.Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What the "Open as query" button hands over has to run on the first F5 — and must not gain a prologue it
    /// has no use for, which would read as the extension not knowing what its own query needs.
    /// </summary>
    [Fact]
    public void StandaloneAddsThePrologueOnlyToBatchesThatReadTheCopies()
    {
        var caps = Caps(true);

        string reads = AgQueryService.DatabasesSql(caps, AgCatalog.Copies);
        Assert.StartsWith(AgCatalog.PrologueSql(caps), AgCatalog.Standalone(reads, caps), StringComparison.Ordinal);

        string doesNot = AgQueryService.CountersSql;
        Assert.Equal(doesNot, AgCatalog.Standalone(doesNot, caps));

        Assert.Null(AgCatalog.Standalone(null, caps));
    }
}
