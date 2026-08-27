using System;
using System.Linq;
using SQLExtended.Monitoring.Replication;
using Xunit;

namespace SQLExtended.Tests.Monitoring;

/// <summary>
/// Tests for the replication value decoding and diagnostic rules.
///
/// Replication stores its state as bare numbers — run status, publication type, subscription status, a retention
/// period in one of four units — with no descriptive column anywhere in the distribution database. Every one of
/// those mappings is a place to be quietly wrong, and two of the rules describe conditions nobody wants to
/// reproduce on a real topology: a subscription past its retention window, and a published database whose log
/// cannot be truncated.
/// </summary>
public class ReplDiagnosticsTests
{
    // -----------------------------------------------------------------------------------------------
    // Value decoding
    // -----------------------------------------------------------------------------------------------

    // The enum is internal, so it cannot appear in a public test signature — the mapping is asserted inside the
    // method body instead of through [InlineData].
    [Fact]
    public void ToRunStatus_MapsTheHistoryTablesNumbers()
    {
        Assert.Equal(ReplRunStatus.Starting, ReplValueParser.ToRunStatus(1));
        Assert.Equal(ReplRunStatus.Succeeded, ReplValueParser.ToRunStatus(2));
        Assert.Equal(ReplRunStatus.InProgress, ReplValueParser.ToRunStatus(3));
        Assert.Equal(ReplRunStatus.Idle, ReplValueParser.ToRunStatus(4));
        Assert.Equal(ReplRunStatus.Retrying, ReplValueParser.ToRunStatus(5));
        Assert.Equal(ReplRunStatus.Failed, ReplValueParser.ToRunStatus(6));

        // Anything unrecognised, including a null history row, is Unknown rather than a wrong guess.
        Assert.Equal(ReplRunStatus.Unknown, ReplValueParser.ToRunStatus(null));
        Assert.Equal(ReplRunStatus.Unknown, ReplValueParser.ToRunStatus(99));
    }

    [Fact]
    public void Describe_NamesEveryRunStatusAndNothingElse()
    {
        Assert.Equal("Idle", ReplValueParser.Describe(ReplRunStatus.Idle));
        Assert.Equal("Running", ReplValueParser.Describe(ReplRunStatus.InProgress));
        Assert.Equal("Retrying", ReplValueParser.Describe(ReplRunStatus.Retrying));
        Assert.Equal("Failed", ReplValueParser.Describe(ReplRunStatus.Failed));
        Assert.Null(ReplValueParser.Describe(ReplRunStatus.Unknown));
    }

    [Theory]
    [InlineData(0, "Transactional")]
    [InlineData(1, "Snapshot")]
    [InlineData(2, "Merge")]
    [InlineData(null, null)]
    public void DescribePublicationType_MapsMSpublicationsValues(int? value, string expected)
    {
        Assert.Equal(expected, ReplValueParser.DescribePublicationType(value));
    }

    [Theory]
    [InlineData(0, "Push")]
    [InlineData(1, "Pull")]
    [InlineData(2, "Anonymous")]
    public void DescribeSubscriptionType_MapsMSsubscriptionsValues(int? value, string expected)
    {
        Assert.Equal(expected, ReplValueParser.DescribeSubscriptionType(value));
    }

    [Theory]
    [InlineData(0, "Inactive")]
    [InlineData(1, "Subscribed")]
    [InlineData(2, "Active")]
    public void DescribeSubscriptionStatus_MapsMSsubscriptionsValues(int? value, string expected)
    {
        Assert.Equal(expected, ReplValueParser.DescribeSubscriptionStatus(value));
    }

    [Theory]
    [InlineData(72, 0, 72d)]      // hours
    [InlineData(3, 1, 72d)]       // days
    [InlineData(2, 2, 336d)]      // weeks
    [InlineData(1, 3, 720d)]      // months
    [InlineData(3, null, 72d)]    // no unit column: publication retention is in days
    public void RetentionHours_NormalisesEveryUnitToHours(int retention, int? unit, double expected)
    {
        Assert.Equal(expected, ReplValueParser.RetentionHours(retention, unit));
    }

    [Fact]
    public void RetentionHours_IsNullWhenNoRetentionIsRecorded()
    {
        Assert.Null(ReplValueParser.RetentionHours(null, 1));
    }

    // -----------------------------------------------------------------------------------------------
    // Subscription arithmetic
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Subscription_TotalLatencyIsTheSumOfBothHops()
    {
        var row = new ReplSubscriptionRow { LogReaderLatencySeconds = 3, DistributionLatencySeconds = 7 };
        Assert.Equal(10d, row.TotalLatencySeconds);
    }

    [Fact]
    public void Subscription_TotalLatencyIsNullWhenNeitherHopReported()
    {
        var row = new ReplSubscriptionRow();
        Assert.Null(row.TotalLatencySeconds);

        // One hop reported is still a usable total — the other simply contributes nothing.
        row.DistributionLatencySeconds = 4;
        Assert.Equal(4d, row.TotalLatencySeconds);
    }

    [Fact]
    public void Subscription_RetentionUsedIsTimeSinceActivityOverTheWindow()
    {
        var row = new ReplSubscriptionRow
        {
            RetentionHours = 72,
            LastActivity = DateTime.Now.AddHours(-36),
            Thresholds = new ReplThresholds()
        };

        Assert.NotNull(row.RetentionUsedFraction);
        Assert.InRange(row.RetentionUsedFraction.Value, 0.49, 0.51);
    }

    [Fact]
    public void Subscription_RetentionUsedIsNullWithoutARetentionWindow()
    {
        var row = new ReplSubscriptionRow { RetentionHours = null, LastActivity = DateTime.Now.AddHours(-36) };
        Assert.Null(row.RetentionUsedFraction);
    }

    [Fact]
    public void Subscription_PastTheRetentionWindowIsUnhealthyRatherThanMerelyDegraded()
    {
        var row = new ReplSubscriptionRow
        {
            RetentionHours = 24,
            LastActivity = DateTime.Now.AddHours(-30),
            RunStatus = ReplRunStatus.Idle,
            Status = "Active",
            Thresholds = new ReplThresholds()
        };

        Assert.True(row.IsUnhealthy);
        Assert.False(row.IsWarning);   // the two are mutually exclusive by construction
    }

    [Fact]
    public void Subscription_AnIdleAgentIsHealthy()
    {
        // Idle is what a working continuous distribution agent looks like most of the time.
        var row = new ReplSubscriptionRow { RunStatus = ReplRunStatus.Idle, Status = "Active", Thresholds = new ReplThresholds() };

        Assert.False(row.IsUnhealthy);
        Assert.False(row.IsWarning);
    }

    [Fact]
    public void Subscription_ADisabledAgentJobIsAWarning()
    {
        var row = new ReplSubscriptionRow { RunStatus = ReplRunStatus.Idle, Status = "Active", JobEnabled = false, Thresholds = new ReplThresholds() };
        Assert.True(row.IsWarning);
    }

    [Fact]
    public void Publisher_LogHeldByReplicationIsOnlyCriticalOnceTheLogIsNearlyFull()
    {
        var row = new ReplPublisherDatabaseRow { LogReuseWait = "REPLICATION", LogPercentUsed = 40, Thresholds = new ReplThresholds() };
        Assert.True(row.IsLogHeldByReplication);
        Assert.False(row.IsUnhealthy);
        Assert.True(row.IsWarning);

        row.LogPercentUsed = 92;
        Assert.True(row.IsUnhealthy);
        Assert.False(row.IsWarning);
    }

    [Fact]
    public void Tracer_HopsAreMeasuredBetweenCommits()
    {
        var posted = new DateTime(2026, 7, 27, 10, 0, 0);
        var row = new ReplTracerRow
        {
            PublisherCommit = posted,
            DistributorCommit = posted.AddSeconds(2),
            SubscriberCommit = posted.AddSeconds(9)
        };

        Assert.Equal(2d, row.PublisherToDistributorSeconds);
        Assert.Equal(7d, row.DistributorToSubscriberSeconds);
        Assert.Equal(9d, row.TotalSeconds);
        Assert.False(row.IsWarning);
    }

    [Fact]
    public void Tracer_ATokenThatNeverArrivedIsFlaggedRatherThanShownAsZero()
    {
        var row = new ReplTracerRow { PublisherCommit = new DateTime(2026, 7, 27, 10, 0, 0), DistributorCommit = null, SubscriberCommit = null };

        Assert.Null(row.TotalSeconds);
        Assert.Null(row.PublisherToDistributorSeconds);
        Assert.True(row.IsWarning);
    }

    // -----------------------------------------------------------------------------------------------
    // Posting a tracer token is only possible from the publisher
    // -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("SQL01", "SQL01", true)]
    [InlineData("sql01", "SQL01", true)]
    [InlineData("SQL01", "SQL01\\MSSQLSERVER", true)]   // a default instance is recorded both ways
    [InlineData("SQL01\\MSSQLSERVER", "SQL01", true)]
    [InlineData("SQL01\\PROD", "SQL01", false)]         // a named instance is a different server
    [InlineData("SQL01", "SQL02", false)]
    [InlineData(null, "SQL01", false)]
    [InlineData("SQL01", null, false)]
    public void CanPostFrom_OnlyMatchesTheLocalInstance(string? local, string? publisher, bool expected)
    {
        Assert.Equal(expected, ReplActionService.CanPostFrom(local, publisher));
    }

    // -----------------------------------------------------------------------------------------------
    // The rules
    // -----------------------------------------------------------------------------------------------

    [Fact]
    public void Evaluate_NotConfiguredIsReportedByTheCollectorRatherThanTheRules()
    {
        // The rules are skipped entirely when there is nothing to judge, so the "not configured" message on the
        // snapshot is not competing with an all-clear row.
        var snapshot = new ReplSnapshot { UnavailableReason = "Replication is not configured on this instance." };

        ReplDiagnostics.Evaluate(snapshot, Caps(), new ReplThresholds());

        Assert.Empty(snapshot.Issues);
    }

    [Fact]
    public void Evaluate_AHealthyTopologyProducesAnExplicitAllClearAndNoProblems()
    {
        var snapshot = HealthyTopology();

        ReplDiagnostics.Evaluate(snapshot, Caps(), new ReplThresholds());

        Assert.DoesNotContain(snapshot.Issues, i => i.Severity != ReplIssueSeverity.Information);

        var summary = snapshot.Issues.First();
        Assert.Equal("Summary", summary.Area);
        Assert.Contains("No problems found", summary.Detail);
    }

    [Fact]
    public void Evaluate_ADeactivatedSubscriptionIsCriticalAndSaysAReinitializeIsNeeded()
    {
        var snapshot = HealthyTopology();
        snapshot.Subscriptions[0].Status = "Inactive";

        ReplDiagnostics.Evaluate(snapshot, Caps(), new ReplThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Subscription");
        Assert.Equal(ReplIssueSeverity.Critical, finding.Severity);
        Assert.Contains("reinitialized", finding.Recommendation);
    }

    [Fact]
    public void Evaluate_LatencyPastTheCriticalThresholdSplitsTheTwoHops()
    {
        var snapshot = HealthyTopology();
        snapshot.Subscriptions[0].LogReaderLatencySeconds = 100;
        snapshot.Subscriptions[0].DistributionLatencySeconds = 400;

        ReplDiagnostics.Evaluate(snapshot, Caps(), new ReplThresholds { LatencyWarningSeconds = 60, LatencyCriticalSeconds = 300 });

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Latency");
        Assert.Equal(ReplIssueSeverity.Critical, finding.Severity);
        Assert.Contains("log reader", finding.Detail);
        Assert.Contains("distribution", finding.Detail);
    }

    [Fact]
    public void Evaluate_ASubscriptionApproachingExpiryIsAWarningBeforeItBecomesCritical()
    {
        var snapshot = HealthyTopology();
        var subscription = snapshot.Subscriptions[0];
        subscription.RetentionHours = 72;
        subscription.LastActivity = DateTime.Now.AddHours(-60);   // 83% of the window

        ReplDiagnostics.Evaluate(snapshot, Caps(), new ReplThresholds { ExpiryWarningFraction = 0.75 });

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Expiry");
        Assert.Equal(ReplIssueSeverity.Warning, finding.Severity);

        // Past the window it becomes critical instead, and only once.
        subscription.LastActivity = DateTime.Now.AddHours(-80);
        ReplDiagnostics.Evaluate(snapshot, Caps(), new ReplThresholds { ExpiryWarningFraction = 0.75 });

        finding = Assert.Single(snapshot.Issues, i => i.Area == "Expiry");
        Assert.Equal(ReplIssueSeverity.Critical, finding.Severity);
    }

    [Fact]
    public void Evaluate_AFailedLogReaderIsCriticalAndMentionsTheLogItHolds()
    {
        var snapshot = HealthyTopology();
        snapshot.Agents.Single(a => a.AgentType == ReplAgentType.LogReader).RunStatus = ReplRunStatus.Failed;

        ReplDiagnostics.Evaluate(snapshot, Caps(), new ReplThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Log Reader");
        Assert.Equal(ReplIssueSeverity.Critical, finding.Severity);
        Assert.Contains("cannot be truncated", finding.Recommendation);
    }

    [Fact]
    public void Evaluate_AFailedDistributionAgentIsReportedAgainstItsSubscriptionNotTwice()
    {
        var snapshot = HealthyTopology();
        snapshot.Subscriptions[0].RunStatus = ReplRunStatus.Failed;
        snapshot.Agents.Single(a => a.AgentType == ReplAgentType.Distribution).RunStatus = ReplRunStatus.Failed;

        ReplDiagnostics.Evaluate(snapshot, Caps(), new ReplThresholds());

        // The subscription names the publication and subscriber; the agent's own generated name says less, so it
        // is deliberately skipped rather than reported alongside.
        Assert.Single(snapshot.Issues, i => i.Severity == ReplIssueSeverity.Critical);
        Assert.Contains(snapshot.Issues, i => i.Area == "Subscription" && i.Severity == ReplIssueSeverity.Critical);
        Assert.DoesNotContain(snapshot.Issues, i => i.Area == "Distribution");
    }

    [Fact]
    public void Evaluate_APublisherLogHeldByReplicationIsCriticalOnceNearlyFull()
    {
        var snapshot = HealthyTopology();
        var db = snapshot.PublisherDatabases[0];
        db.LogReuseWait = "REPLICATION";
        db.LogPercentUsed = 95;

        ReplDiagnostics.Evaluate(snapshot, Caps(), new ReplThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Publisher log" && i.Severity == ReplIssueSeverity.Critical);
        Assert.Contains("disk fills", finding.Recommendation);
    }

    [Fact]
    public void Evaluate_MergeConflictsAreReportedBecauseTheyAreResolvedSilently()
    {
        var snapshot = HealthyTopology();
        snapshot.Agents.Add(new ReplAgentRow
        {
            AgentType = ReplAgentType.Merge,
            AgentId = 90,
            Publisher = "SQL01",
            PublisherDb = "Sales",
            Publication = "SalesMerge",
            Subscriber = "SQL03",
            SubscriberDb = "SalesCopy",
            RunStatus = ReplRunStatus.Succeeded,
            Conflicts = 12
        });

        ReplDiagnostics.Evaluate(snapshot, Caps(), new ReplThresholds());

        var finding = Assert.Single(snapshot.Issues, i => i.Area == "Merge");
        Assert.Equal(ReplIssueSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void Evaluate_APublisherThatIsNotTheDistributorSaysWhatItCannotSee()
    {
        var snapshot = HealthyTopology();
        snapshot.Role.IsDistributor = false;
        snapshot.Role.DistributorName = "DIST01";

        var caps = Caps();
        caps.DistributionDatabase = null;

        ReplDiagnostics.Evaluate(snapshot, caps, new ReplThresholds());

        var finding = snapshot.Issues.First(i => i.Area == "Topology");
        Assert.Equal(ReplIssueSeverity.Information, finding.Severity);
        Assert.Contains("DIST01", finding.Detail);
    }

    [Fact]
    public void Evaluate_FindingsAreOrderedWorstFirst()
    {
        var snapshot = HealthyTopology();
        snapshot.Subscriptions[0].Status = "Inactive";                 // critical
        snapshot.Subscriptions[0].JobEnabled = false;                  // warning
        snapshot.Publications[0].SubscriptionCount = 0;                // warning

        ReplDiagnostics.Evaluate(snapshot, Caps(), new ReplThresholds());

        var severities = snapshot.Issues.Select(i => i.Severity).ToList();
        Assert.Equal(severities.OrderBy(s => s), severities);
    }

    // -----------------------------------------------------------------------------------------------
    // One transactional publication with one healthy push subscription, seen from the distributor.
    // -----------------------------------------------------------------------------------------------

    private static ReplCapabilities Caps() => new ReplCapabilities
    {
        ServerName = "SQL01",
        DistributionDatabase = "distribution",
        PublishedDatabaseCount = 1,
        CanReadJobs = true,
        HasPublications = true,
        HasSubscriptions = true,
        HasLogReaderAgents = true,
        HasDistributionAgents = true
    };

    private static ReplSnapshot HealthyTopology()
    {
        var snapshot = new ReplSnapshot { ServerName = "SQL01" };
        snapshot.Role.IsDistributor = true;
        snapshot.Role.IsPublisher = true;
        snapshot.Role.DistributionDatabase = "distribution";

        snapshot.Publications.Add(new ReplPublicationRow
        {
            Publisher = "SQL01",
            PublisherDb = "Sales",
            Publication = "SalesPub",
            PublicationType = "Transactional",
            ArticleCount = 12,
            SubscriptionCount = 1,
            RetentionHours = 72,
            SnapshotStatus = ReplRunStatus.Succeeded,
            SnapshotTime = DateTime.Now.AddDays(-1)
        });

        snapshot.Subscriptions.Add(new ReplSubscriptionRow
        {
            Publisher = "SQL01",
            PublisherDb = "Sales",
            Publication = "SalesPub",
            Subscriber = "SQL02",
            SubscriberDb = "SalesReporting",
            PublicationType = "Transactional",
            SubscriptionType = "Push",
            Status = "Active",
            AgentId = 5,
            RunStatus = ReplRunStatus.Idle,
            LastActivity = DateTime.Now.AddSeconds(-20),
            LogReaderLatencySeconds = 1,
            DistributionLatencySeconds = 2,
            RetentionHours = 72,
            JobEnabled = true,
            JobRunning = true,
            Thresholds = new ReplThresholds()
        });

        snapshot.Agents.Add(new ReplAgentRow
        {
            AgentType = ReplAgentType.LogReader,
            AgentId = 1,
            Name = "SQL01-Sales-1",
            Publisher = "SQL01",
            PublisherDb = "Sales",
            RunStatus = ReplRunStatus.Idle,
            LatencySeconds = 1,
            JobEnabled = true
        });

        snapshot.Agents.Add(new ReplAgentRow
        {
            AgentType = ReplAgentType.Distribution,
            AgentId = 5,
            Name = "SQL01-Sales-SalesPub-SQL02-5",
            Publisher = "SQL01",
            PublisherDb = "Sales",
            Publication = "SalesPub",
            Subscriber = "SQL02",
            SubscriberDb = "SalesReporting",
            RunStatus = ReplRunStatus.Idle,
            LatencySeconds = 2,
            JobEnabled = true
        });

        snapshot.PublisherDatabases.Add(new ReplPublisherDatabaseRow
        {
            DatabaseName = "Sales",
            IsPublished = true,
            RecoveryModel = "FULL",
            LogReuseWait = "NOTHING",
            LogPercentUsed = 12,
            LogSizeKb = 2_000_000,
            Thresholds = new ReplThresholds()
        });

        return snapshot;
    }
}
