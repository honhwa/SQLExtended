// -----------------------------------------------------------------------------
// Vendored from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Core/Formatting/CompletionTimeFormatter.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md. DO NOT EDIT — sync from upstream instead.
// -----------------------------------------------------------------------------
using System.Globalization;
using StatisticsParser.Core.Models;

namespace StatisticsParser.Core.Formatting;

// Renders a completion-time row as the SSMS client emitted it: the label verbatim,
// followed by the timestamp. The completion-time line is client-side text, so the date
// follows the SSMS UI display language rather than the SQL Server session language.
public static class CompletionTimeFormatter
{
    /// <summary>
    /// Returns "<label><date>" for a completion-time row. The date follows the SSMS UI
    /// display language (CurrentUICulture) so it matches the verbatim, SSMS-emitted label.
    /// </summary>
    public static string Format(CompletionTimeRow row, bool convertToLocalTime)
    {
        var dt = convertToLocalTime ? row.Timestamp.ToLocalTime() : row.Timestamp;
        var culture = UiCulture();
        var pattern = culture.DateTimeFormat.ShortDatePattern + " HH:mm:ss.fffffff zzz";
        var label = string.IsNullOrEmpty(row.Label) ? "Completion time: " : row.Label;
        return label + dt.ToString(pattern, culture);
    }

    // CurrentUICulture is the SSMS display language (CurrentCulture, by contrast, is the
    // Windows regional-format setting). Under .NET Framework a *neutral* culture throws on
    // DateTimeFormat access, so coerce it to a specific culture first.
    private static CultureInfo UiCulture()
    {
        var ui = CultureInfo.CurrentUICulture;
        return ui.IsNeutralCulture ? CultureInfo.CreateSpecificCulture(ui.Name) : ui;
    }
}
