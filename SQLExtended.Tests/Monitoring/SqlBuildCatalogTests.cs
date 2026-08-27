using System;
using System.Globalization;
using System.Linq;
using SQLExtended.Monitoring.Performance;
using Xunit;

namespace SQLExtended.Tests.Monitoring;

/// <summary>
/// The build-list lookup behind the Performance dashboard's Server info tab.
///
/// <para>Almost nothing here is pinned to a particular build number, because the snapshot is regenerated from
/// the source page and tests that named "CU26" would fail on the next refresh for no reason. The cases are
/// derived from the catalog at run time instead — take a release's newest listed build and it must report as
/// the newest, add one to it and it must report as newer than the snapshot — so they keep testing the same
/// rules against whatever data is embedded. The few literals that do appear (SQL Server 2022's RTM build, the
/// 10.50 release key) are historical facts that cannot change.</para>
/// </summary>
public class SqlBuildCatalogTests
{
    // A date late enough that no release is still in mainstream support by accident, used where the test is
    // about build matching rather than lifecycle.
    private static readonly DateTime AsOf = new DateTime(2026, 7, 28);

    private static SqlServerRelease Release(string key) =>
        SqlBuildCatalog.Releases.FirstOrDefault(r => r.Key == key);

    // =====================================================================================================
    // Version parsing and comparison
    // =====================================================================================================

    [Theory]
    [InlineData("16.0.4265.3", 16, 0, 4265, 3)]
    [InlineData("10.50.6000.34", 10, 50, 6000, 34)]
    [InlineData("8.0.194", 8, 0, 194, 0)]
    [InlineData("6.50.201", 6, 50, 201, 0)]
    [InlineData("9.0.1399.06", 9, 0, 1399, 6)]
    [InlineData("15.0", 15, 0, 0, 0)]
    public void TryParse_ReadsEveryFormTheBuildListUses(string text, int major, int minor, int build, int revision)
    {
        SqlVersion version;
        Assert.True(SqlVersion.TryParse(text, out version));

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(build, version.Build);
        Assert.Equal(revision, version.Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("16")]
    [InlineData("16.0.4265.3.1")]
    [InlineData("Microsoft SQL Server 2022")]
    [InlineData("16.0.x.3")]
    [InlineData("-16.0.1.1")]
    public void TryParse_RejectsWhatIsNotABuildNumber(string text)
    {
        SqlVersion version;
        Assert.False(SqlVersion.TryParse(text, out version));
    }

    /// <summary>
    /// The one comparison that must never be done as text. "16.0.4265.3" sorts *below* "16.0.985.1"
    /// lexically, which would report a fully patched server as years behind.
    /// </summary>
    [Fact]
    public void Compare_IsNumericPerComponentNotLexical()
    {
        var patched = Parse("16.0.4265.3");
        var older = Parse("16.0.985.1");

        Assert.True(patched > older);
        Assert.True(older < patched);
        Assert.True(string.CompareOrdinal("16.0.4265.3", "16.0.985.1") < 0); // what the naive version would have said
    }

    [Fact]
    public void Compare_OrdersOnRevisionWhenTheBuildMatches()
    {
        Assert.True(Parse("16.0.4265.3") > Parse("16.0.4265.2"));
        Assert.True(Parse("16.0.4265.3") == Parse("16.0.4265.3"));
        Assert.True(Parse("13.0.1601.5") < Parse("13.0.1708.0"));
    }

    /// <summary>2008 R2 is 10.50 and 2008 is 10.0 — different products, and their support dates are five years apart.</summary>
    [Fact]
    public void ReleaseKey_KeepsTheMinorVersionSo2008R2IsNot2008()
    {
        Assert.Equal("10.50", Parse("10.50.6000.34").ReleaseKey);
        Assert.Equal("10.0", Parse("10.0.6000.29").ReleaseKey);

        var r2 = Release("10.50");
        var original = Release("10.0");

        Assert.NotNull(r2);
        Assert.NotNull(original);
        Assert.Contains("2008 R2", r2.Name);
        Assert.Equal("SQL Server 2008", original.Name);
        Assert.NotEmpty(r2.Builds);
    }

    // =====================================================================================================
    // The embedded snapshot
    // =====================================================================================================

    [Fact]
    public void Snapshot_HasADateAndTheReleasesAnSsmsUserCanConnectTo()
    {
        DateTime snapshot;
        Assert.True(DateTime.TryParseExact(SqlBuildCatalog.SnapshotDate, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out snapshot));

        // The releases SSMS 22 can actually attach to. If a refresh of the snapshot dropped one of these, the
        // tab would silently stop recognising a supported server.
        foreach (string key in new[] { "13.0", "14.0", "15.0", "16.0", "17.0" })
        {
            var release = Release(key);
            Assert.NotNull(release);
            Assert.False(string.IsNullOrWhiteSpace(release.Name));
            Assert.NotEmpty(release.Builds);
            Assert.NotNull(release.LatestBuild);
        }
    }

    [Fact]
    public void Snapshot_HasNoReleaseWhoseBuildsBelongToAnother()
    {
        foreach (var release in SqlBuildCatalog.Releases)
            foreach (var build in release.Builds)
                Assert.Equal(release.Key, build.Version.ReleaseKey);
    }

    [Fact]
    public void Snapshot_ListsBuildsNewestFirst()
    {
        // Lookup's "closest build below" walk stops at the first build under the server's version, which is
        // only the closest one if the list is descending.
        foreach (var release in SqlBuildCatalog.Releases)
        {
            for (int i = 1; i < release.Builds.Count; i++)
                Assert.True(release.Builds[i - 1].Version >= release.Builds[i].Version,
                    release.Name + " is not in descending build order at " + release.Builds[i].Build);
        }
    }

    [Fact]
    public void Snapshot_NamesTheServicingLevelOfNearlyEveryModernBuild()
    {
        // A generator regression (a changed column order, a description format the rules no longer match)
        // shows up as labels disappearing en masse. Individual oddities — one-off advisories with no
        // servicing level at all — are expected, hence a proportion rather than "all".
        foreach (string key in new[] { "13.0", "14.0", "15.0", "16.0", "17.0" })
        {
            var release = Release(key);
            int labelled = release.Builds.Count(b => !string.IsNullOrEmpty(b.Label));

            Assert.True(labelled >= release.Builds.Count * 0.9,
                $"{release.Name}: only {labelled} of {release.Builds.Count} builds have a servicing level");
        }
    }

    [Fact]
    public void Snapshot_ClassifiesCumulativeUpdatesAndCarriesTheSourceWording()
    {
        var release = Release("16.0");

        Assert.Contains(release.Builds, b => b.Kind == SqlBuildKind.CumulativeUpdate);
        Assert.Contains(release.Builds, b => b.Kind == SqlBuildKind.SecurityUpdate);
        Assert.Contains(release.Builds, b => b.Kind == SqlBuildKind.Rtm);

        // The derived label is always checkable against the list's own words, so the description must survive.
        foreach (var build in release.Builds.Where(b => b.Kind == SqlBuildKind.CumulativeUpdate))
            Assert.False(string.IsNullOrWhiteSpace(build.Description));
    }

    [Fact]
    public void LatestCumulativeUpdate_SkipsWithdrawnBuilds()
    {
        foreach (var release in SqlBuildCatalog.Releases)
        {
            if (release.LatestCumulativeUpdate == null) continue;

            Assert.False(release.LatestCumulativeUpdate.Withdrawn);
            Assert.Equal(SqlBuildKind.CumulativeUpdate, release.LatestCumulativeUpdate.Kind);
        }
    }

    [Fact]
    public void Snapshot_KnowsSomeBuildsWereWithdrawn()
    {
        // Withdrawn is carried in a CVE-styled chip on the source page rather than a column of its own, so it
        // is easy to parse away by accident — as it was until this was noticed.
        Assert.Contains(SqlBuildCatalog.Releases.SelectMany(r => r.Builds), b => b.Withdrawn);
    }

    // =====================================================================================================
    // Lookup
    // =====================================================================================================

    [Fact]
    public void Lookup_IdentifiesAKnownBuildExactly()
    {
        // SQL Server 2022 RTM. A shipped RTM build number is not going to change.
        var match = SqlBuildCatalog.Lookup("16.0.1000.6", AsOf);

        Assert.NotNull(match.Release);
        Assert.Equal("SQL Server 2022", match.Release.Name);
        Assert.NotNull(match.Exact);
        Assert.Equal(SqlBuildKind.Rtm, match.Exact.Kind);
        Assert.False(match.IsLatestKnown);
        Assert.False(match.NewerThanCatalog);
        Assert.True(match.NewerBuilds > 0);
        Assert.True(match.NewerCumulativeUpdates > 0);
    }

    [Fact]
    public void Lookup_ReportsTheNewestListedBuildAsTheNewestListed()
    {
        foreach (string key in new[] { "15.0", "16.0", "17.0" })
        {
            var release = Release(key);
            var match = SqlBuildCatalog.Lookup(release.LatestBuild.Build, AsOf);

            Assert.NotNull(match.Exact);
            Assert.Equal(0, match.NewerBuilds);
            Assert.True(match.IsLatestKnown);

            // Being the newest listed build is not the same as being above the list, and the tab words the two
            // differently — one is "you are current as at the snapshot", the other is "the snapshot is stale".
            Assert.False(match.NewerThanCatalog);
        }
    }

    /// <summary>
    /// The case that matters most in practice, because it happens to every install of this extension the moment
    /// Microsoft ships a CU: the server is ahead of the snapshot. That must never read as "up to date".
    /// </summary>
    [Fact]
    public void Lookup_FlagsABuildAboveEverythingListedAsBeyondTheSnapshot()
    {
        var release = Release("16.0");
        var latest = release.LatestBuild.Version;
        string future = $"{latest.Major}.{latest.Minor}.{latest.Build + 10}.1";

        var match = SqlBuildCatalog.Lookup(future, AsOf);

        Assert.Equal(release, match.Release);
        Assert.True(match.NewerThanCatalog);
        Assert.False(match.IsLatestKnown);
        Assert.Null(match.Exact);
        Assert.Equal(0, match.NewerBuilds);

        // It still identifies the level as "at least" the newest thing listed.
        Assert.Equal(release.LatestBuild, match.ClosestBelow);
        Assert.Equal(release.LatestBuild, match.Best);
    }

    [Fact]
    public void Lookup_FallsBackToTheClosestBuildBelowAnUnlistedOne()
    {
        var release = Release("16.0");

        // Between two listed builds: take a listed one and drop the revision below it, which the list will not
        // contain but which is above every earlier build.
        var reference = release.Builds.First(b => b.Version.Revision > 0 && b.Kind == SqlBuildKind.CumulativeUpdate);
        string unlisted = $"{reference.Version.Major}.{reference.Version.Minor}.{reference.Version.Build}.{reference.Version.Revision - 1}";

        var match = SqlBuildCatalog.Lookup(unlisted, AsOf);

        Assert.Null(match.Exact);
        Assert.NotNull(match.ClosestBelow);
        Assert.True(match.ClosestBelow.Version < Parse(unlisted));
        Assert.False(match.IsLatestKnown);

        // Everything above it, including the build it sits just below, counts as newer.
        Assert.Contains(reference, release.Builds.Where(b => b.Version > Parse(unlisted)));
        Assert.True(match.NewerBuilds > 0);
    }

    [Fact]
    public void Lookup_SurvivesAVersionItHasNeverHeardOf()
    {
        var match = SqlBuildCatalog.Lookup("99.0.1000.1", AsOf);

        Assert.Null(match.Release);
        Assert.Null(match.Exact);
        Assert.Null(match.Best);
        Assert.False(match.NewerThanCatalog);
        Assert.False(match.IsLatestKnown);
        Assert.Equal(SqlSupportPhase.Unknown, match.Phase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a version")]
    public void Lookup_NeverReturnsNullForUnusableInput(string productVersion)
    {
        var match = SqlBuildCatalog.Lookup(productVersion, AsOf);

        Assert.NotNull(match);
        Assert.Null(match.Release);
        Assert.True(match.Version.IsEmpty);
    }

    // =====================================================================================================
    // Support lifecycle
    // =====================================================================================================

    [Fact]
    public void SupportPhase_MovesFromMainstreamToExtendedToEndedAroundTheListedDates()
    {
        var release = SqlBuildCatalog.Releases.First(r =>
            r.Key == "16.0" && r.MainstreamSupportEnd != null && r.ExtendedSupportEnd != null);

        string build = release.LatestBuild.Build;

        var during = SqlBuildCatalog.Lookup(build, release.MainstreamSupportEnd.Value.AddDays(-1));
        Assert.Equal(SqlSupportPhase.Mainstream, during.Phase);
        Assert.Equal(1, during.DaysUntilSupportEnds);

        // The end date itself is still supported — support ends after it, not on it.
        Assert.Equal(SqlSupportPhase.Mainstream, SqlBuildCatalog.Lookup(build, release.MainstreamSupportEnd.Value).Phase);

        var extended = SqlBuildCatalog.Lookup(build, release.MainstreamSupportEnd.Value.AddDays(1));
        Assert.Equal(SqlSupportPhase.Extended, extended.Phase);
        Assert.True(extended.DaysUntilSupportEnds > 0);

        Assert.Equal(SqlSupportPhase.Extended, SqlBuildCatalog.Lookup(build, release.ExtendedSupportEnd.Value).Phase);

        var ended = SqlBuildCatalog.Lookup(build, release.ExtendedSupportEnd.Value.AddDays(1));
        Assert.Equal(SqlSupportPhase.OutOfSupport, ended.Phase);
        Assert.Equal(-1, ended.DaysUntilSupportEnds);
    }

    [Fact]
    public void SupportPhase_ReportsTheLongOutOfSupportReleasesAsSuch()
    {
        // SQL Server 2008's extended support ended in 2019; nothing about that is going to change.
        var match = SqlBuildCatalog.Lookup("10.0.6000.29", AsOf);

        Assert.Equal("SQL Server 2008", match.Release.Name);
        Assert.Equal(SqlSupportPhase.OutOfSupport, match.Phase);
        Assert.True(match.DaysUntilSupportEnds < 0);
    }

    [Fact]
    public void SupportPhase_IsUnknownRatherThanGuessedWhenNoDatesAreListed()
    {
        // Only a release with no mainstream date at all can be Unknown; every release the snapshot carries has
        // one, so the guard is asserted on the code path rather than on data that happens to exercise it.
        foreach (var release in SqlBuildCatalog.Releases)
        {
            if (release.MainstreamSupportEnd != null) continue;

            var match = SqlBuildCatalog.Lookup(release.RtmBuild, AsOf);
            Assert.Equal(SqlSupportPhase.Unknown, match.Phase);
        }
    }

    private static SqlVersion Parse(string text)
    {
        SqlVersion version;
        Assert.True(SqlVersion.TryParse(text, out version), "could not parse " + text);
        return version;
    }
}
