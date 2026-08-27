using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLExtended.Monitoring.Performance;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace SQLExtended.Tests.Monitoring;

/// <summary>
/// Parses the Performance dashboard's T-SQL with ScriptDom.
///
/// <para>These batches are long, several are assembled at run time from a capability probe, and a syntax error
/// in one does not fail loudly — the section's try/catch turns it into a warning banner and an empty tab, which
/// reads as "this DMV is unavailable here". Parsing is the only check available without an instance to run
/// against, and it catches the mistakes that assembling SQL from string fragments actually produces: a lost
/// comma between substituted columns, an unbalanced bracket, a missing terminator between two result sets.</para>
///
/// <para>It cannot tell whether a column exists on a given release — that is what the capability probe is for.</para>
/// </summary>
public class PerfSqlTests
{
    private static IList<ParseError> Parse(string sql)
    {
        // The same parser the formatter uses, so "valid" means the same thing in both places.
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);

        IList<ParseError> errors;
        using (var reader = new StringReader(sql)) parser.Parse(reader, out errors);

        return errors;
    }

    private static void AssertParses(string label, string sql)
    {
        Assert.False(string.IsNullOrWhiteSpace(sql), label + " produced no SQL");

        var errors = Parse(sql);
        Assert.True(errors.Count == 0,
            label + " does not parse: " + string.Join("; ", errors.Select(e => $"line {e.Line}: {e.Message}")));
    }

    [Fact]
    public void VitalsSql_Parses() => AssertParses(nameof(PerfQueryService.VitalsSql), PerfQueryService.VitalsSql);

    [Fact]
    public void RequestsSql_Parses() => AssertParses(nameof(PerfQueryService.RequestsSql), PerfQueryService.RequestsSql);

    [Fact]
    public void FileStatsSql_Parses() => AssertParses(nameof(PerfQueryService.FileStatsSql), PerfQueryService.FileStatsSql);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WaitsSql_ParsesWithAndWithoutTheBenignFilter(bool includeBenign) =>
        AssertParses($"WaitsSql(includeBenign: {includeBenign})", PerfQueryService.WaitsSql(includeBenign));

    /// <summary>
    /// Every ranking metric, enumerated rather than listed: the metric only changes an ORDER BY expression, and
    /// a metric added later would otherwise ship untested. (An InlineData per metric is not available here —
    /// the enum is internal and a public test method cannot take one as a parameter.)
    /// </summary>
    [Fact]
    public void TopQueriesSql_ParsesForEveryRankingMetric()
    {
        foreach (PerfQueryMetric metric in System.Enum.GetValues(typeof(PerfQueryMetric)))
            AssertParses($"TopQueriesSql({metric})", "DECLARE @top int = 25;\n" + PerfQueryService.TopQueriesSql(metric));
    }

    /// <summary>
    /// The Server info batch as it renders when the instance has every optional column — the form the tab's
    /// "Open as query" button produces.
    /// </summary>
    [Fact]
    public void ServerInfoSql_ParsesWithEveryOptionalColumnPresent() =>
        AssertParses("PerfServerInfoQuery.Sql(all capabilities)", PerfServerInfoQuery.Sql(PerfServerInfoQuery.Capabilities.All));

    /// <summary>
    /// And as it renders against an instance that has none of them — an empty capability set is what an older
    /// release, or a probe that failed to return rows, produces. Every substituted column becomes a typed NULL
    /// and the two absent DMVs become empty result sets, and all of that still has to parse.
    /// </summary>
    [Fact]
    public void ServerInfoSql_ParsesWithNoOptionalColumnAtAll() =>
        AssertParses("PerfServerInfoQuery.Sql(no capabilities)", PerfServerInfoQuery.Sql(new PerfServerInfoQuery.Capabilities()));

    /// <summary>
    /// The reader addresses every column by name, so the substituted form has to expose the same aliases as the
    /// real one. A dropped alias would surface as an IndexOutOfRangeException from GetOrdinal on exactly the
    /// older release the substitution exists to support.
    /// </summary>
    [Fact]
    public void ServerInfoSql_KeepsTheSameColumnAliasesWhenColumnsAreSubstituted()
    {
        string[] substituted =
        {
            "scheduler_count", "max_workers_count", "socket_count", "cores_per_socket", "numa_node_count",
            "physical_memory_kb", "committed_kb", "committed_target_kb", "virtual_machine_type_desc",
            "softnuma_configuration_desc", "affinity_type_desc", "container_type_desc"
        };

        string withNothing = PerfServerInfoQuery.Sql(new PerfServerInfoQuery.Capabilities());

        foreach (string alias in substituted)
            Assert.Contains("AS " + alias, withNothing);
    }

    /// <summary>
    /// Every DMV that is only present on some releases has a rendering that returns no rows at all, so the reader
    /// still finds a result set in that position. Losing one would shift every later result set by one and quietly
    /// read the configuration rows as service rows.
    /// </summary>
    [Fact]
    public void ServerInfoSql_AlwaysProducesTheSameNumberOfResultSets()
    {
        int Statements(string sql)
        {
            var parser = new TSql170Parser(initialQuotedIdentifiers: true);
            IList<ParseError> errors;

            using (var reader = new StringReader(sql))
            {
                var fragment = parser.Parse(reader, out errors);
                Assert.Empty(errors);

                var script = Assert.IsType<TSqlScript>(fragment);
                return script.Batches.Sum(b => b.Statements.Count);
            }
        }

        // Eight reads: identity, sys info, host info, configuration, services, listener, tempdb, memory dumps.
        Assert.Equal(8, Statements(PerfServerInfoQuery.Sql(PerfServerInfoQuery.Capabilities.All)));
        Assert.Equal(8, Statements(PerfServerInfoQuery.Sql(new PerfServerInfoQuery.Capabilities())));
    }

    [Fact]
    public void ProbeSql_Parses()
    {
        // Reached through Sql() in the other tests; the probe runs as its own command, so it is checked directly.
        var field = typeof(PerfServerInfoQuery).GetField("ProbeSql",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(field);
        AssertParses("ProbeSql", (string)field.GetValue(null));
    }
}
