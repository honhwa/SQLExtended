using System;
using SQLExtended.Monitoring.Jobs;
using Xunit;

namespace SQLExtended.Tests.Monitoring;

/// <summary>
/// Agent's integer date and duration encoding is the one place in the jobs dashboard where getting the
/// arithmetic wrong produces plausible-looking numbers rather than an error, so it is covered here.
/// </summary>
public class JobValueParserTests
{
    // --- DurationToSeconds: run_duration is HHMMSS packed into an int, with hours uncapped ---

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]                  // 00:00:01
    [InlineData(59, 59)]
    [InlineData(100, 60)]               // 00:01:00
    [InlineData(130, 90)]               // 00:01:30
    [InlineData(10000, 3600)]           // 01:00:00
    [InlineData(13045, 5445)]           // 01:30:45
    [InlineData(240000, 86400)]         // 24:00:00 — hours are not a time of day
    [InlineData(1000000, 360000)]       // 100:00:00
    public void DurationToSeconds_decodes_HHMMSS(int runDuration, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, JobValueParser.DurationToSeconds(runDuration));
    }

    [Fact]
    public void DurationToSeconds_clamps_negative_to_zero()
    {
        Assert.Equal(0, JobValueParser.DurationToSeconds(-1));
    }

    // --- ToDateTime: run_date is YYYYMMDD, run_time is HHMMSS ---

    [Fact]
    public void ToDateTime_combines_date_and_time()
    {
        Assert.Equal(new DateTime(2026, 7, 27, 14, 5, 9), JobValueParser.ToDateTime(20260727, 140509));
    }

    [Fact]
    public void ToDateTime_handles_midnight()
    {
        Assert.Equal(new DateTime(2026, 7, 27, 0, 0, 0), JobValueParser.ToDateTime(20260727, 0));
    }

    [Theory]
    [InlineData(0, 0)]                  // the zero date Agent writes when a job has never run
    [InlineData(-1, 0)]
    [InlineData(20261327, 0)]           // month 13
    [InlineData(20260732, 0)]           // day 32
    [InlineData(20260229, 0)]           // 2026 is not a leap year
    [InlineData(17000101, 0)]           // before datetime's floor
    [InlineData(20260727, 250000)]      // hour 25
    [InlineData(20260727, 106000)]      // minute 60
    [InlineData(20260727, 100060)]      // second 60
    public void ToDateTime_returns_null_for_impossible_values(int runDate, int runTime)
    {
        Assert.Null(JobValueParser.ToDateTime(runDate, runTime));
    }

    [Fact]
    public void ToDateTime_accepts_a_real_leap_day()
    {
        Assert.Equal(new DateTime(2024, 2, 29, 23, 59, 59), JobValueParser.ToDateTime(20240229, 235959));
    }

    // --- ParseCategories ---

    [Fact]
    public void ParseCategories_trims_and_drops_blanks()
    {
        var result = JobValueParser.ParseCategories(" Report Server , ,Report Server HTML,");
        Assert.Equal(new[] { "Report Server", "Report Server HTML" }, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    public void ParseCategories_returns_empty_for_nothing_configured(string input)
    {
        Assert.Empty(JobValueParser.ParseCategories(input));
    }
}
