// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Formatting/PercentFormatter.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
using System.Globalization;

namespace StatisticsParser.Core.Formatting;

public static class PercentFormatter
{
    public static string FormatPercent(double value) =>
        value.ToString("F3", CultureInfo.InvariantCulture) + "%";
}
