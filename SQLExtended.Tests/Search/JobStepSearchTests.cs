using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLExtended.Search;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace SQLExtended.Tests.Search;

/// <summary>
/// Pins SQL Search's Agent job step search.
///
/// <para>Everything here fails the same way: the search's own try/catch turns an exception into a status-line
/// warning and an empty list, and a wrong LIKE pattern does not even manage that — it returns zero rows and
/// looks exactly like a server with no matching job steps. Since there is no instance to run against, parsing
/// the SQL and pinning the escaping is the whole of what can be checked.</para>
/// </summary>
public class JobStepSearchTests
{
    private static void AssertParses(string label, string sql)
    {
        Assert.False(string.IsNullOrWhiteSpace(sql), label + " produced no SQL");

        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        IList<ParseError> errors;
        using (var reader = new StringReader(sql)) parser.Parse(reader, out errors);

        Assert.True(errors.Count == 0,
            label + " does not parse: " + string.Join("; ", errors.Select(e => $"line {e.Line}: {e.Message}")));
    }

    [Fact]
    public void ProbeSql_Parses() => AssertParses(nameof(JobStepSearchService.ProbeSql), JobStepSearchService.ProbeSql);

    [Fact]
    public void StepsSql_Parses() => AssertParses(nameof(JobStepSearchService.StepsSql), JobStepSearchService.StepsSql);

    /// <summary>
    /// The pattern is escaped with a backslash, which is not LIKE's default escape character — without the
    /// ESCAPE clause the backslashes become literals and every term containing one of the escaped characters
    /// silently matches nothing.
    /// </summary>
    [Fact]
    public void StepsSql_DeclaresTheEscapeCharacterTheClientEscapesWith()
    {
        Assert.Contains(@"ESCAPE '\'", JobStepSearchService.StepsSql);
        Assert.Equal(3, JobStepSearchService.StepsSql.Split(new[] { @"ESCAPE '\'" }, System.StringSplitOptions.None).Length - 1);
    }

    /// <summary>
    /// msdb on a case-sensitive instance would otherwise answer case-sensitively while every other part of
    /// this search is OrdinalIgnoreCase — the same term would find a procedure and miss the job step calling it.
    /// </summary>
    [Fact]
    public void StepsSql_ForcesACaseInsensitiveCollation()
    {
        Assert.Equal(3, JobStepSearchService.StepsSql.Split(new[] { "COLLATE Latin1_General_CI_AS" }, System.StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void LikePattern_WrapsTheTermInWildcards()
    {
        Assert.Equal("%usp_Load%", JobStepSearchService.BuildLikePattern("usp_Load").Replace(@"\", ""));
    }

    /// <summary>
    /// <c>[</c> matters as much as <c>%</c> and <c>_</c> here: searching for a bracketed identifier is a normal
    /// thing to type, and unescaped it opens a character class that swallows the rest of the pattern — the
    /// search then returns nothing, or worse, something else.
    /// </summary>
    [Theory]
    [InlineData("50%", @"%50\%%")]
    [InlineData("usp_Load", @"%usp\_Load%")]
    [InlineData("[dbo]", @"%\[dbo]%")]
    [InlineData(@"C:\Jobs", @"%C:\\Jobs%")]
    public void LikePattern_EscapesTheCharactersLikeWouldInterpret(string term, string expected)
    {
        Assert.Equal(expected, JobStepSearchService.BuildLikePattern(term));
    }

    private static JobStepMatch Step(string command, string stepName = "Load", string jobName = "Nightly") =>
        new() { Command = command, StepName = stepName, JobName = jobName };

    [Fact]
    public void Classify_PrefersTheCommandAndBuildsASnippet()
    {
        var step = Step("EXEC dbo.usp_LoadFacts @full = 1;");
        JobStepSearchService.Classify(step, "usp_LoadFacts");

        Assert.Equal("Command", step.MatchedIn);
        Assert.Contains("usp_LoadFacts", step.Snippet);
    }

    [Fact]
    public void Classify_FallsBackToTheStepAndJobNames()
    {
        var byStep = Step("EXEC dbo.Other;", stepName: "Load facts");
        JobStepSearchService.Classify(byStep, "facts");
        Assert.Equal("StepName", byStep.MatchedIn);
        Assert.Null(byStep.Snippet);

        var byJob = Step("EXEC dbo.Other;", stepName: "Step one", jobName: "Nightly load");
        JobStepSearchService.Classify(byJob, "nightly");
        Assert.Equal("JobName", byJob.MatchedIn);
    }

    /// <summary>
    /// The server matched under a CI collation, which is not exactly OrdinalIgnoreCase — width, kana and a
    /// few accent pairs can differ. A row the client scan cannot place is the server's answer, not a mistake,
    /// so it has to survive with a generic label rather than be dropped.
    /// </summary>
    [Fact]
    public void Classify_KeepsARowItCannotPlaceItself()
    {
        var step = Step("EXEC dbo.Other;");
        JobStepSearchService.Classify(step, "unfindable");

        Assert.Equal("JobStep", step.MatchedIn);
    }

    /// <summary>
    /// A job step command is usually a whole formatted batch, so a raw excerpt of it is mostly indentation.
    /// </summary>
    [Fact]
    public void Snippet_CollapsesWhitespaceAndMarksWhatItTrimmed()
    {
        string command = new string('a', 200) + "\r\n\t   TARGET   \r\n" + new string('b', 200);
        int index = command.IndexOf("TARGET", System.StringComparison.Ordinal);

        string snippet = JobStepSearchService.BuildSnippet(command, index, "TARGET".Length);

        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.Contains(" TARGET ", snippet);
        Assert.DoesNotContain("\n", snippet);
        Assert.DoesNotContain("  ", snippet);
    }

    [Fact]
    public void Snippet_LeavesAShortCommandWhole()
    {
        string snippet = JobStepSearchService.BuildSnippet("EXEC dbo.usp_Load;", 10, 8);

        Assert.Equal("EXEC dbo.usp_Load;", snippet);
    }
}
