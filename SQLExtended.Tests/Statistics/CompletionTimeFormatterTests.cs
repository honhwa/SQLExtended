// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core.Tests/CompletionTimeFormatterTests.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
using System;
using System.Globalization;
using System.Threading;
using StatisticsParser.Core.Formatting;
using StatisticsParser.Core.Models;
using Xunit;

namespace StatisticsParser.Core.Tests;

public class CompletionTimeFormatterTests
{
    // 2026-05-19T14:18:00.0765943-04:00 — a fixed offset so the rendered date/time does not
    // depend on the test host's time zone (tests use convertToLocalTime: false unless the
    // local-time conversion itself is under test).
    private static CompletionTimeRow Row(string label = "Completion time: ") => new()
    {
        Timestamp = new DateTimeOffset(2026, 5, 19, 14, 18, 0, TimeSpan.FromHours(-4)).AddTicks(765943),
        Label = label,
    };

    [Fact]
    public void Format_ItalianUiCulture_RendersDayMonthYear()
    {
        WithUiCulture("it-IT", () =>
            Assert.Equal(
                "Ora di completamento: 19/05/2026 14:18:00.0765943 -04:00",
                CompletionTimeFormatter.Format(Row("Ora di completamento: "), convertToLocalTime: false)));
    }

    [Fact]
    public void Format_EnglishUiCulture_RendersMonthDayYear()
    {
        WithUiCulture("en-US", () =>
            Assert.Equal(
                "Completion time: 5/19/2026 14:18:00.0765943 -04:00",
                CompletionTimeFormatter.Format(Row(), convertToLocalTime: false)));
    }

    [Fact]
    public void Format_NeutralUiCulture_DoesNotThrowAndUsesItalianDate()
    {
        // A neutral culture throws on DateTimeFormat access under .NET Framework; the
        // formatter must coerce it to a specific culture first.
        WithUiCulture("it", () =>
        {
            var text = CompletionTimeFormatter.Format(Row("Ora di completamento: "), convertToLocalTime: false);
            Assert.Equal("Ora di completamento: 19/05/2026 14:18:00.0765943 -04:00", text);
        });
    }

    [Fact]
    public void Format_ConvertToLocalTimeFalse_KeepsOriginalOffset()
    {
        WithUiCulture("en-US", () =>
            Assert.Contains(" -04:00", CompletionTimeFormatter.Format(Row(), convertToLocalTime: false)));
    }

    [Fact]
    public void Format_ConvertToLocalTimeTrue_UsesLocalOffset()
    {
        WithUiCulture("en-US", () =>
        {
            var row = Row();
            var expectedLocal = row.Timestamp.ToLocalTime();
            var text = CompletionTimeFormatter.Format(row, convertToLocalTime: true);
            Assert.Contains(expectedLocal.ToString("HH:mm:ss.fffffff zzz", CultureInfo.InvariantCulture), text);
        });
    }

    [Fact]
    public void Format_EmptyLabel_FallsBackToEnglishLabel()
    {
        WithUiCulture("en-US", () =>
            Assert.StartsWith("Completion time: ", CompletionTimeFormatter.Format(Row(label: ""), convertToLocalTime: false)));
    }

    [Fact]
    public void Format_NonEmptyLabel_EchoedVerbatim()
    {
        WithUiCulture("en-US", () =>
            Assert.StartsWith("Ora di completamento: ", CompletionTimeFormatter.Format(Row("Ora di completamento: "), convertToLocalTime: false)));
    }

    private static void WithUiCulture(string name, Action action)
    {
        var t = Thread.CurrentThread;
        var prevUiCulture = t.CurrentUICulture;
        t.CurrentUICulture = new CultureInfo(name);
        try { action(); }
        finally { t.CurrentUICulture = prevUiCulture; }
    }
}
