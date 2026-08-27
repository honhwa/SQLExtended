// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Parsing/NumberSeparators.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
namespace StatisticsParser.Core.Parsing;

/// <summary>
/// Locale grouping/decimal separators for rendering parsed numbers. Sourced from the upstream
/// languagetext-*.json "numberformat" block. Used by the rendering layer for display only — the
/// parser itself reads SQL Server's culture-invariant integers and never formats with these.
/// </summary>
public sealed class NumberSeparators
{
    public NumberSeparators(string thousandSeparator, string decimalSeparator)
    {
        ThousandSeparator = thousandSeparator;
        DecimalSeparator = decimalSeparator;
    }

    public string ThousandSeparator { get; }
    public string DecimalSeparator { get; }
}
