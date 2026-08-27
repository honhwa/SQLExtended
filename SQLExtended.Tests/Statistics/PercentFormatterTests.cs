// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core.Tests/PercentFormatterTests.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
using System.Globalization;
using System.Threading;
using StatisticsParser.Core.Formatting;
using Xunit;

namespace StatisticsParser.Core.Tests;

public class PercentFormatterTests
{
    [Theory]
    [InlineData(0.0, "0.000%")]
    [InlineData(7.692, "7.692%")]
    [InlineData(61.538, "61.538%")]
    [InlineData(100.0, "100.000%")]
    [InlineData(12.3456, "12.346%")]
    public void FormatPercent_ReturnsThreeDecimalsWithSign(double value, string expected)
    {
        Assert.Equal(expected, PercentFormatter.FormatPercent(value));
    }

    [Fact]
    public void FormatPercent_IgnoresCurrentCulture()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("7.692%", PercentFormatter.FormatPercent(7.692));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
