// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Formatting/TimeFormatter.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
using System;
using System.Globalization;

namespace StatisticsParser.Core.Formatting;

public static class TimeFormatter
{
    public static string FormatMs(int ms) =>
        TimeSpan.FromMilliseconds(ms).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
}
